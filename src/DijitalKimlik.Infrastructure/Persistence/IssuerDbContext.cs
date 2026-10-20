using DijitalKimlik.Application.Abstractions;
using DijitalKimlik.Domain.Auditing;
using DijitalKimlik.Domain.Credentials;
using DijitalKimlik.Domain.Foreigners;
using DijitalKimlik.Domain.Offers;
using DijitalKimlik.Domain.Permits;
using DijitalKimlik.Domain.StatusLists;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace DijitalKimlik.Infrastructure.Persistence;

/// <summary>
/// Issuer (kimlik bilgisi veren kurum) servisinin veritabanı bağlamı. Tablolar "issuer"
/// şemasında, denetim kayıtları "audit" şemasında. Cüzdan ve doğrulayıcı kendi bağlamlarını
/// ve şemalarını kullanır; hiçbir servis başka bir servisin tablosuna doğrudan yazmaz.
/// </summary>
public sealed class IssuerDbContext(DbContextOptions<IssuerDbContext> options) : DbContext(options), IUnitOfWork
{
    public const string Schema = "issuer";
    public const string AuditSchema = "audit";

    public DbSet<Foreigner> Foreigners => Set<Foreigner>();

    public DbSet<ResidencePermit> ResidencePermits => Set<ResidencePermit>();

    public DbSet<CredentialOffer> CredentialOffers => Set<CredentialOffer>();

    public DbSet<IssuedCredential> IssuedCredentials => Set<IssuedCredential>();

    public DbSet<StatusList> StatusLists => Set<StatusList>();

    public DbSet<AuditEntry> AuditLog => Set<AuditEntry>();

    /// <summary>Uygulama ve design-time (dotnet ef) aynı SQL Server ayarlarını kullansın.</summary>
    public static void ConfigureSqlServer(SqlServerDbContextOptionsBuilder sql)
    {
        // Migration geçmişi de servisin kendi şemasında tutulur.
        sql.MigrationsHistoryTable("__EFMigrationsHistory", Schema);

        // Geçici ağ hatalarında (ör. bağlantı koptu, failover) komut yeniden denenir.
        sql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (SqlServerErrors.IsUniqueViolation(ex))
        {
            throw new UniqueConstraintException("Benzersiz olması gereken bir değer zaten kayıtlı.", ex);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IssuerDbContext).Assembly);
    }
}
