using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using DijitalKimlik.Crypto;
using DijitalKimlik.Crypto.Jose;
using DijitalKimlik.Crypto.SelectiveDisclosure;

using Microsoft.Extensions.Options;

namespace DijitalKimlik.Wallet.Credentials;

/// <summary>Teklif ekranında gösterilen özet: kim, ne veriyor, PIN gerekiyor mu?</summary>
public sealed record OfferPreview(
    string Issuer,
    string IssuerName,
    string DisplayName,
    string BackgroundColor,
    string TextColor,
    int? TxCodeLength,
    string? TxCodeDescription,
    IReadOnlyList<string> ClaimLabels);

/// <summary>
/// OpenID4VCI istemcisi (pre-authorized code akışı): teklifi okur, düzenleyicinin metadata'sını
/// indirir, PIN ile token alır, anahtar üretir, kanıt imzalar ve kimlik bilgisini alıp doğrular.
/// </summary>
/// <remarks>
/// <see cref="HttpClient"/> DI'dan gelir (typed client). Her istekte <c>new HttpClient()</c> soket
/// tükenmesine yol açar; IHttpClientFactory bağlantı havuzunu yönetir.
/// </remarks>
public sealed class IssuanceClient(HttpClient http, IOptions<WalletOptions> options, TimeProvider clock)
{
    private const string PreAuthorizedCodeGrant = "urn:ietf:params:oauth:grant-type:pre-authorized_code";
    private const string ProofType = "openid4vci-proof+jwt";

    public async Task<OfferPreview> PreviewAsync(string offerJson, CancellationToken cancellationToken)
    {
        var offer = ParseOffer(offerJson);
        var issuer = await IssuerMetadata.FetchAsync(http, offer.Issuer, offer.ConfigurationId, cancellationToken);
        return new OfferPreview(
            offer.Issuer,
            issuer.Name,
            issuer.DisplayName,
            issuer.BackgroundColor,
            issuer.TextColor,
            offer.TxCode is null ? null : (int?)JoseJson.GetInt64(offer.TxCode, "length"),
            offer.TxCode is null ? null : JoseJson.GetString(offer.TxCode, "description"),
            [.. issuer.ClaimLabels.Values]);
    }

