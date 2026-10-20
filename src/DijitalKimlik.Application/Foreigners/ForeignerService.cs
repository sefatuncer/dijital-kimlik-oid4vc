using DijitalKimlik.Application.Abstractions;
using DijitalKimlik.Application.Common;
using DijitalKimlik.Domain.Common;
using DijitalKimlik.Domain.Foreigners;

namespace DijitalKimlik.Application.Foreigners;

public sealed record RegisterForeignerCommand(
    string IdentityNumber,
    string GivenName,
    string FamilyName,
    DateOnly BirthDate,
    string Nationality,
    string PassportNumber);

public static class ForeignerErrors
{
    public static readonly Error InvalidIdentityNumber =
        new("foreigner.invalid_identity_number", "Yabancı kimlik numarası geçersiz.");

    public static readonly Error Duplicate =
        new("foreigner.duplicate", "Bu yabancı kimlik numarasıyla kayıtlı bir kişi zaten var.");
}

/// <summary>Yabancı kayıt işlemleri (use-case).</summary>
/// <remarks>
/// Tek sorumluluk (SRP): yalnız iş akışını yönetir. Kurallar Domain'de, kalıcılık
/// Infrastructure'da. Bağımlılıklar yapıcıdan arayüz olarak gelir (DIP); saat bile
/// <see cref="TimeProvider"/> ile dışarıdan verilir, böylece testte zaman sabitlenebilir.
/// </remarks>
public sealed class ForeignerService(IForeignerRepository foreigners, IUnitOfWork unitOfWork, TimeProvider clock)
{
    public async Task<Result<int>> RegisterAsync(RegisterForeignerCommand command, CancellationToken cancellationToken = default)
    {
        if (!ForeignerIdentityNumber.TryCreate(command.IdentityNumber, out var identityNumber))
        {
            return ForeignerErrors.InvalidIdentityNumber;
        }

        // Bu kontrol kullanıcıya düzgün bir mesaj vermek içindir. Asıl güvence veritabanındaki
        // unique index'tir: iki istek aynı anda bu satırı geçebilir (check-then-act yarışı).
        if (await foreigners.ExistsAsync(identityNumber, cancellationToken))
        {
            return ForeignerErrors.Duplicate;
        }

        Foreigner foreigner;
        try
        {
            foreigner = new Foreigner(
                identityNumber,
                command.GivenName,
                command.FamilyName,
                command.BirthDate,
                command.Nationality,
                command.PassportNumber,
                clock.GetUtcNow());
        }
        catch (DomainException ex)
        {
            return new Error(ex.Code, ex.Message);
        }

        foreigners.Add(foreigner);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintException)
        {
            // Yarışı kaybeden istek: aynı numara bu arada başka bir istekle kaydedildi.
            return ForeignerErrors.Duplicate;
        }

        return foreigner.Id;
    }
}
