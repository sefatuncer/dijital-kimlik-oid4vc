// Teklif kullanımındaki dört eşzamanlılık yönteminin ölçümü.
//
// Çalıştırma (Docker açık olmalı):
//   dotnet run --project tools/DijitalKimlik.Olcum -c Release -- [--cikti artifacts/olcum] [--hizli]
//
// Veritabanı: DK_OLCUM_BAGLANTI ortam değişkeni varsa o SQL Server kullanılır (ör. ayrı bir
// veritabanı sunucusu; parola komut satırında görünmesin diye ortam değişkeninden okunur).
// Yoksa Testcontainers geçici bir SQL Server açar. Migration'lar uygulanır ve iki senaryo koşar:
//   1) Çekişme: aynı teklife aynı anda 10 istek. Doğru PIN'de 1 başarı, yanlış PIN'de 5. denemede
//      kilit beklenir. Her tur ayrı bir teklifle yapılır.
//   2) Verim: her işçi farklı bir teklifi kullanır (gerçek trafiğe benzer). İşçi sayısı artırılır;
//      saniyedeki işlem ve gecikme yüzdelikleri (p50/p95/p99) ölçülür.
// Her istek yeni bir DI scope'u (yeni DbContext) ile yapılır, tıpkı bir HTTP isteği gibi.
// Sonuçlar CSV olarak yazılır; öğretim notlarındaki grafikler bu dosyalardan çizilir.
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

using DijitalKimlik.Application.Offers;
using DijitalKimlik.Domain.Foreigners;
using DijitalKimlik.Domain.Offers;
using DijitalKimlik.Domain.Permits;
using DijitalKimlik.Infrastructure;
using DijitalKimlik.Infrastructure.Persistence;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Testcontainers.MsSql;

var outputDir = ArgValue(args, "--cikti") ?? Path.Combine("artifacts", "olcum");
var quick = args.Contains("--hizli");
var plan = quick
    ? new Plan(Rounds: 3, Parallel: 10, WorkerLevels: [1, 8], OpsPerWorker: 10, WarmUp: 5)
    : new Plan(Rounds: 30, Parallel: 10, WorkerLevels: [1, 4, 16, 32, 64], OpsPerWorker: 60, WarmUp: 40);
const string Pin = "482915";
var strategies = Enum.GetValues<OfferRedemptionStrategy>();

var external = Environment.GetEnvironmentVariable("DK_OLCUM_BAGLANTI");
await using var container = string.IsNullOrWhiteSpace(external)
    ? new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build()
    : null;
string connectionString;
if (container is null)
{
    connectionString = external!;
    // Ölçüm veritabanı her çalıştırmada sıfırdan kurulur; başka bir veritabanını silmemek için ad denetlenir.
    if (!new SqlConnectionStringBuilder(connectionString).InitialCatalog.EndsWith("Olcum", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("DK_OLCUM_BAGLANTI içindeki veritabanı adı 'Olcum' ile bitmeli.");
    }
}
else
{
    Console.WriteLine("SQL Server konteyneri açılıyor...");
    await container.StartAsync();
    connectionString = new SqlConnectionStringBuilder(container.GetConnectionString())
    {
        InitialCatalog = "DijitalKimlikOlcum",
    }.ConnectionString;
}

var services = new ServiceCollection();
services.AddSingleton(TimeProvider.System);
services.AddIssuerInfrastructure(new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:IssuerDb"] = connectionString })
    .Build());
await using var provider = services.BuildServiceProvider(validateScopes: true);

int permitId;
await using (var scope = provider.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<IssuerDbContext>();
    await db.Database.EnsureDeletedAsync();
    await db.Database.MigrateAsync();
    var number = ForeignerIdentityNumber.Create("990000001" + ForeignerIdentityNumber.ComputeCheckDigits("990000001"));
    var foreigner = new Foreigner(number, "Ölçüm", "Kişi", new DateOnly(1990, 1, 1), "IRN", "T990000001", DateTimeOffset.UtcNow);
    var permit = foreigner.GrantPermit("IK-OLCUM-1", PermitType.ShortTerm, new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1));
    db.Foreigners.Add(foreigner);
    await db.SaveChangesAsync();
    permitId = permit.Id;
}

Directory.CreateDirectory(outputDir);
await File.WriteAllTextAsync(Path.Combine(outputDir, "ortam.txt"), string.Join(Environment.NewLine,
    $"tarih_utc={DateTimeOffset.UtcNow:O}",
    $"isletim_sistemi={RuntimeInformation.OSDescription}",
    $"dotnet={RuntimeInformation.FrameworkDescription}",
    $"mantiksal_islemci={Environment.ProcessorCount}",
    $"sunucu_gc={System.Runtime.GCSettings.IsServerGC}",
    $"veritabani={(container is null ? "DK_OLCUM_BAGLANTI ile verilen SQL Server" : "SQL Server 2022, Testcontainers")}",
    $"plan={plan}"));

