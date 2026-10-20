using DijitalKimlik.Domain.Common;
using DijitalKimlik.Domain.Permits;

namespace DijitalKimlik.Domain.Foreigners;

/// <summary>
/// Kayıtlı yabancı (aggregate root). İkamet izinleri bu kök üzerinden verilir; dışarıdan
/// doğrudan izin listesine eleman eklenemez.
/// </summary>
public sealed class Foreigner : Entity
{
    public const int NameMaxLength = 100;
    public const int PassportMaxLength = 20;

    private readonly List<ResidencePermit> _permits = [];

    // EF Core, nesneyi veritabanından okurken bu yapıcıyı kullanır.
    private Foreigner()
    {
    }

    public Foreigner(
        ForeignerIdentityNumber identityNumber,
        string givenName,
        string familyName,
        DateOnly birthDate,
        string nationality,
        string passportNumber,
        DateTimeOffset createdAt)
    {
        IdentityNumber = identityNumber;
        GivenName = RequireText(givenName, nameof(givenName), NameMaxLength);
        FamilyName = RequireText(familyName, nameof(familyName), NameMaxLength);
        BirthDate = birthDate;
        Nationality = RequireCountryCode(nationality);
        PassportNumber = RequireText(passportNumber, nameof(passportNumber), PassportMaxLength).ToUpperInvariant();
        CreatedAt = createdAt;

        if (birthDate > DateOnly.FromDateTime(createdAt.UtcDateTime))
        {
            throw new DomainException("foreigner.birth_date_in_future", "Doğum tarihi gelecekte olamaz.");
        }
    }

    public ForeignerIdentityNumber IdentityNumber { get; private set; } = null!;

    public string GivenName { get; private set; } = string.Empty;

    public string FamilyName { get; private set; } = string.Empty;

    public DateOnly BirthDate { get; private set; }

    /// <summary>ISO 3166-1 alpha-3 ülke kodu, ör. "SYR", "UKR", "DEU".</summary>
    public string Nationality { get; private set; } = string.Empty;

    public string PassportNumber { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyCollection<ResidencePermit> Permits => _permits.AsReadOnly();

    public int AgeOn(DateOnly date)
    {
        var age = date.Year - BirthDate.Year;
        return BirthDate > date.AddYears(-age) ? age - 1 : age;
    }

    public ResidencePermit GrantPermit(string permitNumber, PermitType type, DateOnly validFrom, DateOnly validUntil)
    {
        // Aynı dönemde aynı türden ikinci bir aktif izin verilemez.
        if (_permits.Any(p => p.Type == type && p.Overlaps(validFrom, validUntil)))
        {
            throw new DomainException("permit.overlapping",
                "Bu yabancının aynı türde ve çakışan dönemde aktif bir ikamet izni var.");
        }

        var permit = new ResidencePermit(this, permitNumber, type, validFrom, validUntil);
        _permits.Add(permit);
        return permit;
    }

    private static string RequireText(string value, string name, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > maxLength)
        {
            throw new DomainException("foreigner.invalid_" + name,
                $"{name} boş olamaz ve en fazla {maxLength} karakter olabilir.");
        }

        return trimmed;
    }

    private static string RequireCountryCode(string value)
    {
        var code = value?.Trim().ToUpperInvariant();
        if (code is not { Length: 3 } || !code.All(char.IsAsciiLetterUpper))
        {
            throw new DomainException("foreigner.invalid_nationality", "Uyruk 3 harfli ISO ülke kodu olmalı.");
        }

        return code;
    }
}
