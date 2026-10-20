using DijitalKimlik.Application.Foreigners;
using DijitalKimlik.Domain.Foreigners;

using Microsoft.EntityFrameworkCore;

namespace DijitalKimlik.Infrastructure.Persistence.Repositories;

internal sealed class ForeignerRepository(IssuerDbContext db) : IForeignerRepository
{
    // EF, value converter sayesinde value object karşılaştırmasını "IdentityNumber = @p" olarak çevirir.
    public Task<bool> ExistsAsync(ForeignerIdentityNumber identityNumber, CancellationToken cancellationToken = default) =>
        db.Foreigners.AnyAsync(f => f.IdentityNumber == identityNumber, cancellationToken);

    // Aggregate kökü izinleriyle birlikte yüklenir (eager loading), yoksa kural kontrolü eksik veriyle yapılır.
    public Task<Foreigner?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        db.Foreigners.Include(f => f.Permits).FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

    public void Add(Foreigner foreigner) => db.Foreigners.Add(foreigner);
}
