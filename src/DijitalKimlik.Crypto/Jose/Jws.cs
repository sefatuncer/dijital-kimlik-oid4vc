using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace DijitalKimlik.Crypto.Jose;

/// <summary>
/// Kompakt JWS (RFC 7515): <c>base64url(başlık).base64url(içerik).base64url(imza)</c>.
/// Yalnız ES256 (P-256 + SHA-256) desteklenir. İmza 64 bayttır: r ve s yan yana (IEEE P1363);
/// .NET'in <see cref="ECDsa.SignData(byte[], HashAlgorithmName)"/> varsayılanı zaten bu biçimdir.
/// </summary>
public sealed class Jws
{
    public const string Es256 = "ES256";

    private readonly string _signingInput;
    private readonly byte[] _signature;

    private Jws(JsonObject header, JsonObject payload, string signingInput, byte[] signature, string compact)
    {
        Header = header;
        Payload = payload;
        _signingInput = signingInput;
        _signature = signature;
        Compact = compact;
    }

    public JsonObject Header { get; }

    public JsonObject Payload { get; }

    public string Compact { get; }

    public string? Type => JoseJson.GetString(Header, "typ");

    public string? Algorithm => JoseJson.GetString(Header, "alg");

    /// <summary>Başlığa <c>alg: ES256</c> ekler ve imzalar.</summary>
    public static string Sign(JsonObject header, JsonObject payload, ECDsa privateKey)
    {
        var signedHeader = new JsonObject { ["alg"] = Es256 };
        foreach (var (name, value) in header)
        {
            if (name != "alg")
            {
                signedHeader[name] = value?.DeepClone();
            }
        }

        var signingInput = JoseJson.EncodeSegment(signedHeader) + "." + JoseJson.EncodeSegment(payload);
        var signature = privateKey.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256);
        return signingInput + "." + Base64Url.EncodeToString(signature);
    }

    /// <summary>Yalnız biçimi çözer; imzayı denetlemez. Güvenmeden önce <see cref="VerifySignature"/> çağrılır.</summary>
    public static Jws Parse(string compact)
    {
        var parts = compact.Split('.');
        if (parts.Length != 3)
        {
            throw new CryptoValidationException("invalid_jws", "JWS üç parçadan oluşmalı.");
        }

        var header = JoseJson.DecodeObject(parts[0], "JWS başlığı");
        var payload = JoseJson.DecodeObject(parts[1], "JWS içeriği");
        byte[] signature;
        try
        {
            signature = Base64Url.DecodeFromChars(parts[2]);
        }
        catch (FormatException)
        {
            throw new CryptoValidationException("invalid_jws", "İmza base64url değil.");
        }

        return new Jws(header, payload, parts[0] + "." + parts[1], signature, compact);
    }

    public void VerifySignature(ECDsa publicKey)
    {
        // Algoritma başlıktan okunur ama yalnız beklenen değer kabul edilir: "none" ya da
        // HS256 gibi simetrik bir algoritmayla imza atlatılamaz.
        if (Algorithm != Es256)
        {
            throw new CryptoValidationException("unsupported_alg", $"İmza algoritması ES256 olmalı, gelen: {Algorithm ?? "yok"}.");
        }

        if (!publicKey.VerifyData(Encoding.ASCII.GetBytes(_signingInput), _signature, HashAlgorithmName.SHA256))
        {
            throw new CryptoValidationException("invalid_signature", "İmza doğrulanamadı.");
        }
    }

    public void RequireType(string expected)
    {
        if (Type != expected)
        {
            throw new CryptoValidationException("invalid_typ", $"typ '{expected}' olmalı, gelen: {Type ?? "yok"}.");
        }
    }
}
