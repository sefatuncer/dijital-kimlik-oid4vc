using System.Security.Cryptography;
using System.Text.Json.Nodes;

using DijitalKimlik.Crypto;
using DijitalKimlik.Crypto.Jose;

using Microsoft.Extensions.Options;

namespace DijitalKimlik.Issuer.Oid4Vci;

/// <summary>Kanıttan çıkan, kimlik bilgisinin bağlanacağı cüzdan anahtarı.</summary>
public sealed record HolderKey(JsonObject Jwk, string Thumbprint);

/// <summary>
/// Cüzdanın "bu özel anahtar bende" kanıtı (OpenID4VCI Ek F.1, <c>typ: openid4vci-proof+jwt</c>).
/// Kimlik bilgisi bu anahtara bağlanır (<c>cnf.jwk</c>); çalınan bir kimlik bilgisi anahtarsız sunulamaz.
/// </summary>
public sealed class ProofValidator(NonceStore nonces, IOptions<IssuerOptions> options, TimeProvider clock)
{
    public const string Type = "openid4vci-proof+jwt";

    /// <summary><c>iat</c> için kabul edilen saat farkı: cüzdanın saati biraz ileri ya da geri olabilir.</summary>
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

    public HolderKey Validate(string proofJwt)
    {
        Jws jws;
        try
        {
            jws = Jws.Parse(proofJwt);
            jws.RequireType(Type);
        }
        catch (CryptoValidationException ex)
        {
            throw new Oid4VciException("invalid_proof", $"Kanıt JWT'si okunamadı ({ex.Code}).");
        }

        // Anahtar başlıkta jwk olarak gelir. kid ya da x5c ile birlikte gelirse hangisinin geçerli
        // olduğu belirsiz kalır; F.1 bunlardan yalnız birini ister.
        if (jws.Header["jwk"] is not JsonObject jwk || jws.Header.ContainsKey("kid") || jws.Header.ContainsKey("x5c"))
        {
            throw new Oid4VciException("invalid_proof", "Kanıt başlığında yalnız jwk olmalı.");
        }

        JsonObject publicJwk;
        try
        {
            using var key = EcJwk.ToPublicKey(jwk);
            jws.VerifySignature(key);

            // Saklanan ve kimlik bilgisine yazılan JWK yalnız açık anahtar alanlarını içerir.
            publicJwk = EcJwk.FromPublicKey(key);
        }
        catch (CryptoValidationException ex)
        {
            throw new Oid4VciException("invalid_proof", $"Kanıt imzası doğrulanamadı ({ex.Code}).");
        }
        catch (CryptographicException)
        {
            throw new Oid4VciException("invalid_proof", "Kanıttaki anahtar geçersiz.");
        }

        var payload = jws.Payload;
        if (JoseJson.GetString(payload, "aud") != options.Value.Identifier)
        {
            throw new Oid4VciException("invalid_proof", "Kanıtın aud alanı bu düzenleyici değil.");
        }

        if (JoseJson.GetInt64(payload, "iat") is not { } iat
            || (DateTimeOffset.FromUnixTimeSeconds(iat) - clock.GetUtcNow()).Duration() > MaxClockSkew)
        {
            throw new Oid4VciException("invalid_proof", "Kanıtın iat alanı yok ya da zaman aralığı dışında.");
        }

        // Nonce en son tüketilir: imzası bozuk bir kanıt, geçerli bir nonce'u boşa harcatmasın.
        if (JoseJson.GetString(payload, "nonce") is not { } nonce || !nonces.TryConsume(nonce))
        {
            throw new Oid4VciException("invalid_nonce", "c_nonce yok, süresi dolmuş ya da daha önce kullanılmış.");
        }

        return new HolderKey(publicJwk, EcJwk.Thumbprint(publicJwk));
    }
}
