using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using DijitalKimlik.Crypto.Jose;
using DijitalKimlik.Crypto.SelectiveDisclosure;

namespace DijitalKimlik.Wallet.Credentials;

public sealed record ClaimView(string Name, string Label, string Value);

/// <summary>Cüzdandaki bir kimlik bilgisi: SD-JWT VC'nin kendisi, ona bağlı özel anahtar ve gösterim bilgisi.</summary>
public sealed class StoredCredential
{
    public required string Id { get; init; }

    public required string Issuer { get; init; }

    public required string IssuerName { get; init; }

    public required string Vct { get; init; }

    public required string DisplayName { get; init; }

    public string BackgroundColor { get; init; } = "#0b3d5c";

    public string TextColor { get; init; } = "#ffffff";

    /// <summary>Düzenleyici metadata'sındaki Türkçe alan adları ("given_name" → "Ad").</summary>
    public Dictionary<string, string> ClaimLabels { get; init; } = [];

    /// <summary>Düzenleyiciden gelen SD-JWT VC: imzalı kısım ve bütün disclosure'lar.</summary>
    public required string Credential { get; init; }

    /// <summary>
    /// Kimlik bilgisinin bağlı olduğu özel anahtar (PEM). Gerçek bir mobil cüzdanda anahtar telefonun
    /// güvenli donanımında (Secure Enclave, StrongBox) üretilir ve oradan hiç çıkmaz.
    /// </summary>
    public required string HolderKeyPem { get; init; }

    public DateTimeOffset ReceivedAt { get; init; }

    [JsonIgnore]
    public DateTimeOffset? ExpiresAt =>
        JoseJson.GetInt64(SdJwt.Parse(Credential).IssuerJwt.Payload, "exp") is { } exp ? DateTimeOffset.FromUnixTimeSeconds(exp) : null;

    /// <summary>Cüzdan kendi kimlik bilgisinin bütün alanlarını görür: disclosure'ların hepsi onda.</summary>
    public IReadOnlyList<ClaimView> Claims() =>
        [.. SdJwt.Parse(Credential).Disclosures
            .Where(d => d.Name is not null)
            .Select(d => new ClaimView(d.Name!, LabelOf(d.Name!), Format(d.Value)))];

    public string LabelOf(string claimName) => ClaimLabels.GetValueOrDefault(claimName, claimName);

    public static string Format(JsonNode? value) => value switch
    {
        JsonValue v when v.TryGetValue(out bool flag) => flag ? "Evet" : "Hayır",
        JsonValue v when v.TryGetValue(out string? text) => text ?? string.Empty,
        null => string.Empty,
        _ => value.ToJsonString(),
    };
}
