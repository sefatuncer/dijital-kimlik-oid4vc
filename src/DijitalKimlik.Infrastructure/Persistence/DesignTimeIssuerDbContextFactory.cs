using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DijitalKimlik.Infrastructure.Persistence;

/// <summary>
/// "dotnet ef" komutları bağlamı bu sınıfla oluşturur; bir web projesini başlatmaya gerek kalmaz.
/// Migration üretmek için veritabanı bağlantısı gerekmez. Uygulamak için bağlantı dizesi
/// "dotnet ef database update --connection" ile verilir.
/// </summary>
internal sealed class DesignTimeIssuerDbContextFactory : IDesignTimeDbContextFactory<IssuerDbContext>
{
    private const string PlaceholderConnection =
        "Server=localhost,1433;Database=DijitalKimlik;TrustServerCertificate=True";

    public IssuerDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<IssuerDbContext>()
            .UseSqlServer(PlaceholderConnection, IssuerDbContext.ConfigureSqlServer)
            .Options;

        return new IssuerDbContext(options);
    }
}
