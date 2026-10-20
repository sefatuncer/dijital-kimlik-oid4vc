using DijitalKimlik.Domain.Foreigners;

namespace DijitalKimlik.Application.Foreigners;

/// <summary>
/// Yabancı aggregate'i için repository. Arayüz Application'da, uygulaması Infrastructure'da
/// (EF Core). Testte sahte (fake) bir uygulama verilir.
/// </summary>
public interface IForeignerRepository
{
    Task<bool> ExistsAsync(ForeignerIdentityNumber identityNumber, CancellationToken cancellationToken = default);

    Task<Foreigner?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    void Add(Foreigner foreigner);
}
