extern alias verifier;
extern alias wallet;

using DijitalKimlik.Crypto.SelectiveDisclosure;
using DijitalKimlik.Infrastructure.Persistence;
using DijitalKimlik.IntegrationTests.Infrastructure;
using DijitalKimlik.Issuer.Oid4Vci;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using IssuanceClient = wallet::DijitalKimlik.Wallet.Credentials.IssuanceClient;
using PresentationClient = wallet::DijitalKimlik.Wallet.Presentations.PresentationClient;
using PresentationRequest = wallet::DijitalKimlik.Wallet.Presentations.PresentationRequest;
using PresentationVerifier = verifier::DijitalKimlik.Verifier.Presentations.PresentationVerifier;
using Scenario = verifier::DijitalKimlik.Verifier.Presentations.Scenario;
using StoredCredential = wallet::DijitalKimlik.Wallet.Credentials.StoredCredential;
using Transaction = verifier::DijitalKimlik.Verifier.Presentations.Transaction;
using TransactionStore = verifier::DijitalKimlik.Verifier.Presentations.TransactionStore;
using VerifierProgram = verifier::Program;
using WalletException = wallet::DijitalKimlik.Wallet.Credentials.WalletException;
using WalletOptions = wallet::DijitalKimlik.Wallet.Credentials.WalletOptions;

namespace DijitalKimlik.IntegrationTests;

