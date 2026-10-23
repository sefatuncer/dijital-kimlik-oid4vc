using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

using DijitalKimlik.Crypto.Jose;

namespace DijitalKimlik.Crypto.SelectiveDisclosure;

/// <summary>
/// Key Binding JWT (RFC 9901 §4.3). Cüzdan, kimlik bilgisindeki <c>cnf.jwk</c> anahtarının özel
/// yarısıyla imzalar. Kanıtladığı üç şey: sunumu anahtarın sahibi yaptı (imza), sunum bu
/// doğrulayıcıya ve bu isteğe ait (<c>aud</c>, <c>nonce</c>), gösterilen disclosure'lar değişmedi
/// (<c>sd_hash</c>). Çalınan bir sunum başka bir istekte kullanılamaz.
/// </summary>
public static class KeyBindingJwt
{
    public const string Type = "kb+jwt";

    /// <summary>Gelecekteki <c>iat</c> için tanınan saat farkı.</summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    public static string SdHash(string sdJwtWithoutKeyBinding) => JoseJson.Sha256Base64Url(sdJwtWithoutKeyBinding);

    public static string Create(string sdJwtWithoutKeyBinding, ECDsa holderKey, string audience, string nonce, DateTimeOffset issuedAt)
    {
        if (!sdJwtWithoutKeyBinding.EndsWith('~'))
        {
            throw new ArgumentException("KB-JWT, '~' ile biten SD-JWT için üretilir.", nameof(sdJwtWithoutKeyBinding));
        }

        var payload = new JsonObject
        {
            ["iat"] = issuedAt.ToUnixTimeSeconds(),
            ["aud"] = audience,
            ["nonce"] = nonce,
            ["sd_hash"] = SdHash(sdJwtWithoutKeyBinding),
        };
        return Jws.Sign(new JsonObject { ["typ"] = Type }, payload, holderKey);
    }

    /// <summary>RFC 9901 §7.3, 6–10. adımlar.</summary>
    public static void Verify(
        string keyBindingJwt,
        string sdJwtWithoutKeyBinding,
        ECDsa holderKey,
        string expectedAudience,
        string expectedNonce,
        DateTimeOffset now,
        TimeSpan maxAge)
    {
        var jws = Jws.Parse(keyBindingJwt);
        jws.RequireType(Type);
        jws.VerifySignature(holderKey);

        var iat = JoseJson.GetInt64(jws.Payload, "iat")
            ?? throw new CryptoValidationException("invalid_kb_jwt", "KB-JWT'de iat yok.");
        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(iat);
        if (issuedAt > now + ClockSkew || now - issuedAt > maxAge)
        {
            throw new CryptoValidationException("kb_jwt_expired", "KB-JWT'nin iat değeri kabul edilen aralıkta değil.");
        }

        if (JoseJson.GetString(jws.Payload, "aud") != expectedAudience)
        {
            throw new CryptoValidationException("invalid_audience", "KB-JWT başka bir doğrulayıcı için üretilmiş.");
        }

        if (!FixedTimeEquals(JoseJson.GetString(jws.Payload, "nonce"), expectedNonce))
        {
            throw new CryptoValidationException("invalid_nonce", "KB-JWT'deki nonce bu isteğe ait değil.");
        }

        if (!FixedTimeEquals(JoseJson.GetString(jws.Payload, "sd_hash"), SdHash(sdJwtWithoutKeyBinding)))
        {
            throw new CryptoValidationException("invalid_sd_hash", "sd_hash sunulan SD-JWT ile uyuşmuyor.");
        }
    }

    private static bool FixedTimeEquals(string? actual, string expected) =>
        actual is not null
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(actual), Encoding.UTF8.GetBytes(expected));
}