// 1) Çekişme senaryosu.
var contention = new StringBuilder("senaryo,strateji,tur,sure_ms,basarili,zaten_kullanilmis,yanlis_pin,kilitli,son_deneme_sayisi\n");
foreach (var (scenario, pin) in new[] { ("dogru_pin", Pin), ("yanlis_pin", "000000") })
{
    for (var round = 1; round <= plan.Rounds; round++)
    {
        foreach (var strategy in strategies)
        {
            var code = (await CreateOffersAsync(1))[0];
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var tasks = Enumerable.Range(0, plan.Parallel).Select(async _ =>
            {
                await gate.Task;
                return await RedeemAsync(strategy, code, pin);
            }).ToList();

            var watch = Stopwatch.StartNew();
            gate.SetResult();
            var results = await Task.WhenAll(tasks);
            watch.Stop();

            var attempts = await FailedAttemptsAsync(code);
            int Count(RedeemResult r) => results.Count(x => x?.Result == r);
            contention.Append(CultureInfo.InvariantCulture,
                $"{scenario},{strategy},{round},{watch.Elapsed.TotalMilliseconds:F1},{Count(RedeemResult.Success)}," +
                $"{Count(RedeemResult.AlreadyRedeemed)},{Count(RedeemResult.WrongTxCode)},{Count(RedeemResult.Locked)},{attempts}\n");
        }
    }

    Console.WriteLine($"Çekişme senaryosu bitti: {scenario}");
}

await File.WriteAllTextAsync(Path.Combine(outputDir, "cekisme.csv"), contention.ToString());

// 2) Verim senaryosu. Stratejiler her işçi düzeyinde sırayla koşar; zamanla değişen koşullar
// (önbellek, tablo boyutu) tek bir stratejinin aleyhine birikmesin.
var throughput = new StringBuilder("strateji,isci,islem,sure_sn,islem_sn,p50_ms,p95_ms,p99_ms,hatali\n");
foreach (var strategy in strategies)
{
    foreach (var code in await CreateOffersAsync(plan.WarmUp))
    {
        await RedeemAsync(strategy, code, Pin);
    }
}

foreach (var workers in plan.WorkerLevels)
{
    foreach (var strategy in strategies)
    {
        var codes = await CreateOffersAsync(workers * plan.OpsPerWorker);
        var latencies = new ConcurrentBag<double>();
        var failures = 0;
        var next = -1;

        var watch = Stopwatch.StartNew();
        await Task.WhenAll(Enumerable.Range(0, workers).Select(_ => Task.Run(async () =>
        {
            int i;
            while ((i = Interlocked.Increment(ref next)) < codes.Count)
            {
                var start = Stopwatch.GetTimestamp();
                var result = await RedeemAsync(strategy, codes[i], Pin);
                latencies.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                if (result?.Result != RedeemResult.Success)
                {
                    Interlocked.Increment(ref failures);
                }
            }
        })));
        watch.Stop();

        var sorted = latencies.Order().ToArray();
        throughput.Append(CultureInfo.InvariantCulture,
            $"{strategy},{workers},{codes.Count},{watch.Elapsed.TotalSeconds:F2},{codes.Count / watch.Elapsed.TotalSeconds:F1}," +
            $"{Percentile(sorted, 0.50):F2},{Percentile(sorted, 0.95):F2},{Percentile(sorted, 0.99):F2},{failures}\n");
        Console.WriteLine($"Verim: {strategy,-15} işçi={workers,3}  {codes.Count / watch.Elapsed.TotalSeconds,8:F1} işlem/sn  " +
                          $"p95={Percentile(sorted, 0.95),7:F2} ms  hatalı={failures}");
    }
}

await File.WriteAllTextAsync(Path.Combine(outputDir, "verim.csv"), throughput.ToString());
Console.WriteLine($"Sonuçlar: {Path.GetFullPath(outputDir)}");

async Task<List<string>> CreateOffersAsync(int count)
{
    await using var scope = provider.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<IssuerDbContext>();
    var permit = await db.ResidencePermits.SingleAsync(p => p.Id == permitId);
    var now = DateTimeOffset.UtcNow;
    var codes = Enumerable.Range(0, count).Select(_ => Guid.NewGuid().ToString("N")).ToList();
    db.CredentialOffers.AddRange(codes.Select(code => new CredentialOffer(
        Guid.NewGuid(), permit, CredentialOffer.HashPreAuthorizedCode(code), CredentialOffer.HashTxCode(code, Pin),
        "olcum", now, TimeSpan.FromHours(2))));
    await db.SaveChangesAsync();
    return codes;
}

async Task<OfferRedemption?> RedeemAsync(OfferRedemptionStrategy strategy, string code, string pin)
{
    await using var scope = provider.CreateAsyncScope();
    var redeemer = scope.ServiceProvider.GetRequiredKeyedService<IOfferRedeemer>(strategy);
    return await redeemer.RedeemAsync(code, pin);
}

async Task<int> FailedAttemptsAsync(string code)
{
    await using var scope = provider.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<IssuerDbContext>();
    var hash = CredentialOffer.HashPreAuthorizedCode(code);
    return await db.CredentialOffers.Where(o => o.PreAuthorizedCodeHash == hash).Select(o => o.FailedTxCodeAttempts).SingleAsync();
}

static double Percentile(double[] sorted, double q) =>
    sorted.Length == 0 ? 0 : sorted[Math.Max(0, (int)Math.Ceiling(q * sorted.Length) - 1)];

static string? ArgValue(string[] args, string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

internal sealed record Plan(int Rounds, int Parallel, int[] WorkerLevels, int OpsPerWorker, int WarmUp)
{
    public override string ToString() =>
        $"tur={Rounds}, paralel={Parallel}, isci=[{string.Join(' ', WorkerLevels)}], isci_basi_islem={OpsPerWorker}, isinma={WarmUp}";
}
