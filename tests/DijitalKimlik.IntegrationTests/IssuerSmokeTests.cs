using System.Net;

using DijitalKimlik.Application.Offers;
using DijitalKimlik.Infrastructure;
using DijitalKimlik.Infrastructure.Persistence;
using DijitalKimlik.IntegrationTests.Infrastructure;

using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DijitalKimlik.IntegrationTests;

/// <summary>
/// Duman testi (smoke test): uygulama veritabanıyla birlikte ayağa kalkıyor ve yanıt veriyor mu?
/// WebApplicationFactory uygulamayı gerçek bir port açmadan, bellek içi TestServer'da çalıştırır.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class IssuerSmokeTests(SqlServerFixture sql) : IDisposable
{
    private readonly IssuerApplicationFactory _factory = new(sql);

    [Fact]
    public async Task Home_page_returns_ok_and_demo_data_is_seeded()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Veritabanı diğer testlerle paylaşılıyor: sayım ("tam 1 status list") test sırasına bağlı olur
        // ve bazen kırılır. Bunun yerine bu testin beklediği kayıtların kendisi aranır.
        await using var db = sql.CreateContext();
        var seeded = IssuerDevelopmentSeeder.IdentityNumbers;
        Assert.Equal(seeded.Count, await db.Foreigners.CountAsync(f => seeded.Contains(f.IdentityNumber)));
        Assert.True(await db.StatusLists.AnyAsync(l => l.Capacity == IssuerDevelopmentSeeder.DefaultStatusListCapacity));
    }

    [Theory]
    [InlineData(OfferRedemptionStrategy.Optimistic)]
    [InlineData(OfferRedemptionStrategy.Pessimistic)]
    [InlineData(OfferRedemptionStrategy.Atomic)]
    [InlineData(OfferRedemptionStrategy.StoredProcedure)]
    public async Task Offer_redemption_strategy_is_selected_by_configuration(OfferRedemptionStrategy strategy)
    {
        await using var factory = _factory.WithWebHostBuilder(b =>
            b.UseSetting(DependencyInjection.OfferRedemptionStrategyKey, strategy.ToString()));

        await using var scope = factory.Services.CreateAsyncScope();

        Assert.Equal(strategy, scope.ServiceProvider.GetRequiredService<IOfferRedeemer>().Strategy);
    }

    public void Dispose() => _factory.Dispose();
}
