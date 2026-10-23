using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

using DijitalKimlik.Crypto.Jose;
using DijitalKimlik.Crypto.SelectiveDisclosure;
using DijitalKimlik.Crypto.StatusLists;
using DijitalKimlik.Infrastructure.Persistence;
using DijitalKimlik.IntegrationTests.Infrastructure;
using DijitalKimlik.Issuer.Oid4Vci;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DijitalKimlik.IntegrationTests;

/// <summary>
/// OpenID4VCI akışı uçtan uca: test burada cüzdanı ve doğrulayıcıyı oynar. Issuer gerçek SQL Server ile
/// bellek içinde çalışır; istekler HTTP üzerinden gider.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class Oid4VciFlowTests(SqlServerFixture sql) : IDisposable
{
    private const string Issuer = IssuerApplicationFactory.IssuerIdentifier;
    private const string Verifier = "redirect_uri:http://localhost:5301/presentations/response";

    private readonly IssuerApplicationFactory _factory = new(sql);

    [Fact]
    public async Task Wallet_gets_a_credential_presents_two_claims_and_sees_the_revocation()
    {
        using var client = _factory.CreateClient();
        var offer = await CreateOfferAsync();

        // 1. Cüzdan teklifteki düzenleyicinin metadata'sını okur.
        var metadata = await GetJsonAsync(client, "/.well-known/openid-credential-issuer");
        Assert.Equal(Issuer, (string?)metadata["credential_issuer"]);
        Assert.Equal("dc+sd-jwt", (string?)metadata["credential_configurations_supported"]?[CredentialIssuanceService.ConfigurationId]?["format"]);

        // 2-4. Token (kod + PIN), nonce ve kanıtla kimlik bilgisi.
        var accessToken = await RedeemAsync(client, offer, offer.TxCode);
        using var holderKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var response = await RequestCredentialAsync(client, accessToken, Proof(holderKey, await NonceAsync(client)));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var credential = (string)JsonNode.Parse(await response.Content.ReadAsStringAsync())!["credentials"]![0]!["credential"]!;

        // 5. Doğrulayıcı imzayı düzenleyicinin yayımladığı anahtarla doğrular.
        using var issuerKey = await IssuerKeyAsync(client);
        var now = DateTimeOffset.UtcNow;
        var issued = SdJwtVc.Verify(SdJwt.Parse(credential), issuerKey, now, keyBinding: null);
        Assert.Equal(CredentialIssuanceService.Vct, (string?)issued.Claims["vct"]);
        Assert.Equal(Issuer, (string?)issued.Claims["iss"]);
        Assert.Equal(10, issued.DisclosedClaimNames.Count);
        var thumbprint = EcJwk.Thumbprint(EcJwk.FromPublicKey(holderKey));
        Assert.Equal(thumbprint, EcJwk.Thumbprint(issued.HolderJwk!));

        // 6. Cüzdan yalnız iki alanı açar; ad ve kimlik numarası doğrulayıcıya gitmez.
        var presentation = SdJwtVc.Present(credential, ["age_over_18", "valid_until"], holderKey, Verifier, "n-1", now);
        var shown = SdJwtVc.Verify(SdJwt.Parse(presentation), issuerKey, now, new KeyBindingRequirement(Verifier, "n-1", TimeSpan.FromMinutes(5)));
        Assert.Equal(["age_over_18", "valid_until"], shown.DisclosedClaimNames.Order(StringComparer.Ordinal));
        Assert.True((bool)shown.Claims["age_over_18"]!);
        Assert.False(shown.Claims.ContainsKey("given_name"));
        Assert.False(shown.Claims.ContainsKey("identity_number"));

        // 7. Durum listesi: önce geçerli (0), memur iptal edince geçersiz (1).
        var statusRef = issued.Claims["status"]!["status_list"]!;
        var uri = new Uri((string)statusRef["uri"]!);
        var index = (int)statusRef["idx"]!;
        Assert.Equal(0, await StatusAtAsync(client, uri, index, issuerKey));

        await using (var db = sql.CreateContext())
        {
            var record = await db.IssuedCredentials.SingleAsync(c => c.StatusListIndex == index && c.HolderKeyThumbprint == thumbprint);
            record.Revoke("Test: izin iptal edildi.", DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        Assert.Equal(1, await StatusAtAsync(client, uri, index, issuerKey));
    }

    [Fact]
    public async Task Wrong_pin_and_second_redemption_are_invalid_grant()
    {
        using var client = _factory.CreateClient();
        var offer = await CreateOfferAsync();
        var wrongPin = offer.TxCode == "000000" ? "111111" : "000000";

        using var wrong = await PostTokenAsync(client, offer, wrongPin);
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal("invalid_grant", (string?)JsonNode.Parse(await wrong.Content.ReadAsStringAsync())!["error"]);

        await RedeemAsync(client, offer, offer.TxCode);

        using var again = await PostTokenAsync(client, offer, offer.TxCode);
        Assert.Equal("invalid_grant", (string?)JsonNode.Parse(await again.Content.ReadAsStringAsync())!["error"]);
    }

    [Fact]
    public async Task Nonce_is_single_use_and_proof_must_target_this_issuer()
    {
        using var client = _factory.CreateClient();
        var accessToken = await RedeemAsync(client, await CreateOfferAsync(), txCode: null);
        using var holderKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        using var foreign = await RequestCredentialAsync(client, accessToken, Proof(holderKey, await NonceAsync(client), aud: "https://baska-duzenleyici.example"));
        Assert.Equal("invalid_proof", await ErrorAsync(foreign));

        var nonce = await NonceAsync(client);
        using var first = await RequestCredentialAsync(client, accessToken, Proof(holderKey, nonce));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var replay = await RequestCredentialAsync(client, accessToken, Proof(holderKey, nonce));
        Assert.Equal("invalid_nonce", await ErrorAsync(replay));
    }

    [Fact]
    public async Task Credential_endpoint_without_access_token_is_401()
    {
        using var client = _factory.CreateClient();
        using var holderKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        using var response = await RequestCredentialAsync(client, accessToken: "gecersiz", Proof(holderKey, await NonceAsync(client)));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("invalid_token", response.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_list_token_is_signed_and_self_describing()
    {
        using var client = _factory.CreateClient();
        int statusListId;
        await using (var db = sql.CreateContext())
        {
            statusListId = await db.StatusLists.OrderBy(l => l.Id).Select(l => l.Id).FirstAsync();
        }

        using var response = await client.GetAsync(new Uri($"/status-lists/{statusListId}", UriKind.Relative));

        Assert.Equal("application/statuslist+jwt", response.Content.Headers.ContentType?.MediaType);
        var jws = Jws.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("statuslist+jwt", jws.Type);
        Assert.Equal($"{Issuer}/status-lists/{statusListId}", (string?)jws.Payload["sub"]);
        Assert.Equal(2, (int)jws.Payload["status_list"]!["bits"]!);
    }

    public void Dispose() => _factory.Dispose();

    /// <summary>Memurun "Teklif oluştur" düğmesinin yaptığı iş; tohum verideki geçerli bir izin için.</summary>
    private async Task<CreatedOffer> CreateOfferAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IssuerDbContext>();
        var seeded = IssuerDevelopmentSeeder.IdentityNumbers;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var permits = await db.ResidencePermits.Where(p => seeded.Contains(p.Foreigner.IdentityNumber)).ToListAsync();
        var permit = permits.First(p => p.IsValidOn(today));

        var offer = await scope.ServiceProvider.GetRequiredService<OfferService>().CreateAsync(permit.Id, "test", CancellationToken.None);
        return offer!;
    }

    private static async Task<JsonObject> GetJsonAsync(HttpClient client, string path) =>
        (JsonObject)JsonNode.Parse(await client.GetStringAsync(new Uri(path, UriKind.Relative)))!;

    private static string PreAuthorizedCode(CreatedOffer offer) =>
        (string)JsonNode.Parse(offer.OfferJson)!["grants"]![OfferService.PreAuthorizedCodeGrant]!["pre-authorized_code"]!;

    private static Task<HttpResponseMessage> PostTokenAsync(HttpClient client, CreatedOffer offer, string txCode) =>
        client.PostAsync(new Uri("/token", UriKind.Relative), new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = OfferService.PreAuthorizedCodeGrant,
            ["pre-authorized_code"] = PreAuthorizedCode(offer),
            ["tx_code"] = txCode,
        }));

    private static async Task<string> RedeemAsync(HttpClient client, CreatedOffer offer, string? txCode)
    {
        using var response = await PostTokenAsync(client, offer, txCode ?? offer.TxCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        return (string)JsonNode.Parse(await response.Content.ReadAsStringAsync())!["access_token"]!;
    }

    private static async Task<string> NonceAsync(HttpClient client)
    {
        using var response = await client.PostAsync(new Uri("/nonce", UriKind.Relative), content: null);
        return (string)JsonNode.Parse(await response.Content.ReadAsStringAsync())!["c_nonce"]!;
    }

    /// <summary>Cüzdanın anahtar sahipliği kanıtı (OpenID4VCI Ek F.1).</summary>
    private static string Proof(ECDsa holderKey, string nonce, string aud = Issuer) =>
        Jws.Sign(
            new JsonObject { ["typ"] = ProofValidator.Type, ["jwk"] = EcJwk.FromPublicKey(holderKey) },
            new JsonObject { ["aud"] = aud, ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), ["nonce"] = nonce },
            holderKey);

    private static Task<HttpResponseMessage> RequestCredentialAsync(HttpClient client, string accessToken, string proof)
    {
        var body = new JsonObject
        {
            ["credential_configuration_id"] = CredentialIssuanceService.ConfigurationId,
            ["proofs"] = new JsonObject { ["jwt"] = new JsonArray(proof) },
        };
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/credential", UriKind.Relative))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client.SendAsync(request);
    }

    private static async Task<string?> ErrorAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        return (string?)JsonNode.Parse(await response.Content.ReadAsStringAsync())!["error"];
    }

    private static async Task<ECDsa> IssuerKeyAsync(HttpClient client)
    {
        var metadata = await GetJsonAsync(client, "/.well-known/jwt-vc-issuer");
        Assert.Equal(Issuer, (string?)metadata["issuer"]);
        return EcJwk.ToPublicKey((JsonObject)metadata["jwks"]!["keys"]![0]!);
    }

    private static async Task<int> StatusAtAsync(HttpClient client, Uri uri, int index, ECDsa issuerKey)
    {
        var jws = Jws.Parse(await client.GetStringAsync(uri));
        jws.RequireType(StatusListService.Type);
        jws.VerifySignature(issuerKey);
        var list = jws.Payload["status_list"]!;
        return TokenStatusList.Decode((string)list["lst"]!, (int)list["bits"]!)[index];
    }
}
