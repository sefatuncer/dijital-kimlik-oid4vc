using System.Security.Cryptography;

using DijitalKimlik.Application.Offers;
using DijitalKimlik.Domain.Offers;
using DijitalKimlik.Domain.Permits;
using DijitalKimlik.Infrastructure.Persistence;
using DijitalKimlik.Infrastructure.Persistence.Offers;
using DijitalKimlik.IntegrationTests.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace DijitalKimlik.IntegrationTests;

/// <summary>
/// Aynı teklifi aynı anda kullanan istekler. Senaryo: kod bir saldırgana sızdı ya da cüzdan isteği
/// iki kez gönderdi. Kural: bir teklif yalnız bir kez kullanılır, PIN en fazla beş kez yanlış girilir.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class OfferRedemptionConcurrencyTests(SqlServerFixture sql)
{
    private const int Parallelism = 10;
    private const string Pin = "493017";

    public static TheoryData<OfferRedemptionStrategy> Strategies =>
    [
        OfferRedemptionStrategy.Optimistic,
        OfferRedemptionStrategy.Pessimistic,
        OfferRedemptionStrategy.Atomic,
        OfferRedemptionStrategy.StoredProcedure,
    ];

    [Theory]
    [MemberData(nameof(Strategies))]
    public async Task Correct_code_is_redeemed_exactly_once_under_concurrency(OfferRedemptionStrategy strategy)
    {
        var code = await CreateOfferAsync();

        var results = await RedeemConcurrentlyAsync(strategy, code, Pin);

        Assert.Single(results, r => r!.Result == RedeemResult.Success);
        Assert.Equal(Parallelism - 1, results.Count(r => r!.Result == RedeemResult.AlreadyRedeemed));

        var offer = await LoadAsync(code);
        Assert.Equal(OfferStatus.Redeemed, offer.Status);
        Assert.Equal(TestEntities.Now, offer.RedeemedAt);
    }

    [Theory]
    [MemberData(nameof(Strategies))]
    public async Task Concurrent_wrong_pins_lock_the_offer_after_exactly_five_failures(OfferRedemptionStrategy strategy)
    {
        var code = await CreateOfferAsync();

        var results = await RedeemConcurrentlyAsync(strategy, code, "000000");

        // Kayıp güncelleme (lost update) olsaydı on deneme sayacı beşe ulaştırmadan geçebilirdi.
        var offer = await LoadAsync(code);
        Assert.Equal(CredentialOffer.MaxTxCodeAttempts, offer.FailedTxCodeAttempts);
        Assert.Equal(OfferStatus.Locked, offer.Status);
        Assert.All(results, r => Assert.Contains(r!.Result, new[] { RedeemResult.WrongTxCode, RedeemResult.Locked }));

        var wrong = results.Count(r => r!.Result == RedeemResult.WrongTxCode);
        if (strategy == OfferRedemptionStrategy.Atomic)
        {
            // Atomik yöntem durumu UPDATE'ten sonra ayrıca okur: kayıt kesin, yanıt yaklaşık.
            Assert.InRange(wrong, 0, CredentialOffer.MaxTxCodeAttempts - 1);
        }
        else
        {
            // İyimser ve kötümser yöntemler istekleri fiilen sıraya dizer; saklı yordam yeni durumu
            // OUTPUT ile aynı komutta okur. Üçünde de ilk dört istek WrongTxCode alır.
            Assert.Equal(CredentialOffer.MaxTxCodeAttempts - 1, wrong);
        }
    }

    [Theory]
    [MemberData(nameof(Strategies))]
    public async Task Unknown_code_returns_null(OfferRedemptionStrategy strategy)
    {
        await using var db = sql.CreateContext();
        var redeemer = Create(strategy, db, new FakeTimeProvider(TestEntities.Now));

        Assert.Null(await redeemer.RedeemAsync(RandomNumberGenerator.GetHexString(64), Pin));
    }

    [Theory]
    [MemberData(nameof(Strategies))]
    public async Task Expired_offer_is_never_redeemed(OfferRedemptionStrategy strategy)
    {
        var code = await CreateOfferAsync();
        var later = new FakeTimeProvider(TestEntities.Now.AddMinutes(11));

        await using var db = sql.CreateContext();
        var result = await Create(strategy, db, later).RedeemAsync(code, Pin);

        Assert.Equal(RedeemResult.Expired, result!.Result);
    }

    /// <summary>
    /// Yanlış yöntem: önce oku ("Pending mi?"), sonra koşulsuz yaz. İki adım arasında başka istek
    /// araya girebilir (TOCTOU: time-of-check to time-of-use). Test, bütün isteklerin okumayı
    /// yazmadan önce bitirmesini garanti eder ve hatayı belirlenimci olarak gösterir.
    /// </summary>
    [Fact]
    public async Task Naive_check_then_act_lets_the_same_offer_be_redeemed_many_times()
    {
        var code = await CreateOfferAsync();
        var hash = CredentialOffer.HashPreAuthorizedCode(code);
        var allChecked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var checkedCount = 0;

        var attempts = Enumerable.Range(0, Parallelism).Select(async _ =>
        {
            await using var db = sql.CreateContext();
            var offers = db.CredentialOffers.Where(o => o.PreAuthorizedCodeHash == hash);

            var status = await offers.Select(o => o.Status).SingleAsync();
            if (Interlocked.Increment(ref checkedCount) == Parallelism)
            {
                allChecked.SetResult();
            }

            await allChecked.Task;
            if (status != OfferStatus.Pending)
            {
                return false;
            }

            await offers.ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OfferStatus.Redeemed));
            return true;
        });

        var succeeded = await Task.WhenAll(attempts);

        // Her istek "kullandım" dedi: aynı tekliften on ayrı kimlik bilgisi çıkardı.
        Assert.Equal(Parallelism, succeeded.Count(s => s));
    }

    private static IOfferRedeemer Create(OfferRedemptionStrategy strategy, IssuerDbContext db, TimeProvider time) =>
        strategy switch
        {
            OfferRedemptionStrategy.Optimistic => new OptimisticOfferRedeemer(db, time),
            OfferRedemptionStrategy.Pessimistic => new PessimisticOfferRedeemer(db, time),
            OfferRedemptionStrategy.Atomic => new AtomicOfferRedeemer(db, time),
            OfferRedemptionStrategy.StoredProcedure => new StoredProcedureOfferRedeemer(db, time),
            _ => throw new ArgumentOutOfRangeException(nameof(strategy)),
        };

    private async Task<OfferRedemption?[]> RedeemConcurrentlyAsync(OfferRedemptionStrategy strategy, string code, string pin)
    {
        var time = new FakeTimeProvider(TestEntities.Now);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var attempts = Enumerable.Range(0, Parallelism).Select(async _ =>
        {
            await start.Task;

            // DbContext thread-safe değildir: her eşzamanlı istek kendi bağlamını (ve bağlantısını) alır.
            await using var db = sql.CreateContext();
            return await Create(strategy, db, time).RedeemAsync(code, pin);
        }).ToArray();

        start.SetResult();
        return await Task.WhenAll(attempts);
    }

    private async Task<string> CreateOfferAsync()
    {
        var code = RandomNumberGenerator.GetHexString(64);
        var foreigner = TestEntities.NewForeigner();
        var permit = foreigner.GrantPermit(
            TestEntities.NewPermitNumber(), PermitType.ShortTerm, new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1));
        var offer = new CredentialOffer(
            Guid.NewGuid(), permit, CredentialOffer.HashPreAuthorizedCode(code), CredentialOffer.HashTxCode(code, Pin),
            "memur.test", TestEntities.Now, TimeSpan.FromMinutes(10));

        await using var db = sql.CreateContext();
        db.Foreigners.Add(foreigner);
        db.CredentialOffers.Add(offer);
        await db.SaveChangesAsync();
        return code;
    }

    private async Task<CredentialOffer> LoadAsync(string code)
    {
        var hash = CredentialOffer.HashPreAuthorizedCode(code);
        await using var db = sql.CreateContext();
        return await db.CredentialOffers.AsNoTracking().SingleAsync(o => o.PreAuthorizedCodeHash == hash);
    }
}
