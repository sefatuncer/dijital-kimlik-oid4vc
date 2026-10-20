namespace DijitalKimlik.Application.Abstractions;

/// <summary>
/// Unit of Work: bir iş adımındaki tüm değişiklikler tek seferde, tek transaction'da kaydedilir.
/// EF Core'da DbContext zaten bir Unit of Work'tür; bu arayüz Application katmanının EF'e
/// bağımlı olmamasını sağlar (Dependency Inversion).
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
