using DijitalKimlik.Application;
using DijitalKimlik.Infrastructure;
using DijitalKimlik.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddApplication();
builder.Services.AddIssuerInfrastructure(builder.Configuration);

var app = builder.Build();

// Demo verisi yalnız geliştirmede ve açıkça istenirse yüklenir. Migration'lar burada değil, ayrı
// bir adımda uygulanır (scripts/dev-veritabani.ps1): uygulamanın DDL yetkisi yok.
if (app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("Seed:DemoData"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<IssuerDbContext>();
    await IssuerDevelopmentSeeder.SeedAsync(db, TimeProvider.System);
}

// Ara katman (middleware) sırası önemlidir: istek bu sırayla girer, yanıt ters sırayla çıkar.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");

    // Yerel geliştirmede yalnız HTTP dinliyoruz. HTTPS yönlendirmesi ve HSTS üretimde açık.
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

await app.RunAsync();
