using DijitalKimlik.Domain.Common;
using DijitalKimlik.Domain.Foreigners;

namespace DijitalKimlik.Domain.Permits;

/// <summary>
/// 6458 sayılı Kanun'daki ikamet izni türleri (sadeleştirilmiş).
/// Sayısal değerler veritabanına yazılır; değiştirilmemeli, yalnız yenisi eklenmeli.
/// </summary>
public enum PermitType
{
    ShortTerm = 1,
    Family = 2,
    Student = 3,
    LongTerm = 4,
    Humanitarian = 5,
}

public enum PermitStatus
{
    Active = 1,
    Cancelled = 2,
}

/// <summary>İkamet izni. Yalnız <see cref="Foreigner.GrantPermit"/> ile oluşturulur.</summary>
public sealed class ResidencePermit : Entity
{
    public const int PermitNumberMaxLength = 20;

    private ResidencePermit()
    {
    }

    internal ResidencePermit(Foreigner foreigner, string permitNumber, PermitType type, DateOnly validFrom, DateOnly validUntil)
    {
        if (validUntil < validFrom)
        {
            throw new DomainException("permit.invalid_period", "Bitiş tarihi başlangıçtan önce olamaz.");
        }

        if (string.IsNullOrWhiteSpace(permitNumber) || permitNumber.Length > PermitNumberMaxLength)
        {
            throw new DomainException("permit.invalid_number", "İzin numarası boş olamaz.");
        }

        Foreigner = foreigner;
        PermitNumber = permitNumber.Trim().ToUpperInvariant();
        Type = type;
        ValidFrom = validFrom;
        ValidUntil = validUntil;
        Status = PermitStatus.Active;
    }

    public int ForeignerId { get; private set; }

    public Foreigner Foreigner { get; private set; } = null!;

    public string PermitNumber { get; private set; } = string.Empty;

    public PermitType Type { get; private set; }

    public DateOnly ValidFrom { get; private set; }

    public DateOnly ValidUntil { get; private set; }

    public PermitStatus Status { get; private set; }

    public string? CancellationReason { get; private set; }

    public bool IsValidOn(DateOnly date) =>
        Status == PermitStatus.Active && date >= ValidFrom && date <= ValidUntil;

    public bool Overlaps(DateOnly from, DateOnly until) =>
        Status == PermitStatus.Active && from <= ValidUntil && until >= ValidFrom;

    public void Cancel(string reason)
    {
        if (Status == PermitStatus.Cancelled)
        {
            throw new DomainException("permit.already_cancelled", "İzin zaten iptal edilmiş.");
        }

        CancellationReason = string.IsNullOrWhiteSpace(reason)
            ? throw new DomainException("permit.reason_required", "İptal gerekçesi zorunlu.")
            : reason.Trim();
        Status = PermitStatus.Cancelled;
    }
}
