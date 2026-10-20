using System.Globalization;
using System.Security.Cryptography;

using DijitalKimlik.Domain.Foreigners;

namespace DijitalKimlik.IntegrationTests.Infrastructure;

/// <summary>
/// Entegrasyon testleri tek veritabanını paylaşır. Unique indeksler çakışmasın diye her test
/// kendi rastgele kimlik ve izin numaralarını üretir.
/// </summary>
internal static class TestEntities
{
    public static readonly DateTimeOffset Now = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);

    public static string NewFirst9() =>
        "99" + RandomNumberGenerator.GetInt32(0, 10_000_000).ToString("D7", CultureInfo.InvariantCulture);

    public static string NewPermitNumber() => "IK-T-" + RandomNumberGenerator.GetHexString(10);

    public static Foreigner NewForeigner(string? first9 = null)
    {
        first9 ??= NewFirst9();
        var number = ForeignerIdentityNumber.Create(first9 + ForeignerIdentityNumber.ComputeCheckDigits(first9));
        return new Foreigner(number, "Test", "Kişi", new DateOnly(1990, 1, 1), "IRN", "T" + first9, Now);
    }
}
