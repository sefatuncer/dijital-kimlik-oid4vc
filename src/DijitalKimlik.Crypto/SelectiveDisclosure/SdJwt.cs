using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

using DijitalKimlik.Crypto.Jose;

namespace DijitalKimlik.Crypto.SelectiveDisclosure;

/// <summary>
/// SD-JWT (RFC 9901 §4): <c>&lt;imzalı JWT&gt;~&lt;disclosure 1&gt;~...~&lt;disclosure N&gt;~[KB-JWT]</c>.
/// KB-JWT yoksa sondaki <c>~</c> zorunludur. Bu sınıf biçimi çözer ve kurar; güveni
/// <see cref="VerifyAndProcess"/> sağlar.
/// </summary>
public sealed class SdJwt
{
    public const string HashAlgorithm = "sha-256";

    private SdJwt(Jws issuerJwt, IReadOnlyList<Disclosure> disclosures, string? keyBindingJwt, string withoutKeyBinding)
    {
        IssuerJwt = issuerJwt;
        Disclosures = disclosures;
        KeyBindingJwt = keyBindingJwt;
        WithoutKeyBinding = withoutKeyBinding;
    }

    public Jws IssuerJwt { get; }

    public IReadOnlyList<Disclosure> Disclosures { get; }

    public string? KeyBindingJwt { get; }

    /// <summary>KB-JWT'siz kısım, sondaki <c>~</c> dahil. KB-JWT'deki <c>sd_hash</c> bunun özetidir.</summary>
    public string WithoutKeyBinding { get; }

    /// <summary>
    /// SD-JWT üretir. <paramref name="plainClaims"/> her zaman görünür (iss, vct, cnf, exp, status...);
    /// <paramref name="disclosableClaims"/> yalnız özetiyle imzalanır. <paramref name="decoyCount"/>
    /// kadar sahte özet eklenir: gizli alan sayısı da belli olmasın diye (RFC 9901 §4.2.5).
    /// </summary>
    public static string Issue(
        JsonObject header,
        JsonObject plainClaims,
        IEnumerable<KeyValuePair<string, JsonNode?>> disclosableClaims,
        ECDsa issuerKey,
        int decoyCount = 0)
    {
        var payload = (JsonObject)plainClaims.DeepClone();
        var disclosures = new List<Disclosure>();
        foreach (var (name, value) in disclosableClaims)
        {
            if (payload.ContainsKey(name))
            {
                throw new ArgumentException($"'{name}' hem açık hem gizli alan olamaz.", nameof(disclosableClaims));
            }

            disclosures.Add(Disclosure.ForProperty(name, value));
        }

        var decoys = Enumerable.Range(0, decoyCount)
            .Select(_ => Base64Url.EncodeToString(SHA256.HashData(RandomNumberGenerator.GetBytes(32))));

        // Özetler sıralanır: disclosure'ların hangi sırayla eklendiği içerikten anlaşılmasın.
        payload["_sd"] = new JsonArray([.. disclosures.Select(d => d.Digest).Concat(decoys)
            .Order(StringComparer.Ordinal).Select(d => (JsonNode?)JsonValue.Create(d))]);
        payload["_sd_alg"] = HashAlgorithm;

        return Combine(Jws.Sign(header, payload, issuerKey), disclosures);
    }

    public static string Combine(string issuerJwt, IEnumerable<Disclosure> disclosures) =>
        issuerJwt + "~" + string.Concat(disclosures.Select(d => d.Encoded + "~"));

    public static SdJwt Parse(string value)
    {
        var parts = value.Split('~');
        if (parts.Length < 2)
        {
            throw new CryptoValidationException("invalid_sd_jwt", "SD-JWT en az bir '~' içermeli.");
        }

        var disclosures = new List<Disclosure>(parts.Length - 2);
        foreach (var part in parts[1..^1])
        {
            if (part.Length == 0)
            {
                throw new CryptoValidationException("invalid_sd_jwt", "SD-JWT'de boş disclosure var.");
            }

            disclosures.Add(Disclosure.Parse(part));
        }

        var keyBinding = parts[^1].Length == 0 ? null : parts[^1];
        return new SdJwt(Jws.Parse(parts[0]), disclosures, keyBinding, value[..(value.LastIndexOf('~') + 1)]);
    }

    /// <summary>
    /// RFC 9901 §7.1: imzayı doğrular, disclosure'ları özetleriyle eşleyip içeriğe yerleştirir ve
    /// <c>_sd</c>, <c>_sd_alg</c> alanlarını kaldırır. Sonuç, sahibin gösterdiği alanlarla birlikte
    /// imzalı içeriktir.
    /// </summary>
    public JsonObject VerifyAndProcess(ECDsa issuerKey)
    {
        IssuerJwt.VerifySignature(issuerKey);
        return DisclosureProcessor.Process(IssuerJwt.Payload, Disclosures);
    }
}
