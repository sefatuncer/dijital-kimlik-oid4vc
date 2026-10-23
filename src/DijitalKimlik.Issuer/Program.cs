using DijitalKimlik.Application;
using DijitalKimlik.Infrastructure;
using DijitalKimlik.Infrastructure.Persistence;
using DijitalKimlik.Issuer.Oid4Vci;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddApplication();
builder.Services.AddIssuerInfrastructure(builder.Configuration);

// OpenID4VCI: anahtar ve nonce deposu uygulama boyunca tek örnek; veritabanı kullananlar istek başına.
builder.Services.Configure<IssuerOptions>(builder.Configuration.GetSection(IssuerOptions.SectionName));
builder.Services.AddSingleton<IssuerSigningKey>();
builder.Services.AddSingleton<NonceStore>();
builder.Services.AddSingleton<AccessTokenService>();
builder.Services.AddSingleton<ProofValidator>();
builder.Services.AddScoped<OfferService>();
builder.Services.AddScoped<CredentialIssuanceService>();
builder.Services.AddScoped<StatusListService>();

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

app.MapOid4Vci();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

await app.RunAsync();
