using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Unicode;

namespace DijitalKimlik.Crypto.Jose;

/// <summary>
/// JOSE'nin temel işlemleri: JSON'u base64url metne çevirmek, geri okumak ve alan okumak.
/// base64url, "+/" yerine "-_" kullanan ve "=" dolgusu olmayan base64'tür; URL'de kaçırılmadan taşınır.
/// </summary>
public static class JoseJson
{
    // Türkçe harfler \uXXXX biçimine çevrilmez. HTML'e duyarlı karakterler (<, >, &) yine kaçırılır.
    private static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    public static string Serialize(JsonNode node) => node.ToJsonString(Options);

    public static string EncodeSegment(JsonNode node) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(Serialize(node)));

    public static JsonNode DecodeSegment(string segment, string what)
    {
        try
        {
            return JsonNode.Parse(Base64Url.DecodeFromChars(segment))
                ?? throw new CryptoValidationException("invalid_json", $"{what} boş.");
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            throw new CryptoValidationException("invalid_json", $"{what} çözülemedi: {ex.Message}");
        }
    }

    public static JsonObject DecodeObject(string segment, string what) =>
        DecodeSegment(segment, what) as JsonObject
            ?? throw new CryptoValidationException("invalid_json", $"{what} bir JSON nesnesi değil.");

    /// <summary>base64url(SHA-256(ASCII baytları)). SD-JWT özetleri ve sd_hash bu yolla hesaplanır.</summary>
    public static string Sha256Base64Url(string ascii) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(ascii)));

    public static string? GetString(JsonObject obj, string name) =>
        obj[name] is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    public static long? GetInt64(JsonObject obj, string name) =>
        obj[name] is JsonValue value && value.TryGetValue(out long number) ? number : null;
}
