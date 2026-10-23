using DijitalKimlik.Domain.Credentials;
using DijitalKimlik.Domain.Permits;

namespace DijitalKimlik.Issuer.Models;

public sealed record PermitRow(
    int PermitId,
    string HolderName,
    string MaskedIdentityNumber,
    string Nationality,
    string PermitNumber,
    PermitType Type,
    DateOnly ValidFrom,
    DateOnly ValidUntil,
    bool IsValid);

public sealed record CredentialRow(
    int Id,
    string CredentialId,
    string HolderName,
    string PermitNumber,
    CredentialStatus Status,
    string? StatusReason,
    int StatusListIndex,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt);

public sealed record DashboardViewModel(IReadOnlyList<PermitRow> Permits, IReadOnlyList<CredentialRow> Credentials);

/// <summary>Ekranda gösterilen Türkçe adlar. Veritabanında ve kimlik bilgisinde sabit kodlar durur.</summary>
public static class Labels
{
    public static string Of(PermitType type) => type switch
    {
        PermitType.ShortTerm => "Kısa dönem",
        PermitType.Family => "Aile",
        PermitType.Student => "Öğrenci",
        PermitType.LongTerm => "Uzun dönem",
        PermitType.Humanitarian => "İnsani",
        _ => type.ToString(),
    };

    public static string Of(CredentialStatus status) => status switch
    {
        CredentialStatus.Active => "Geçerli",
        CredentialStatus.Suspended => "Askıda",
        CredentialStatus.Revoked => "İptal",
        CredentialStatus.Expired => "Süresi doldu",
        _ => status.ToString(),
    };

    public static string BadgeClass(CredentialStatus status) => status switch
    {
        CredentialStatus.Active => "text-bg-success",
        CredentialStatus.Suspended => "text-bg-warning",
        _ => "text-bg-danger",
    };

    /// <summary>Ekranda kimlik numarasının yalnız başı ve sonu görünür (KVKK: veri minimizasyonu).</summary>
    public static string Mask(string identityNumber) =>
        identityNumber.Length <= 5 ? identityNumber : identityNumber[..3] + new string('•', identityNumber.Length - 5) + identityNumber[^2..];
}
