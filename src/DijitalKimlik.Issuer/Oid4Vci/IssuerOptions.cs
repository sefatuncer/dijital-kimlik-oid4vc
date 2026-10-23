namespace DijitalKimlik.Issuer.Oid4Vci;

/// <summary>Issuer ayarları: appsettings.json'daki "Issuer" bölümü.</summary>
public sealed class IssuerOptions
{
    public const string SectionName = "Issuer";

    /// <summary>
    /// <c>credential_issuer</c> kimliği; kimlik bilgisindeki <c>iss</c> de budur. Üretimde https olmalı,
    /// sorgu ya da parça (#) içermemeli (OpenID4VCI §12.2.1). Geliştirmede http://localhost:5101.
    /// </summary>
    public string Identifier { get; set; } = "http://localhost:5101";

    /// <summary>Memur sayfasındaki "Cüzdanda aç" düğmesinin gittiği web cüzdanı (aynı cihaz akışı).</summary>
    public string WalletUrl { get; set; } = "http://localhost:5201";

    public string SigningKeyPath { get; set; } = "App_Data/issuer-signing-key.pem";

    public TimeSpan OfferLifetime { get; set; } = TimeSpan.FromMinutes(10);

    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(5);
}
