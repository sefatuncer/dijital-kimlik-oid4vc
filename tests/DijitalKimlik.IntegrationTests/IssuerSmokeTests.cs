using System.Net;

using Microsoft.AspNetCore.Mvc.Testing;

namespace DijitalKimlik.IntegrationTests;

/// <summary>
/// Duman testi (smoke test): uygulama ayağa kalkıyor ve ana sayfa yanıt veriyor mu?
/// WebApplicationFactory uygulamayı gerçek bir port açmadan, bellek içi TestServer'da çalıştırır.
/// </summary>
public class IssuerSmokeTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Home_page_returns_ok()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
