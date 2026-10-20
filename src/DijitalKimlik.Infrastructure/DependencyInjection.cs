using DijitalKimlik.Application.Abstractions;
using DijitalKimlik.Application.Foreigners;
using DijitalKimlik.Infrastructure.Persistence;
using DijitalKimlik.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DijitalKimlik.Infrastructure;

public static class DependencyInjection
{
    public const string IssuerConnectionName = "IssuerDb";

    /// <summary>Issuer servisinin veri erişim katmanını kaydeder.</summary>
    public static IServiceCollection AddIssuerInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Bağlantı dizesi kodda ve appsettings.json'da yok: geliştirmede user-secrets, üretimde ortam değişkeni.
        var connectionString = configuration.GetConnectionString(IssuerConnectionName)
            ?? throw new InvalidOperationException(
                $"ConnectionStrings:{IssuerConnectionName} tanımlı değil. Geliştirmede: scripts/dev-sirlar.ps1");

        // DbContext Scoped kaydedilir: her HTTP isteği kendi bağlamını alır.
        services.AddDbContext<IssuerDbContext>(options =>
            options.UseSqlServer(connectionString, IssuerDbContext.ConfigureSqlServer));

        // Aynı scope'taki IUnitOfWork, repository'lerin kullandığı DbContext örneğinin kendisi.
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<IssuerDbContext>());
        services.AddScoped<IForeignerRepository, ForeignerRepository>();

        return services;
    }
}
