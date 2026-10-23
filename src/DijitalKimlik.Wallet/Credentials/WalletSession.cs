using System.Buffers.Text;
using System.Security.Cryptography;

namespace DijitalKimlik.Wallet.Credentials;

/// <summary>
/// Tarayıcıya bağlı demo cüzdan: kimliği 128 bit rastgele bir değerdir ve HttpOnly bir çerezde durur.
/// Kullanıcı hesabı (ASP.NET Core Identity) gelince bu sınıfın yerini oturum açmış kullanıcı alır.
/// </summary>
public static class WalletSession
{
    public const string CookieName = "dk-wallet";

    private const int IdLength = 22; // 16 bayt base64url

    public static string GetOrCreateWalletId(HttpContext context)
    {
        if (context.Request.Cookies[CookieName] is { } existing && IsValidId(existing))
        {
            return existing;
        }

        var id = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));
        context.Response.Cookies.Append(CookieName, id, new CookieOptions
        {
            HttpOnly = true,
            Secure = context.Request.IsHttps,

            // Lax: düzenleyicinin ya da doğrulayıcının sayfasından gelen bağlantı (üst düzey GET)
            // çerezi taşır; başka bir sitenin arka planda gönderdiği POST taşımaz.
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromDays(30),
            IsEssential = true,
        });
        return id;
    }

    /// <summary>Kimlik dosya adında kullanılır: yalnız base64url karakterleri, sabit uzunluk (yol aşımı yok).</summary>
    public static bool IsValidId(string id) =>
        id.Length == IdLength && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
