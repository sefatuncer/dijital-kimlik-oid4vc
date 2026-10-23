using System.Text.Json.Nodes;

namespace DijitalKimlik.Verifier.Presentations;

/// <summary>
/// Doğrulayıcının bir iş için istediği alanlar. Veri minimizasyonu (KVKK md. 4): her senaryo yalnız
/// işin gerektirdiğini ister. Yaş kontrolü doğum tarihini değil, yalnız "18 yaşından büyük mü?"
/// cevabını ister.
/// </summary>
public sealed record Scenario(string Key, string Title, string Purpose, IReadOnlyList<string> Claims)
{
    public const string Vct = "urn:demo-goc-kurumu:ikamet-izni:1";

    /// <summary>DCQL sorgusundaki kimlik bilgisi kimliği; yanıttaki <c>vp_token</c> bu anahtarla gelir.</summary>
    public const string QueryId = "ikamet_izni";

    public static IReadOnlyList<Scenario> All { get; } =
    [
        new("konaklama", "Otel konaklama kaydı",
            "Yabancı konuk bildirimi için ad, soyad, uyruk ve kimlik numarası; iznin bitiş tarihi.",
            ["given_name", "family_name", "nationality", "identity_number", "valid_until"]),
        new("yas", "Yaş doğrulama",
            "Yalnız 18 yaşından büyük olup olmadığı. Ad, doğum tarihi ve kimlik numarası istenmez.",
            ["age_over_18"]),
    ];

    public static IReadOnlyDictionary<string, string> ClaimLabels { get; } = new Dictionary<string, string>
    {
        ["given_name"] = "Ad",
        ["family_name"] = "Soyad",
        ["birthdate"] = "Doğum tarihi",
        ["nationality"] = "Uyruk",
        ["identity_number"] = "Yabancı kimlik no",
        ["permit_number"] = "İzin no",
        ["permit_type"] = "İzin türü",
        ["valid_from"] = "Geçerlilik başlangıcı",
        ["valid_until"] = "Geçerlilik bitişi",
        ["age_over_18"] = "18 yaşından büyük",
    };

    public static Scenario? Find(string key) => All.FirstOrDefault(s => s.Key == key);

    public static string LabelOf(string claim) => ClaimLabels.GetValueOrDefault(claim, claim);

    /// <summary>OpenID4VP 1.0 §6: Digital Credentials Query Language.</summary>
    public JsonObject DcqlQuery() => new()
    {
        ["credentials"] = new JsonArray(new JsonObject
        {
            ["id"] = QueryId,
            ["format"] = "dc+sd-jwt",
            ["meta"] = new JsonObject { ["vct_values"] = new JsonArray(Vct) },
            ["claims"] = new JsonArray([.. Claims.Select(c => (JsonNode)new JsonObject { ["path"] = new JsonArray(c) })]),
        }),
    };
}
