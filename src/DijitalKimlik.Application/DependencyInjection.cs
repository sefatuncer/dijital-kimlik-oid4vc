using DijitalKimlik.Application.Foreigners;

using Microsoft.Extensions.DependencyInjection;

namespace DijitalKimlik.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Application katmanının servislerini kaydeder. Use-case servisleri Scoped: her HTTP isteği
    /// kendi örneğini alır, çünkü Scoped olan DbContext'e bağımlılar. Singleton bir servis Scoped
    /// bir servisi tutarsa "captive dependency" oluşur ve DbContext istekler arasında paylaşılır.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ForeignerService>();
        return services;
    }
}
