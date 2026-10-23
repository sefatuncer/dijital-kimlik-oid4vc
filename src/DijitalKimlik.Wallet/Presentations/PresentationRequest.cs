using System.Text.Json;
using System.Text.Json.Nodes;

using DijitalKimlik.Crypto.Jose;
using DijitalKimlik.Wallet.Credentials;

using Microsoft.AspNetCore.WebUtilities;

namespace DijitalKimlik.Wallet.Presentations;

/// <summary>DCQL sorgusundaki tek kimlik bilgisi isteği: hangi tür, hangi alanlar.</summary>
public sealed record RequestedCredential(string QueryId, IReadOnlyList<string> VctValues, IReadOnlyList<string> Claims);

/// <summary>
/// Doğrulayıcının yetkilendirme isteği (OpenID4VP 1.0). Bu cüzdan şu biçimi kabul eder:
/// <c>client_id=redirect_uri:...</c> (imzasız istek), <c>response_type=vp_token</c>,
/// <c>response_mode=direct_post</c> ve tek kimlik bilgili, üst düzey alanlı bir <c>dcql_query</c>.
/// </summary>
public sealed record PresentationRequest(string ClientId, Uri ResponseUri, string Nonce, string State, RequestedCredential Credential)
{
    public const string RedirectUriPrefix = "redirect_uri:";

    /// <summary>
    /// İmzasız istekte doğrulayıcının kimliği kanıtlanmamıştır; cüzdan yalnız yanıtın gideceği adresi
    /// gösterebilir. Üretimde istek imzalanır (<c>x509_san_dns:</c> ya da doğrulayıcı onay belgesi).
    /// </summary>
    public string VerifierHost => ResponseUri.Authority;

    /// <summary>Sorgu dizesinden okur; kural dışı her durumda <see cref="WalletException"/> fırlatır.</summary>
    public static PresentationRequest Parse(string queryString)
    {
        var query = QueryHelpers.ParseQuery(queryString);
        string Single(string name) =>
            query.TryGetValue(name, out var values) && values.Count == 1 && !string.IsNullOrEmpty(values[0])
                ? values[0]!
                : throw new WalletException($"İstekte '{name}' yok ya da birden çok kez geçiyor.");

        if (Single("response_type") != "vp_token")
        {
            throw new WalletException("Yalnız response_type=vp_token destekleniyor.");
        }

        if (Single("response_mode") != "direct_post")
        {
            throw new WalletException("Yalnız response_mode=direct_post destekleniyor.");
        }

        var clientId = Single("client_id");
        if (!clientId.StartsWith(RedirectUriPrefix, StringComparison.Ordinal))
        {
            throw new WalletException("Bu cüzdan yalnız redirect_uri: önekli doğrulayıcıları tanıyor.");
        }

        // §5.9.3: redirect_uri öneki ile direct_post'ta yanıt adresi, client_id'deki adresin kendisidir.
        // Böylece istek, yanıtı başka bir adrese yönlendiremez.
        var responseUri = Single("response_uri");
        if (clientId[RedirectUriPrefix.Length..] != responseUri
            || !Uri.TryCreate(responseUri, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("https" or "http"))
        {
            throw new WalletException("response_uri, client_id'deki adresle aynı olmalı.");
        }

        if (query.ContainsKey("request") || query.ContainsKey("request_uri"))
        {
            throw new WalletException("İmzalı istek nesnesi (request, request_uri) bu cüzdanda henüz desteklenmiyor.");
        }

        return new PresentationRequest(clientId, uri, Single("nonce"), Single("state"), ParseDcql(Single("dcql_query")));
    }

    private static RequestedCredential ParseDcql(string json)
    {
        JsonObject dcql;
        try
        {
            dcql = JsonNode.Parse(json) as JsonObject ?? throw new WalletException("dcql_query bir JSON nesnesi değil.");
        }
        catch (JsonException)
        {
            throw new WalletException("dcql_query okunamadı.");
        }

        if (dcql["credentials"] is not JsonArray { Count: 1 } credentials || credentials[0] is not JsonObject query
            || dcql.ContainsKey("credential_sets"))
        {
            throw new WalletException("Bu cüzdan tek kimlik bilgili DCQL sorgularını destekliyor.");
        }

        var id = JoseJson.GetString(query, "id") ?? throw new WalletException("DCQL sorgusunda id yok.");
        if (JoseJson.GetString(query, "format") != "dc+sd-jwt")
        {
            throw new WalletException("Bu cüzdan yalnız dc+sd-jwt biçimini sunabilir.");
        }

        var vctValues = (query["meta"]?["vct_values"] as JsonArray)?.Select(v => (string?)v).OfType<string>().ToList();
        if (vctValues is not { Count: > 0 })
        {
            throw new WalletException("DCQL sorgusunda meta.vct_values yok.");
        }

        // claims yoksa doğrulayıcı seçerek açılan hiçbir alan istemiyor demektir (DCQL §6.3).
        var claims = new List<string>();
        foreach (var claim in query["claims"] as JsonArray ?? new JsonArray())
        {
            if (claim?["path"] is not JsonArray { Count: 1 } path || path[0] is not JsonValue name || !name.TryGetValue(out string? text))
            {
                throw new WalletException("Bu cüzdan yalnız üst düzey alan yollarını (ör. [\"given_name\"]) destekliyor.");
            }

            claims.Add(text);
        }

        return new RequestedCredential(id, vctValues, claims);
    }
}
