using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

using DijitalKimlik.Crypto;
using DijitalKimlik.Crypto.Jose;
using DijitalKimlik.Crypto.SelectiveDisclosure;
using DijitalKimlik.Crypto.StatusLists;

using Microsoft.Extensions.Options;

namespace DijitalKimlik.Verifier.Presentations;

public sealed record VerificationCheck(string Name, bool Passed, string Detail);

public sealed record DisclosedClaim(string Name, string Label, string Value);

/// <summary>Doğrulama zincirinin sonucu: her adım ayrı görünür, ilk başarısız adımda durulur.</summary>
public sealed record VerificationResult(
    bool Success,
    bool Declined,
    IReadOnlyList<VerificationCheck> Checks,
    IReadOnlyList<DisclosedClaim> Claims,
    int HiddenDigests)
{
    public static VerificationResult Rejected(string reason) =>
        new(Success: false, Declined: true, [new VerificationCheck("Kullanıcı kararı", false, reason)], [], 0);
}

/// <summary>
/// Sunumu doğrular. Sıra önemlidir: düzenleyiciye güvenilmeden onun adresine istek atılmaz; imza
/// doğrulanmadan içerikteki hiçbir alana (durum listesi adresi dahil) güvenilmez.
/// </summary>
public sealed class PresentationVerifier(HttpClient http, IOptions<VerifierOptions> options, TimeProvider clock)
{
    /// <summary>KB-JWT'nin kabul edilen en büyük yaşı: sunum birkaç saniye içinde gelmeli.</summary>
    public static readonly TimeSpan MaxPresentationAge = TimeSpan.FromMinutes(5);

