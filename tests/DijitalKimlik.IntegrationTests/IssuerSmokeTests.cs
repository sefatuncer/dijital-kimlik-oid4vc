using System.Net;

using DijitalKimlik.Application.Offers;
using DijitalKimlik.Infrastructure;
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

        await using var db = sql.CreateContext();
        Assert.True(await db.Foreigners.CountAsync() >= 5);
        Assert.Equal(1, await db.StatusLists.CountAsync());
    }

    [Theory]
    [InlineData(OfferRedemptionStrategy.Optimistic)]
    [InlineData(OfferRedemptionStrategy.Pessimistic)]
    [InlineData(OfferRedemptionStrategy.Atomic)]
    public async Task Offer_redemption_strategy_is_selected_by_configuration(OfferRedemptionStrategy strategy)
    {
        await using var factory = _factory.WithWebHostBuilder(b =>
            b.UseSetting(DependencyInjection.OfferRedemptionStrategyKey, strategy.ToString()));

        await using var scope = factory.Services.CreateAsyncScope();

        Assert.Equal(strategy, scope.ServiceProvider.GetRequiredService<IOfferRedeemer>().Strategy);
    }

    public void Dispose() => _factory.Dispose();
}
