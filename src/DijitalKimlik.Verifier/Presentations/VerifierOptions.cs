namespace DijitalKimlik.Verifier.Presentations;

/// <summary>Doğrulayıcı ayarları: appsettings.json'daki "Verifier" bölümü.</summary>
public sealed class VerifierOptions
{
    public const string SectionName = "Verifier";

    /// <summary>Doğrulayıcının kendi adresi; yanıt adresi ve <c>client_id</c> bundan türetilir.</summary>
    public string Identifier { get; set; } = "http://localhost:5301";

    /// <summary>Aynı cihaz akışında "Cüzdanla paylaş" düğmesinin açtığı web cüzdanı.</summary>
    public string WalletUrl { get; set; } = "http://localhost:5201";

    /// <summary>
    /// Kabul edilen düzenleyiciler (güven listesi). İmzası geçerli olsa da listede olmayan bir
    /// düzenleyicinin kimlik bilgisi reddedilir. Üretimde bu liste bir güven çerçevesinden gelir
    /// (ör. AB'nin güvenilir listeleri).
    /// </summary>
    public List<string> TrustedIssuers { get; set; } = [];

    public TimeSpan TransactionLifetime { get; set; } = TimeSpan.FromMinutes(10);

    public string ResponseUri => Identifier.TrimEnd('/') + "/presentations/response";

    /// <summary>OpenID4VP §5.9.3: imzasız istekte client_id, yanıt adresinin önekli hâlidir.</summary>
    public string ClientId => "redirect_uri:" + ResponseUri;
}
