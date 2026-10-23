using System.Text.Encodings.Web;
using System.Text.Unicode;

using DijitalKimlik.Verifier.Presentations;

using Microsoft.Extensions.WebEncoders;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// Razor varsayılan olarak yalnız ASCII'yi olduğu gibi yazar; "ğ" sayfa kaynağında "&#x11F;" olur.
// Bütün Unicode harfler serbest; <, >, & ve tırnaklar yine kodlanır (XSS koruması değişmez).
builder.Services.Configure<WebEncoderOptions>(o => o.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));
builder.Services.Configure<VerifierOptions>(builder.Configuration.GetSection(VerifierOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<TransactionStore>();

// Düzenleyicinin anahtarını ve durum listesini indiren istemci. Süre ve boyut sınırlı.
builder.Services.AddHttpClient<PresentationVerifier>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    client.MaxResponseContentBufferSize = 1024 * 1024;
});

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

app.MapPresentationResponse();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

await app.RunAsync();
