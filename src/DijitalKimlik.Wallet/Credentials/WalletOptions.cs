namespace DijitalKimlik.Wallet.Credentials;

/// <summary>Cüzdan ayarları: appsettings.json'daki "Wallet" bölümü.</summary>
public sealed class WalletOptions
{
    public const string SectionName = "Wallet";

    /// <summary>
    /// Kimlik bilgisi alınabilecek düzenleyiciler. Teklifteki adres bu listede yoksa cüzdan o adrese
    /// hiç istek atmaz: sunucu tarafında çalışan bir cüzdan, rastgele bir bağlantıyla iç ağdaki bir
    /// adrese istek attırılamamalı (SSRF).
    /// </summary>
    public List<string> TrustedIssuers { get; set; } = [];

    public string StorePath { get; set; } = "App_Data/wallets";
}

/// <summary>Kullanıcıya gösterilecek, kişisel veri içermeyen hata.</summary>
public sealed class WalletException(string message) : Exception(message);