    public async Task<VerificationResult> VerifyAsync(Transaction transaction, string? vpToken, CancellationToken cancellationToken)
    {
        var checks = new List<VerificationCheck>();
        VerificationResult Fail(string name, string detail)
        {
            checks.Add(new VerificationCheck(name, false, detail));
            return new VerificationResult(false, false, checks, [], 0);
        }

        // 1. Yanıt biçimi: { "<DCQL id>": ["<SD-JWT>~...~<KB-JWT>"] }
        string presentation;
        try
        {
            if (vpToken is null
                || JsonNode.Parse(vpToken) is not JsonObject token
                || token[Scenario.QueryId] is not JsonArray { Count: 1 } list
                || list[0] is not JsonValue value
                || !value.TryGetValue(out string? text))
            {
                return Fail("Yanıt biçimi", "vp_token, istenen kimlik bilgisi için tek bir sunum içermiyor.");
            }

            presentation = text;
        }
        catch (JsonException)
        {
            return Fail("Yanıt biçimi", "vp_token geçerli JSON değil.");
        }

        SdJwt sdJwt;
        try
        {
            sdJwt = SdJwt.Parse(presentation);
        }
        catch (CryptoValidationException ex)
        {
            return Fail("Yanıt biçimi", $"SD-JWT çözülemedi ({ex.Code}).");
        }

        checks.Add(new VerificationCheck("Yanıt biçimi", true, $"SD-JWT, {sdJwt.Disclosures.Count} disclosure ve KB-JWT"));

        // 2. Düzenleyici güven listesinde mi? (Henüz imza doğrulanmadı: yalnız adrese bakılıyor.)
        var issuer = JoseJson.GetString(sdJwt.IssuerJwt.Payload, "iss");
        if (issuer is null || !options.Value.TrustedIssuers.Contains(issuer, StringComparer.Ordinal))
        {
            return Fail("Güvenilir düzenleyici", $"{issuer ?? "iss yok"} güven listesinde değil.");
        }

        checks.Add(new VerificationCheck("Güvenilir düzenleyici", true, issuer));

        // 3. İmza, özetler, KB-JWT ve süre.
        using var issuerKey = await ResolveIssuerKeyAsync(issuer, JoseJson.GetString(sdJwt.IssuerJwt.Header, "kid"), cancellationToken);
        if (issuerKey is null)
        {
            return Fail("İmza ve özetler", "Düzenleyicinin imza anahtarı alınamadı.");
        }

        var now = clock.GetUtcNow();
        VerifiedSdJwtVc verified;
        try
        {
            verified = SdJwtVc.Verify(sdJwt, issuerKey, now, new KeyBindingRequirement(options.Value.ClientId, transaction.Nonce, MaxPresentationAge));
        }
        catch (CryptoValidationException ex)
        {
            var step = ex.Code switch
            {
                "credential_expired" or "credential_not_yet_valid" => "Geçerlilik süresi",
                "missing_kb_jwt" or "missing_cnf" or "invalid_audience" or "invalid_nonce" or "invalid_sd_hash" or "kb_jwt_expired" => "Cüzdan bağlama (KB-JWT)",
                _ => "İmza ve özetler",
            };
            return Fail(step, $"{ex.Message} ({ex.Code})");
        }

        checks.Add(new VerificationCheck("İmza ve özetler", true, $"ES256, anahtar {JoseJson.GetString(sdJwt.IssuerJwt.Header, "kid")?[..8]}…"));
        checks.Add(new VerificationCheck("Cüzdan bağlama (KB-JWT)", true, "nonce, aud ve sd_hash bu isteğe ait; imza cnf.jwk ile doğrulandı"));
        var exp = JoseJson.GetInt64(verified.Claims, "exp");
        checks.Add(new VerificationCheck("Geçerlilik süresi", true,
            exp is null ? "exp yok" : $"{DateTimeOffset.FromUnixTimeSeconds(exp.Value):dd.MM.yyyy} tarihine kadar"));

        // 4. Tür ve istenen alanlar.
        if (JoseJson.GetString(verified.Claims, "vct") != Scenario.Vct)
        {
            return Fail("Kimlik bilgisi türü", $"Beklenen {Scenario.Vct}.");
        }

        checks.Add(new VerificationCheck("Kimlik bilgisi türü", true, Scenario.Vct));

        var missing = transaction.Scenario.Claims.Where(c => !verified.Claims.ContainsKey(c)).ToList();
        if (missing.Count > 0)
        {
            return Fail("İstenen alanlar", "Açılmayan alanlar: " + string.Join(", ", missing.Select(Scenario.LabelOf)));
        }

        checks.Add(new VerificationCheck("İstenen alanlar", true, $"{transaction.Scenario.Claims.Count} alan açıldı"));

        // 5. İptal ve askı: düzenleyicinin imzaladığı durum listesi.
        var status = await CheckStatusAsync(verified.Claims, issuer, issuerKey, cancellationToken);
        checks.Add(status);
        if (!status.Passed)
        {
            return new VerificationResult(false, false, checks, [], 0);
        }

        // Yalnız istenen alanlar gösterilir; fazladan açılmış bir alan olsa da ekrana ve kayda girmez.
        var claims = transaction.Scenario.Claims
            .Select(c => new DisclosedClaim(c, Scenario.LabelOf(c), Format(verified.Claims[c])))
            .ToList();
        var digests = (sdJwt.IssuerJwt.Payload["_sd"] as JsonArray)?.Count ?? 0;
        return new VerificationResult(true, false, checks, claims, Math.Max(0, digests - sdJwt.Disclosures.Count));
    }