    public async Task<StoredCredential> ReceiveAsync(string offerJson, string? txCode, CancellationToken cancellationToken)
    {
        var offer = ParseOffer(offerJson);
        var issuer = await IssuerMetadata.FetchAsync(http, offer.Issuer, offer.ConfigurationId, cancellationToken);
        if (offer.TxCode is not null && string.IsNullOrWhiteSpace(txCode))
        {
            throw new WalletException("Bu teklif için memurun verdiği PIN gerekiyor.");
        }

        // 1. Token: kod + PIN. Düzenleyici kodu tek kullanımlık sayar.
        var token = await PostAsync(issuer.TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = PreAuthorizedCodeGrant,
            ["pre-authorized_code"] = offer.PreAuthorizedCode,
            ["tx_code"] = txCode?.Trim() ?? string.Empty,
        }), accessToken: null, cancellationToken);
        var accessToken = JoseJson.GetString(token, "access_token")
            ?? throw new WalletException("Düzenleyici erişim token'ı vermedi.");

        // 2. Nonce: kanıtın bir kez kullanılabilmesi için düzenleyicinin verdiği rastgele değer.
        var nonceResponse = await PostAsync(issuer.NonceEndpoint, content: null, accessToken: null, cancellationToken);
        var nonce = JoseJson.GetString(nonceResponse, "c_nonce")
            ?? throw new WalletException("Düzenleyici c_nonce vermedi.");

        // 3. Anahtar ve kanıt: kimlik bilgisi bu anahtara bağlanacak.
        using var holderKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var holderJwk = EcJwk.FromPublicKey(holderKey);
        var proof = Jws.Sign(
            new JsonObject { ["typ"] = ProofType, ["jwk"] = holderJwk.DeepClone() },
            new JsonObject { ["aud"] = offer.Issuer, ["iat"] = clock.GetUtcNow().ToUnixTimeSeconds(), ["nonce"] = nonce },
            holderKey);

        var request = new JsonObject
        {
            ["credential_configuration_id"] = offer.ConfigurationId,
            ["proofs"] = new JsonObject { ["jwt"] = new JsonArray(proof) },
        };
        var response = await PostAsync(
            issuer.CredentialEndpoint,
            new StringContent(JoseJson.Serialize(request), Encoding.UTF8, "application/json"),
            accessToken,
            cancellationToken);
        var credential = (string?)response["credentials"]?[0]?["credential"]
            ?? throw new WalletException("Düzenleyicinin yanıtında kimlik bilgisi yok.");

        // 4. Saklamadan önce doğrula: imza düzenleyicinin, alanlar tutarlı, anahtar bizim.
        await VerifyReceivedAsync(credential, offer.Issuer, issuer.Vct, holderJwk, cancellationToken);

        return new StoredCredential
        {
            Id = WalletStore.NewCredentialId(),
            Issuer = offer.Issuer,
            IssuerName = issuer.Name,
            Vct = issuer.Vct,
            DisplayName = issuer.DisplayName,
            BackgroundColor = issuer.BackgroundColor,
            TextColor = issuer.TextColor,
            ClaimLabels = new Dictionary<string, string>(issuer.ClaimLabels),
            Credential = credential,
            HolderKeyPem = holderKey.ExportECPrivateKeyPem(),
            ReceivedAt = clock.GetUtcNow(),
        };
    }

    private async Task VerifyReceivedAsync(string credential, string issuer, string vct, JsonObject holderJwk, CancellationToken cancellationToken)
    {
        try
        {
            var sdJwt = SdJwt.Parse(credential);
            using var issuerKey = await IssuerKeys.ResolveAsync(http, issuer, JoseJson.GetString(sdJwt.IssuerJwt.Header, "kid"), cancellationToken);
            var verified = SdJwtVc.Verify(sdJwt, issuerKey, clock.GetUtcNow(), keyBinding: null);
            if (JoseJson.GetString(verified.Claims, "iss") != issuer || JoseJson.GetString(verified.Claims, "vct") != vct)
            {
                throw new WalletException("Kimlik bilgisinin düzenleyicisi ya da türü teklifle uyuşmuyor.");
            }

            if (verified.HolderJwk is null || EcJwk.Thumbprint(verified.HolderJwk) != EcJwk.Thumbprint(holderJwk))
            {
                throw new WalletException("Kimlik bilgisi bu cüzdanın anahtarına bağlanmamış.");
            }
        }
        catch (CryptoValidationException ex)
        {
            throw new WalletException($"Kimlik bilgisi doğrulanamadı ({ex.Code}).");
        }
    }

    private CredentialOffer ParseOffer(string offerJson)
    {
        JsonObject offer;
        try
        {
            offer = JsonNode.Parse(offerJson) as JsonObject ?? throw new WalletException("Teklif bir JSON nesnesi değil.");
        }
        catch (JsonException)
        {
            throw new WalletException("Teklif okunamadı.");
        }

        var issuer = JoseJson.GetString(offer, "credential_issuer") ?? throw new WalletException("Teklifte düzenleyici adresi yok.");
        if (!options.Value.TrustedIssuers.Contains(issuer, StringComparer.Ordinal))
        {
            throw new WalletException($"{issuer} bu cüzdanın güvendiği düzenleyiciler arasında değil.");
        }

        var configurationId = (string?)offer["credential_configuration_ids"]?[0]
            ?? throw new WalletException("Teklifte kimlik bilgisi türü yok.");
        var grant = offer["grants"]?[PreAuthorizedCodeGrant] as JsonObject
            ?? throw new WalletException("Bu cüzdan yalnız pre-authorized code tekliflerini destekliyor.");
        var code = JoseJson.GetString(grant, "pre-authorized_code") ?? throw new WalletException("Teklifte kod yok.");
        return new CredentialOffer(issuer, configurationId, code, grant["tx_code"] as JsonObject);
    }

    private async Task<JsonObject> PostAsync(Uri endpoint, HttpContent? content, string? accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = content };
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonObject? json = null;
        try
        {
            json = JsonNode.Parse(body) as JsonObject;
        }
        catch (JsonException)
        {
            // Aşağıda genel hata iletisi verilir.
        }

        if (!response.IsSuccessStatusCode)
        {
            var error = json is null ? null : JoseJson.GetString(json, "error");
            throw new WalletException(error switch
            {
                "invalid_grant" => "PIN yanlış ya da teklif geçersiz: " + (JoseJson.GetString(json!, "error_description") ?? error),
                null => $"Düzenleyici isteği reddetti (HTTP {(int)response.StatusCode}).",
                _ => $"Düzenleyici isteği reddetti: {error}.",
            });
        }

        return json ?? throw new WalletException("Düzenleyicinin yanıtı JSON değil.");
    }

    private sealed record CredentialOffer(string Issuer, string ConfigurationId, string PreAuthorizedCode, JsonObject? TxCode);
}
