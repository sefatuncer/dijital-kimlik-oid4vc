using System.Text.Encodings.Web;
using System.Text.Unicode;

using DijitalKimlik.Wallet.Credentials;
using DijitalKimlik.Wallet.Presentations;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.WebEncoders;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// Razor varsayılan olarak yalnız ASCII'yi olduğu gibi yazar; "ğ" sayfa kaynağında "&#x11F;" olur.
// Bütün Unicode harfler serbest; <, >, & ve tırnaklar yine kodlanır (XSS koruması değişmez).
builder.Services.Configure<WebEncoderOptions>(o => o.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));

// Geliştirmede üç uygulama aynı makine adını (localhost) paylaşır ve çerezler portu ayırt etmez.
// TempData çerezinin adı uygulamaya özel olmalı; yoksa biri ötekinin iletisini siler.
builder.Services.Configure<CookieTempDataProviderOptions>(o => o.Cookie.Name = "dk-wallet-tempdata");
builder.Services.Configure<WalletOptions>(builder.Configuration.GetSection(WalletOptions.SectionName));

// Data Protection, cüzdan dosyalarını şifreler. Anahtarlar geliştirmede kullanıcı profilinde durur;
// üretimde ortak bir yere (Azure Blob + Key Vault, ağ paylaşımı + sertifika) konur.
builder.Services.AddDataProtection();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<WalletStore>();

// Typed client: IHttpClientFactory bağlantıları havuzlar. Süre ve yanıt boyutu sınırlı, çünkü
// adresler dışarıdan gelen bir tekliften ya da istekten okunur.
builder.Services.AddHttpClient<IssuanceClient>(ConfigureClient);
builder.Services.AddHttpClient<PresentationClient>(ConfigureClient);

var app = builder.Build();

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

static void ConfigureClient(HttpClient client)
{
    client.Timeout = TimeSpan.FromSeconds(10);
    client.MaxResponseContentBufferSize = 1024 * 1024;
}
