using DijitalKimlik.Application.Abstractions;
using DijitalKimlik.Application.Foreigners;
using DijitalKimlik.Application.Offers;
using DijitalKimlik.Infrastructure.Persistence;
using DijitalKimlik.Infrastructure.Persistence.Offers;
using DijitalKimlik.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DijitalKimlik.Infrastructure;

public static class DependencyInjection
{
    public const string IssuerConnectionName = "IssuerDb";

    public const string OfferRedemptionStrategyKey = "Persistence:OfferRedemptionStrategy";

    /// <summary>Issuer servisinin veri erişim katmanını kaydeder.</summary>
    public static IServiceCollection AddIssuerInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Bağlantı dizesi kodda ve appsettings.json'da yok: geliştirmede user-secrets, üretimde ortam değişkeni.
        var connectionString = configuration.GetConnectionString(IssuerConnectionName)
            ?? throw new InvalidOperationException(
                $"ConnectionStrings:{IssuerConnectionName} tanımlı değil. Geliştirmede: scripts/dev-veritabani.ps1");

        // DbContext Scoped kaydedilir: her HTTP isteği kendi bağlamını alır.
        services.AddDbContext<IssuerDbContext>(options =>
            options.UseSqlServer(connectionString, IssuerDbContext.ConfigureSqlServer));

        // Aynı scope'taki IUnitOfWork, repository'lerin kullandığı DbContext örneğinin kendisi.
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<IssuerDbContext>());
        services.AddScoped<IForeignerRepository, ForeignerRepository>();

        // Üç eşzamanlılık yöntemi de keyed service olarak kayıtlı: testler ve karşılaştırmalar
        // anahtarıyla istediğini alır. Anahtarsız IOfferRedeemer, yapılandırmada seçilendir.
        services.AddKeyedScoped<IOfferRedeemer, OptimisticOfferRedeemer>(OfferRedemptionStrategy.Optimistic);
        services.AddKeyedScoped<IOfferRedeemer, PessimisticOfferRedeemer>(OfferRedemptionStrategy.Pessimistic);
        services.AddKeyedScoped<IOfferRedeemer, AtomicOfferRedeemer>(OfferRedemptionStrategy.Atomic);
        var strategy = configuration.GetValue(OfferRedemptionStrategyKey, OfferRedemptionStrategy.Optimistic);
        services.AddScoped(sp => sp.GetRequiredKeyedService<IOfferRedeemer>(strategy));

        return services;
    }
}
