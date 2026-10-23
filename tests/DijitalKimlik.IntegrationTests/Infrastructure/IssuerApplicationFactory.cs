using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DijitalKimlik.IntegrationTests.Infrastructure;

/// <summary>Issuer uygulamasını test veritabanına bağlanarak bellek içinde çalıştırır.</summary>
public sealed class IssuerApplicationFactory(SqlServerFixture sql) : WebApplicationFactory<Program>
{
    /// <summary>Test istemcisinin adresi; kanıtların <c>aud</c> alanı ve durum listesi adresi buna göre.</summary>
    public const string IssuerIdentifier = "http://localhost";

    // Her fabrika kendi imza anahtarını üretir; anahtar kaynak klasörüne değil geçici klasöre yazılır.
    private readonly string _signingKeyPath = Path.Combine(Path.GetTempPath(), $"dk-issuer-test-{Guid.NewGuid():N}.pem");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // UseSetting, Program.cs bağlantı dizesini okumadan önce uygulanır.
        builder.UseSetting("ConnectionStrings:IssuerDb", sql.ConnectionString);
        builder.UseSetting("Seed:DemoData", "true");
        builder.UseSetting("Issuer:Identifier", IssuerIdentifier);
        builder.UseSetting("Issuer:SigningKeyPath", _signingKeyPath);
        builder.UseEnvironment("Development");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        File.Delete(_signingKeyPath);
    }
}
