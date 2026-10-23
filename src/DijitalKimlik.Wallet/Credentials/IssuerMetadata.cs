using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

using DijitalKimlik.Crypto.Jose;

namespace DijitalKimlik.Wallet.Credentials;

/// <summary>Düzenleyici metadata'sından cüzdanın ihtiyaç duyduğu kısım (OpenID4VCI §12.2).</summary>
public sealed record IssuerMetadata(
    string Name,
    Uri TokenEndpoint,
    Uri NonceEndpoint,
    Uri CredentialEndpoint,
    string Vct,
    string DisplayName,
    string BackgroundColor,
    string TextColor,
    IReadOnlyDictionary<string, string> ClaimLabels)
{
    public static async Task<IssuerMetadata> FetchAsync(HttpClient http, string issuer, string configurationId, CancellationToken cancellationToken)
    {
        var metadata = await GetJsonAsync(http, WellKnown(issuer, "openid-credential-issuer"), cancellationToken);

        // §12.2.2: metadata'daki kimlik, istenen adresle birebir aynı olmalı; değilse metadata kullanılmaz.
        if (JoseJson.GetString(metadata, "credential_issuer") != issuer)
        {
            throw new WalletException("Düzenleyici metadata'sı başka bir adrese ait.");
        }

        var configuration = metadata["credential_configurations_supported"]?[configurationId] as JsonObject
            ?? throw new WalletException("Düzenleyici bu kimlik bilgisi türünü sunmuyor.");
        if (JoseJson.GetString(configuration, "format") != "dc+sd-jwt")
        {
            throw new WalletException("Bu cüzdan yalnız SD-JWT VC (dc+sd-jwt) biçimini destekliyor.");
        }

        // authorization_servers yoksa düzenleyici kendi yetkilendirme sunucusudur.
        var authorizationServer = (string?)metadata["authorization_servers"]?[0] ?? issuer;
        var asMetadata = await GetJsonAsync(http, WellKnown(authorizationServer, "oauth-authorization-server"), cancellationToken);

        var display = Localized(configuration["credential_metadata"]?["display"] as JsonArray);
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var claim in configuration["credential_metadata"]?["claims"] as JsonArray ?? new JsonArray())
        {
            if (claim?["path"] is JsonArray { Count: 1 } path && (string?)path[0] is { } name
                && Localized(claim["display"] as JsonArray) is { } label && JoseJson.GetString(label, "name") is { } text)
            {
                labels[name] = text;
            }
        }

        return new IssuerMetadata(
            JoseJson.GetString(Localized(metadata["display"] as JsonArray) ?? new JsonObject(), "name") ?? issuer,
            SameOrigin(authorizationServer, JoseJson.GetString(asMetadata, "token_endpoint")),
            SameOrigin(issuer, JoseJson.GetString(metadata, "nonce_endpoint")),
            SameOrigin(issuer, JoseJson.GetString(metadata, "credential_endpoint")),
            JoseJson.GetString(configuration, "vct") ?? throw new WalletException("Metadata'da vct yok."),
            (display is null ? null : JoseJson.GetString(display, "name")) ?? configurationId,
            (display is null ? null : JoseJson.GetString(display, "background_color")) ?? "#0b3d5c",
            (display is null ? null : JoseJson.GetString(display, "text_color")) ?? "#ffffff",
            labels);
    }

    internal static Uri WellKnown(string identifier, string name)
    {
        // Yol varsa .well-known, makine adı ile yol arasına girer (RFC 8414 §3.1).
        var uri = new Uri(identifier);
        var path = uri.AbsolutePath.TrimEnd('/');
        return new Uri(uri.GetLeftPart(UriPartial.Authority) + "/.well-known/" + name + path);
    }

    internal static async Task<JsonObject> GetJsonAsync(HttpClient http, Uri uri, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(uri, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new WalletException($"{uri.Authority} yanıt vermedi (HTTP {(int)response.StatusCode}).");
        }

        try
        {
            return JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken)) as JsonObject
                ?? throw new WalletException($"{uri.Authority} yanıtı bir JSON nesnesi değil.");
        }
        catch (JsonException)
        {
            throw new WalletException($"{uri.Authority} yanıtı JSON değil.");
        }
    }

    /// <summary>Metadata'daki uç nokta düzenleyicinin kendi sitesinde olmalı: başka bir adrese istek yönlendirilemez.</summary>
    private static Uri SameOrigin(string identifier, string? endpoint)
    {
        if (endpoint is null || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || !string.Equals(uri.GetLeftPart(UriPartial.Authority), new Uri(identifier).GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase))
        {
            throw new WalletException("Metadata'daki uç nokta düzenleyicinin adresinde değil.");
        }

        return uri;
    }

    /// <summary>Önce Türkçe, yoksa ilk gösterim.</summary>
    private static JsonObject? Localized(JsonArray? displays) =>
        displays?.OfType<JsonObject>().FirstOrDefault(d => JoseJson.GetString(d, "locale") is "tr-TR" or "tr")
        ?? displays?.OfType<JsonObject>().FirstOrDefault();
}

/// <summary>Düzenleyicinin imza anahtarını <c>/.well-known/jwt-vc-issuer</c> adresinden bulur (SD-JWT VC §4).</summary>
public static class IssuerKeys
{
    public static async Task<ECDsa> ResolveAsync(HttpClient http, string issuer, string? kid, CancellationToken cancellationToken)
    {
        var metadata = await IssuerMetadata.GetJsonAsync(http, IssuerMetadata.WellKnown(issuer, "jwt-vc-issuer"), cancellationToken);
        if (JoseJson.GetString(metadata, "issuer") != issuer)
        {
            throw new WalletException("Anahtar metadata'sı başka bir düzenleyiciye ait.");
        }

        var keys = (metadata["jwks"]?["keys"] as JsonArray)?.OfType<JsonObject>().ToList() ?? [];
        var key = keys.FirstOrDefault(k => kid is not null && JoseJson.GetString(k, "kid") == kid)
            ?? (keys.Count == 1 && kid is null ? keys[0] : null)
            ?? throw new WalletException("Düzenleyicinin imza anahtarı bulunamadı.");
        return EcJwk.ToPublicKey(key);
    }
}
