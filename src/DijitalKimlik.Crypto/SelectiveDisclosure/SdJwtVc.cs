using System.Security.Cryptography;
using System.Text.Json.Nodes;

using DijitalKimlik.Crypto.Jose;

namespace DijitalKimlik.Crypto.SelectiveDisclosure;

/// <summary>Sunumda KB-JWT'nin taşıması gereken değerler: doğrulayıcının kimliği ve isteğin nonce'u.</summary>
public sealed record KeyBindingRequirement(string Audience, string Nonce, TimeSpan MaxAge);

/// <summary>Doğrulanmış SD-JWT VC: gösterilen alanlar yerleştirilmiş içerik ve cüzdan anahtarı.</summary>
public sealed record VerifiedSdJwtVc(
    JsonObject Header,
    JsonObject Claims,
    JsonObject? HolderJwk,
    IReadOnlyList<string> DisclosedClaimNames);

/// <summary>
/// SD-JWT VC (draft-ietf-oauth-sd-jwt-vc-19): JOSE <c>typ</c> değeri <c>dc+sd-jwt</c> olan, <c>vct</c>
/// türü taşıyan SD-JWT. Cüzdan aldığı kimlik bilgisini KB-JWT'siz, doğrulayıcı sunumu KB-JWT'li doğrular.
/// </summary>
public static class SdJwtVc
{
    public const string Type = "dc+sd-jwt";

    public static VerifiedSdJwtVc Verify(SdJwt sdJwt, ECDsa issuerKey, DateTimeOffset now, KeyBindingRequirement? keyBinding)
    {
        sdJwt.IssuerJwt.RequireType(Type);
        var claims = sdJwt.VerifyAndProcess(issuerKey);

        if (string.IsNullOrEmpty(JoseJson.GetString(claims, "vct")))
        {
            throw new CryptoValidationException("missing_vct", "Kimlik bilgisinde vct yok.");
        }

        if (JoseJson.GetInt64(claims, "exp") is { } exp && now >= DateTimeOffset.FromUnixTimeSeconds(exp))
        {
            throw new CryptoValidationException("credential_expired", "Kimlik bilgisinin süresi dolmuş.");
        }

        if (JoseJson.GetInt64(claims, "nbf") is { } nbf && now < DateTimeOffset.FromUnixTimeSeconds(nbf))
        {
            throw new CryptoValidationException("credential_not_yet_valid", "Kimlik bilgisi henüz geçerli değil.");
        }

        var holderJwk = claims["cnf"]?["jwk"] as JsonObject;
        if (keyBinding is null)
        {
            // RFC 9901 §7.2: düzenleyiciden KB-JWT'li SD-JWT gelmez; gelirse reddedilir.
            if (sdJwt.KeyBindingJwt is not null)
            {
                throw new CryptoValidationException("unexpected_kb_jwt", "Düzenleyiciden gelen kimlik bilgisi KB-JWT içeremez.");
            }
        }
        else
        {
            // Anahtar bağlamanın gerekip gerekmediği doğrulayıcının kararıdır; KB-JWT'nin gelip
            // gelmemesine bakılarak verilmez (RFC 9901 §7.3, 1. adım).
            var kbJwt = sdJwt.KeyBindingJwt
                ?? throw new CryptoValidationException("missing_kb_jwt", "Sunumda KB-JWT yok.");
            if (holderJwk is null)
            {
                throw new CryptoValidationException("missing_cnf", "Kimlik bilgisi bir cüzdan anahtarına bağlı değil.");
            }

            using var holderKey = EcJwk.ToPublicKey(holderJwk);
            KeyBindingJwt.Verify(kbJwt, sdJwt.WithoutKeyBinding, holderKey, keyBinding.Audience, keyBinding.Nonce, now, keyBinding.MaxAge);
        }

        return new VerifiedSdJwtVc(
            sdJwt.IssuerJwt.Header,
            claims,
            holderJwk,
            [.. sdJwt.Disclosures.Where(d => d.Name is not null).Select(d => d.Name!)]);
    }

    /// <summary>
    /// Cüzdan tarafı: kimlik bilgisinden yalnız istenen üst düzey alanların disclosure'larını seçer
    /// ve KB-JWT ekler. Seçilmeyen alanlar doğrulayıcıya hiç gitmez.
    /// </summary>
    public static string Present(
        string issuedCredential,
        IEnumerable<string> claimNames,
        ECDsa holderKey,
        string audience,
        string nonce,
        DateTimeOffset now)
    {
        var parsed = SdJwt.Parse(issuedCredential);
        if (parsed.KeyBindingJwt is not null)
        {
            throw new CryptoValidationException("unexpected_kb_jwt", "Saklanan kimlik bilgisi KB-JWT içeremez.");
        }

        var wanted = claimNames.ToHashSet(StringComparer.Ordinal);
        var selected = parsed.Disclosures.Where(d => d.Name is not null && wanted.Contains(d.Name));
        var sdJwt = SdJwt.Combine(parsed.IssuerJwt.Compact, selected);
        return sdJwt + KeyBindingJwt.Create(sdJwt, holderKey, audience, nonce, now);
    }
}
