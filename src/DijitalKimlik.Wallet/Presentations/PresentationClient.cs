using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

using DijitalKimlik.Crypto.Jose;
using DijitalKimlik.Crypto.SelectiveDisclosure;
using DijitalKimlik.Wallet.Credentials;

namespace DijitalKimlik.Wallet.Presentations;

/// <summary>
/// Sunumu hazırlar ve doğrulayıcıya gönderir (OpenID4VP <c>direct_post</c>). Yalnız istenen alanların
/// disclosure'ları gider; KB-JWT sunumu bu doğrulayıcıya (<c>aud</c>) ve bu isteğe (<c>nonce</c>) bağlar.
/// </summary>
public sealed class PresentationClient(HttpClient http, TimeProvider clock)
{
    /// <summary>İstenen tür ve alanların hepsi bu kimlik bilgisinde var mı?</summary>
    public static bool Matches(StoredCredential credential, RequestedCredential request)
    {
        var names = credential.Claims().Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        return request.VctValues.Contains(credential.Vct, StringComparer.Ordinal) && request.Claims.All(names.Contains);
    }

    /// <returns>Doğrulayıcının tarayıcıyı göndermek istediği adres; yoksa <see langword="null"/>.</returns>
    public async Task<Uri?> RespondAsync(PresentationRequest request, StoredCredential credential, CancellationToken cancellationToken)
    {
        if (!Matches(credential, request.Credential))
        {
            throw new WalletException("Seçilen kimlik bilgisi istenen alanları içermiyor.");
        }

        using var holderKey = ECDsa.Create();
        holderKey.ImportFromPem(credential.HolderKeyPem);
        var presentation = SdJwtVc.Present(
            credential.Credential, request.Credential.Claims, holderKey, request.ClientId, request.Nonce, clock.GetUtcNow());

        // OpenID4VP 1.0 §8.1: vp_token, DCQL kimliğinden sunum dizisine giden bir JSON nesnesidir.
        var vpToken = JoseJson.Serialize(new JsonObject { [request.Credential.QueryId] = new JsonArray(presentation) });
        return await PostAsync(request, new Dictionary<string, string> { ["vp_token"] = vpToken, ["state"] = request.State }, cancellationToken);
    }

    public Task<Uri?> DeclineAsync(PresentationRequest request, CancellationToken cancellationToken) =>
        PostAsync(request, new Dictionary<string, string>
        {
            ["error"] = "access_denied",
            ["error_description"] = "Kullanıcı paylaşmayı reddetti.",
            ["state"] = request.State,
        }, cancellationToken);

    private async Task<Uri?> PostAsync(PresentationRequest request, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await http.PostAsync(request.ResponseUri, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new WalletException($"Doğrulayıcı yanıtı kabul etmedi (HTTP {(int)response.StatusCode}).");
        }

        JsonObject? body;
        try
        {
            body = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken)) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }

        // Tarayıcı yalnız doğrulayıcının kendi sitesine yönlendirilir (açık yönlendirme olmasın).
        return body is not null
            && JoseJson.GetString(body, "redirect_uri") is { } redirect
            && Uri.TryCreate(redirect, UriKind.Absolute, out var uri)
            && string.Equals(uri.GetLeftPart(UriPartial.Authority), request.ResponseUri.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase)
            ? uri
            : null;
    }
}
