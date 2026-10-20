using DijitalKimlik.Domain.Foreigners;
using DijitalKimlik.Domain.Permits;
using DijitalKimlik.Domain.StatusLists;

using Microsoft.EntityFrameworkCore;

namespace DijitalKimlik.Infrastructure.Persistence;

/// <summary>
/// Geliştirme ortamı için birkaç demo kaydı ekler. Kişiler hayalidir. 20.000 kullanıcılık yük
/// verisi ayrıca, SqlBulkCopy ile yüklenir (Bölüm 9).
/// </summary>
public static class IssuerDevelopmentSeeder
{
    /// <summary>Status list kapasitesi. Gerçek kayıt sayısından büyük: rastgele sıra dağıtımı için.</summary>
    public const int DefaultStatusListCapacity = 131_072;

    private static readonly (string First9, string Given, string Family, int Year, string Nationality, string Passport, PermitType Type)[] People =
    [
        ("990000001", "Amina", "Haddad", 1995, "SYR", "N1234567", PermitType.ShortTerm),
        ("990000002", "Oleksandr", "Kovalenko", 1988, "UKR", "FA123456", PermitType.Family),
        ("990000003", "Farhad", "Rahimi", 2001, "AFG", "P0987654", PermitType.Student),
        ("990000004", "Leyla", "Mammadova", 1979, "AZE", "C7654321", PermitType.LongTerm),
        ("990000005", "Jonas", "Becker", 1990, "DEU", "C01X00T47", PermitType.ShortTerm),
    ];

    public static async Task SeedAsync(IssuerDbContext db, TimeProvider clock, CancellationToken cancellationToken = default)
    {
        if (await db.StatusLists.AnyAsync(cancellationToken))
        {
            return;
        }

        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        db.StatusLists.Add(new StatusList(DefaultStatusListCapacity, now));

        for (var i = 0; i < People.Length; i++)
        {
            var p = People[i];
            var number = ForeignerIdentityNumber.Create(p.First9 + ForeignerIdentityNumber.ComputeCheckDigits(p.First9));
            var foreigner = new Foreigner(number, p.Given, p.Family, new DateOnly(p.Year, 3, 15), p.Nationality, p.Passport, now);
            foreigner.GrantPermit($"IK-{today.Year}-{i + 1:D6}", p.Type, today.AddMonths(-1), today.AddYears(1));
            db.Foreigners.Add(foreigner);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