    private async Task<VerificationCheck> CheckStatusAsync(JsonObject claims, string issuer, ECDsa issuerKey, CancellationToken cancellationToken)
    {
        const string name = "İptal durumu (status list)";
        if (claims["status"]?["status_list"] is not JsonObject reference
            || JoseJson.GetInt64(reference, "idx") is not { } index
            || JoseJson.GetString(reference, "uri") is not { } uri)
        {
            return new VerificationCheck(name, false, "Kimlik bilgisinde durum listesi yok.");
        }

        // Liste düzenleyicinin kendi adresinde olmalı: imzalı içerik bile doğrulayıcıyı başka bir adrese
        // istek atmaya yönlendiremesin.
        if (!uri.StartsWith(issuer + "/", StringComparison.Ordinal))
        {
            return new VerificationCheck(name, false, "Durum listesi düzenleyicinin adresinde değil.");
        }

        try
        {
            using var response = await http.GetAsync(new Uri(uri), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new VerificationCheck(name, false, $"Durum listesi alınamadı (HTTP {(int)response.StatusCode}).");
            }

            var jws = Jws.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            jws.RequireType("statuslist+jwt");
            jws.VerifySignature(issuerKey);
            if (JoseJson.GetString(jws.Payload, "sub") != uri)
            {
                return new VerificationCheck(name, false, "Durum listesinin sub alanı adresle uyuşmuyor.");
            }

            if (JoseJson.GetInt64(jws.Payload, "exp") is { } exp && clock.GetUtcNow() >= DateTimeOffset.FromUnixTimeSeconds(exp))
            {
                return new VerificationCheck(name, false, "Durum listesinin süresi dolmuş.");
            }

            var list = jws.Payload["status_list"] as JsonObject;
            var bits = list is null ? null : JoseJson.GetInt64(list, "bits");
            var lst = list is null ? null : JoseJson.GetString(list, "lst");
            if (bits is null || lst is null)
            {
                return new VerificationCheck(name, false, "Durum listesi eksik.");
            }

            var decoded = TokenStatusList.Decode(lst, (int)bits.Value);
            if (index < 0 || index >= decoded.Size)
            {
                return new VerificationCheck(name, false, "Sıra numarası listenin dışında.");
            }

            return decoded[(int)index] switch
            {
                0 => new VerificationCheck(name, true, $"Geçerli (sıra {index}, {decoded.Size} kayıtlık liste)"),
                1 => new VerificationCheck(name, false, "İptal edilmiş: düzenleyici bu kimlik bilgisini geri aldı."),
                2 => new VerificationCheck(name, false, "Askıda: düzenleyici kimlik bilgisini geçici olarak durdurdu."),
                var other => new VerificationCheck(name, false, $"Bilinmeyen durum değeri: {other}."),
            };
        }
        catch (CryptoValidationException ex)
        {
            return new VerificationCheck(name, false, $"Durum listesi doğrulanamadı ({ex.Code}).");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or ArgumentException)
        {
            return new VerificationCheck(name, false, "Durum listesine ulaşılamadı.");
        }
    }

    /// <summary>Düzenleyicinin açık anahtarı <c>/.well-known/jwt-vc-issuer</c> adresinden (SD-JWT VC §4).</summary>
    private async Task<ECDsa?> ResolveIssuerKeyAsync(string issuer, string? kid, CancellationToken cancellationToken)
    {
        try
        {
            var origin = new Uri(issuer);
            var wellKnown = new Uri(origin.GetLeftPart(UriPartial.Authority) + "/.well-known/jwt-vc-issuer" + origin.AbsolutePath.TrimEnd('/'));
            using var response = await http.GetAsync(wellKnown, cancellationToken);
            if (!response.IsSuccessStatusCode
                || JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken)) is not JsonObject metadata
                || JoseJson.GetString(metadata, "issuer") != issuer)
            {
                return null;
            }

            var key = (metadata["jwks"]?["keys"] as JsonArray)?.OfType<JsonObject>()
                .FirstOrDefault(k => kid is not null && JoseJson.GetString(k, "kid") == kid);
            return key is null ? null : EcJwk.ToPublicKey(key);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or CryptoValidationException or UriFormatException)
        {
            return null;
        }
    }

    private static string Format(JsonNode? value) => value switch
    {
        JsonValue v when v.TryGetValue(out bool flag) => flag ? "Evet" : "Hayır",
        JsonValue v when v.TryGetValue(out string? text) => text ?? string.Empty,
        null => string.Empty,
        _ => value.ToJsonString(),
    };
}
