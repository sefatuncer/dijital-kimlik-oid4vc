using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

using DijitalKimlik.Crypto.Jose;

namespace DijitalKimlik.Crypto.SelectiveDisclosure;

/// <summary>
/// SD-JWT disclosure'ı (RFC 9901 §4.2): seçerek gösterilebilen tek bir alanın açık hâli.
/// Nesne alanı için <c>[salt, ad, değer]</c>, dizi elemanı için <c>[salt, değer]</c> dizisinin
/// base64url'idir. İmzalı JWT'de yalnız özeti (<see cref="Digest"/>) bulunur; sahibi disclosure'ı
/// göstermedikçe alan görünmez. Salt, aynı değerin özetten tahmin edilmesini engeller.
/// </summary>
public sealed class Disclosure
{
    private Disclosure(string encoded, string salt, string? name, JsonNode? value)
    {
        Encoded = encoded;
        Salt = salt;
        Name = name;
        Value = value;
        // Özet, çözülmüş JSON'un değil base64url metnin kendisinin özetidir.
        Digest = JoseJson.Sha256Base64Url(encoded);
    }

    public string Encoded { get; }

    public string Salt { get; }

    /// <summary>Nesne alanının adı; dizi elemanı disclosure'ında <c>null</c>.</summary>
    public string? Name { get; }

    public JsonNode? Value { get; }

    public string Digest { get; }

    public static Disclosure ForProperty(string name, JsonNode? value)
    {
        if (name is "_sd" or "...")
        {
            throw new ArgumentException($"'{name}' disclosure adı olamaz.", nameof(name));
        }

        var salt = NewSalt();
        var encoded = JoseJson.EncodeSegment(new JsonArray(salt, name, value?.DeepClone()));
        return new Disclosure(encoded, salt, name, value?.DeepClone());
    }

    public static Disclosure ForArrayElement(JsonNode? value)
    {
        var salt = NewSalt();
        var encoded = JoseJson.EncodeSegment(new JsonArray(salt, value?.DeepClone()));
        return new Disclosure(encoded, salt, name: null, value?.DeepClone());
    }

    public static Disclosure Parse(string encoded)
    {
        if (JoseJson.DecodeSegment(encoded, "Disclosure") is not JsonArray array)
        {
            throw new CryptoValidationException("invalid_disclosure", "Disclosure bir JSON dizisi olmalı.");
        }

        var salt = AsString(array[0]) ?? throw new CryptoValidationException("invalid_disclosure", "Salt metin olmalı.");
        return array.Count switch
        {
            3 => new Disclosure(encoded, salt,
                AsString(array[1]) ?? throw new CryptoValidationException("invalid_disclosure", "Alan adı metin olmalı."),
                array[2]?.DeepClone()),
            2 => new Disclosure(encoded, salt, name: null, array[1]?.DeepClone()),
            _ => throw new CryptoValidationException("invalid_disclosure", "Disclosure 2 ya da 3 elemanlı olmalı."),
        };
    }

    /// <summary>RFC 9901 §9.3: salt'ın rastgele kısmı en az 128 bit olmalı. 16 bayt = 128 bit.</summary>
    private static string NewSalt() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));

    private static string? AsString(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out string? text) ? text : null;
}
