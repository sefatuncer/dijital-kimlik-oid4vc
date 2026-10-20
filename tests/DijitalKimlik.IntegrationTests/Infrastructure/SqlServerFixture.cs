using DijitalKimlik.Infrastructure.Persistence;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

using Testcontainers.MsSql;

namespace DijitalKimlik.IntegrationTests.Infrastructure;

/// <summary>
/// Testler için gerçek bir SQL Server. Testcontainers, test başında bir Docker konteyneri açar,
/// sonunda siler. Bellek içi sağlayıcı (InMemory) yerine gerçek SQL Server kullanılır, çünkü
/// unique index, check constraint, rowversion ve trigger gibi davranışları yalnız o gösterir.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    // Yerel geliştirmedeki imajla aynı: ikinci bir imaj indirilmez.
    private readonly MsSqlContainer _container =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        ConnectionString = new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = "DijitalKimlikTest",
        }.ConnectionString;

        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public IssuerDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<IssuerDbContext>()
            .UseSqlServer(ConnectionString, IssuerDbContext.ConfigureSqlServer)
            .Options);
}

/// <summary>Tüm entegrasyon testleri tek konteyneri paylaşır (her sınıf için yeniden açılmaz).</summary>
[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SQL Server";
}
