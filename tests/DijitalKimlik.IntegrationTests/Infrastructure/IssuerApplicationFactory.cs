using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DijitalKimlik.IntegrationTests.Infrastructure;

/// <summary>Issuer uygulamasını test veritabanına bağlanarak bellek içinde çalıştırır.</summary>
public sealed class IssuerApplicationFactory(SqlServerFixture sql) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // UseSetting, Program.cs bağlantı dizesini okumadan önce uygulanır.
        builder.UseSetting("ConnectionStrings:IssuerDb", sql.ConnectionString);
        builder.UseSetting("Seed:DemoData", "true");
        builder.UseEnvironment("Development");
    }
}
