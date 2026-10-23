using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace DijitalKimlik.Crypto.Jose;

/// <summary>
/// P-256 eliptik eğri açık anahtarının JWK (RFC 7517) biçimi: <c>{"kty":"EC","crv":"P-256","x":...,"y":...}</c>.
/// Cüzdanın anahtarı kimlik bilgisine (<c>cnf.jwk</c>) ve kanıt JWT'sinin başlığına bu biçimde yazılır.
/// </summary>
public static class EcJwk
{
    private const int CoordinateLength = 32;

    public static JsonObject FromPublicKey(ECDsa key)
    {
        if (key.KeySize != 256)
        {
            throw new ArgumentException("Yalnız P-256 anahtarı destekleniyor.", nameof(key));
        }

        var parameters = key.ExportParameters(includePrivateParameters: false);
        return new JsonObject
        {
            ["kty"] = "EC",
            ["crv"] = "P-256",
            ["x"] = Base64Url.EncodeToString(parameters.Q.X),
            ["y"] = Base64Url.EncodeToString(parameters.Q.Y),
        };
    }

    /// <summary>JWK'dan doğrulama anahtarı kurar. Özel anahtar (<c>d</c>) içeren JWK reddedilir.</summary>
    public static ECDsa ToPublicKey(JsonObject jwk)
    {
        if (jwk.ContainsKey("d"))
        {
            throw new CryptoValidationException("private_key_in_jwk", "JWK özel anahtar içeriyor.");
        }

        if (JoseJson.GetString(jwk, "kty") != "EC" || JoseJson.GetString(jwk, "crv") != "P-256")
        {
            throw new CryptoValidationException("unsupported_key", "Yalnız P-256 EC anahtarı destekleniyor.");
        }

        var parameters = new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = Coordinate(jwk, "x"), Y = Coordinate(jwk, "y") },
        };

        try
        {
            // İçe aktarma, noktanın eğri üzerinde olup olmadığını da denetler.
            return ECDsa.Create(parameters);
        }
        catch (CryptographicException ex)
        {
            throw new CryptoValidationException("invalid_key", $"Anahtar geçersiz: {ex.Message}");
        }
    }

    /// <summary>
    /// RFC 7638 JWK parmak izi: zorunlu alanlar (crv, kty, x, y) sözlük sırasıyla ve boşluksuz
    /// yazılır, SHA-256'sı alınır. Aynı anahtar her zaman aynı izi verir; kimlik bilgisi bu izle
    /// cüzdan anahtarına bağlanır.
    /// </summary>
    public static string Thumbprint(JsonObject jwk)
    {
        var canonical = "{\"crv\":\"" + JoseJson.GetString(jwk, "crv")
            + "\",\"kty\":\"" + JoseJson.GetString(jwk, "kty")
            + "\",\"x\":\"" + JoseJson.GetString(jwk, "x")
            + "\",\"y\":\"" + JoseJson.GetString(jwk, "y") + "\"}";
        return Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static byte[] Coordinate(JsonObject jwk, string name)
    {
        var text = JoseJson.GetString(jwk, name)
            ?? throw new CryptoValidationException("invalid_key", $"JWK'da '{name}' yok.");
        try
        {
            var bytes = Base64Url.DecodeFromChars(text);
            return bytes.Length == CoordinateLength
                ? bytes
                : throw new CryptoValidationException("invalid_key", $"'{name}' {CoordinateLength} bayt olmalı.");
        }
        catch (FormatException)
        {
            throw new CryptoValidationException("invalid_key", $"'{name}' base64url değil.");
        }
    }
}