/// <summary>
/// Üç uygulama birlikte: düzenleyici kimlik bilgisini verir, cüzdan alır ve sunar, doğrulayıcı
/// doğrular. Uygulamalar bellek içi TestServer'larda çalışır; aralarındaki HTTP trafiği gerçektir.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class EndToEndFlowTests : IDisposable
{
    private const string VerifierIdentifier = "http://verifier.test";

    private readonly SqlServerFixture _sql;
    private readonly IssuerApplicationFactory _issuer;
    private readonly VerifierFactory _verifier;
    private readonly HttpClient _walletToIssuer;
    private readonly HttpClient _walletToVerifier;
    private readonly IssuanceClient _issuance;
    private readonly PresentationClient _presentations;

    public EndToEndFlowTests(SqlServerFixture sql)
    {
        _sql = sql;
        _issuer = new IssuerApplicationFactory(sql);
        _verifier = new VerifierFactory(() => _issuer.Server.CreateHandler());
        _walletToIssuer = new HttpClient(_issuer.Server.CreateHandler());
        _walletToVerifier = new HttpClient(_verifier.Server.CreateHandler());
        _issuance = new IssuanceClient(
            _walletToIssuer,
            Options.Create(new WalletOptions { TrustedIssuers = [IssuerApplicationFactory.IssuerIdentifier] }),
            TimeProvider.System);
        _presentations = new PresentationClient(_walletToVerifier, TimeProvider.System);
    }

    [Fact]
    public async Task Hotel_check_in_sees_only_the_requested_claims_and_then_the_revocation()
    {
        var credential = await ReceiveCredentialAsync();
        Assert.Equal(10, credential.Claims().Count);

        // Otel: ad, soyad, uyruk, kimlik no ve bitiş tarihi ister; doğum tarihini istemez.
        var (transaction, redirect) = await PresentAsync("konaklama", credential);
        var result = transaction.Result!;
        Assert.True(result.Success, string.Join(" | ", result.Checks.Select(c => $"{c.Name}: {c.Detail}")));
        Assert.Equal(["given_name", "family_name", "nationality", "identity_number", "valid_until"], result.Claims.Select(c => c.Name));
        Assert.DoesNotContain(result.Claims, c => c.Name == "birthdate");
        Assert.Equal(13 - 5, result.HiddenDigests); // 10 gerçek + 3 sahte özet, 5'i açıldı

        // Cüzdanın yönlendirdiği sonuç sayfası response_code ile açılır.
        using var verifierBrowser = _verifier.CreateClient();
        var page = await verifierBrowser.GetStringAsync(redirect);
        Assert.Contains("Doğrulandı", page, StringComparison.Ordinal);

        // Memur iptal eder; aynı kimlik bilgisiyle yeni sunum durum listesinde takılır.
        await RevokeAsync(credential);
        var (afterRevocation, _) = await PresentAsync("konaklama", credential);
        Assert.False(afterRevocation.Result!.Success);
        Assert.Equal("İptal durumu (status list)", afterRevocation.Result.Checks[^1].Name);
        Assert.Contains("İptal edilmiş", afterRevocation.Result.Checks[^1].Detail, StringComparison.Ordinal);
        Assert.Empty(afterRevocation.Result.Claims);
    }

    [Fact]
    public async Task Age_check_discloses_a_single_boolean()
    {
        var credential = await ReceiveCredentialAsync();

        var (transaction, _) = await PresentAsync("yas", credential);

        Assert.True(transaction.Result!.Success);
        var claim = Assert.Single(transaction.Result.Claims);
        Assert.Equal("age_over_18", claim.Name);
        Assert.Equal("Evet", claim.Value);
    }

    [Fact]
    public async Task Presentation_for_another_nonce_fails_key_binding_and_a_second_answer_is_refused()
    {
        var credential = await ReceiveCredentialAsync();
        var store = _verifier.Services.GetRequiredService<TransactionStore>();
        var transaction = store.Create(Scenario.Find("yas")!, "test-oturumu");
        var query = store.AuthorizationQuery(transaction);

        // Saldırgan başka bir oturumdan yakaladığı nonce ile hazırlanmış sunumu bu oturuma gönderir.
        var forged = PresentationRequest.Parse(query.Replace("nonce=" + transaction.Nonce, "nonce=baska-oturum", StringComparison.Ordinal));
        await _presentations.RespondAsync(forged, credential, CancellationToken.None);

        Assert.False(transaction.Result!.Success);
        Assert.Equal("Cüzdan bağlama (KB-JWT)", transaction.Result.Checks[^1].Name);

        // Aynı state ile ikinci yanıt kabul edilmez.
        var replay = await Assert.ThrowsAsync<WalletException>(() =>
            _presentations.RespondAsync(PresentationRequest.Parse(query), credential, CancellationToken.None));
        Assert.Contains("HTTP 400", replay.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Wallet_refuses_a_request_that_answers_to_another_address()
    {
        var store = _verifier.Services.GetRequiredService<TransactionStore>();
        var query = store.AuthorizationQuery(store.Create(Scenario.Find("yas")!, "test-oturumu"));
        var redirected = query.Replace("response_uri=http%3A%2F%2Fverifier.test", "response_uri=http%3A%2F%2Fsaldirgan.example", StringComparison.Ordinal);

        Assert.NotEqual(query, redirected);
        Assert.Throws<WalletException>(() => PresentationRequest.Parse(redirected));
    }

    [Fact]
    public async Task Wrong_pin_is_reported_and_the_offer_can_still_be_used()
    {
        var offer = await CreateOfferAsync();
        var wrongPin = offer.TxCode == "000000" ? "111111" : "000000";

        var error = await Assert.ThrowsAsync<WalletException>(() => _issuance.ReceiveAsync(offer.OfferJson, wrongPin, CancellationToken.None));
        Assert.Contains("PIN", error.Message, StringComparison.Ordinal);

        var credential = await _issuance.ReceiveAsync(offer.OfferJson, offer.TxCode, CancellationToken.None);
        Assert.Equal(IssuerApplicationFactory.IssuerIdentifier, credential.Issuer);
    }

    public void Dispose()
    {
        _walletToIssuer.Dispose();
        _walletToVerifier.Dispose();
        _verifier.Dispose();
        _issuer.Dispose();
    }

    private async Task<StoredCredential> ReceiveCredentialAsync()
    {
        var offer = await CreateOfferAsync();
        return await _issuance.ReceiveAsync(offer.OfferJson, offer.TxCode, CancellationToken.None);
    }

    private async Task<CreatedOffer> CreateOfferAsync()
    {
        await using var scope = _issuer.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IssuerDbContext>();
        var seeded = IssuerDevelopmentSeeder.IdentityNumbers;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var permits = await db.ResidencePermits.Include(p => p.Foreigner).Where(p => seeded.Contains(p.Foreigner.IdentityNumber)).ToListAsync();

        // Yaş testi "Evet" bekler: tohum verideki herkes 18 yaşından büyük.
        var permit = permits.First(p => p.IsValidOn(today));
        return (await scope.ServiceProvider.GetRequiredService<OfferService>().CreateAsync(permit.Id, "test", CancellationToken.None))!;
    }

    private async Task<(Transaction Transaction, Uri Redirect)> PresentAsync(string scenario, StoredCredential credential)
    {
        var store = _verifier.Services.GetRequiredService<TransactionStore>();
        var transaction = store.Create(Scenario.Find(scenario)!, "test-oturumu");
        var request = PresentationRequest.Parse(store.AuthorizationQuery(transaction));

        var redirect = await _presentations.RespondAsync(request, credential, CancellationToken.None);

        Assert.NotNull(redirect);
        Assert.StartsWith($"{VerifierIdentifier}/presentations/{transaction.Id}/result?response_code=", redirect.AbsoluteUri, StringComparison.Ordinal);
        return (transaction, redirect);
    }

    private async Task RevokeAsync(StoredCredential credential)
    {
        // Durum listesindeki sıra, kimlik bilgisinin imzalı kısmında açıkça yazar.
        var index = (int)SdJwt.Parse(credential.Credential).IssuerJwt.Payload["status"]!["status_list"]!["idx"]!;
        await using var db = _sql.CreateContext();
        var record = await db.IssuedCredentials.SingleAsync(c => c.StatusListIndex == index);
        record.Revoke("Test: izin iptal edildi.", DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
    }

    private sealed class VerifierFactory(Func<HttpMessageHandler> issuerHandler) : WebApplicationFactory<VerifierProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Verifier:Identifier", VerifierIdentifier);
            builder.UseSetting("Verifier:TrustedIssuers:0", IssuerApplicationFactory.IssuerIdentifier);

            // Doğrulayıcının düzenleyiciye giden istekleri, bellek içindeki Issuer'a yönlendirilir.
            builder.ConfigureServices(services =>
                services.AddHttpClient<PresentationVerifier>().ConfigurePrimaryHttpMessageHandler(issuerHandler));
        }
    }
}
