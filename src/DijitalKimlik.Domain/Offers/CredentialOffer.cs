using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

using DijitalKimlik.Domain.Common;
using DijitalKimlik.Domain.Permits;

namespace DijitalKimlik.Domain.Offers;

public enum OfferStatus
{
    Pending = 1,
    Redeemed = 2,
    Expired = 3,
    Locked = 4,
}

/// <remarks>Değerler sabittir: <c>issuer.usp_RedeemOffer</c> saklı yordamı bu sayıları döndürür.</remarks>
public enum RedeemResult
{
    Success = 0,
    WrongTxCode = 1,
    Locked = 2,
    Expired = 3,
    AlreadyRedeemed = 4,
}

/// <summary>
/// OpenID4VCI kimlik bilgisi teklifi (pre-authorized code akışı). Memur oluşturur, cüzdan
/// pre-authorized code ve PIN (tx_code) ile bir kez kullanır.
/// </summary>
/// <remarks>
/// Güvenlik kararları:
/// <list type="bullet">
/// <item>Kodlar düz metin saklanmaz, yalnız SHA-256 özetleri saklanır. Veritabanı sızsa bile kod kullanılamaz.</item>
/// <item>6 haneli PIN'in tek başına özeti kaba kuvvetle bulunabilir (10^6 olasılık). Bu yüzden PIN,
/// yüksek entropili pre-authorized code ile birlikte özetlenir.</item>
/// <item>Özetler sabit zamanlı karşılaştırılır (timing attack'a karşı).</item>
/// <item>Beş hatalı PIN denemesinden sonra teklif kilitlenir.</item>
/// </list>
/// </remarks>
public sealed class CredentialOffer : Entity
{
    public const int MaxTxCodeAttempts = 5;

    private CredentialOffer()
    {
    }

    public CredentialOffer(
        Guid publicId,
        ResidencePermit permit,
        string preAuthorizedCodeHash,
        string txCodeHash,
        string createdBy,
        DateTimeOffset createdAt,
        TimeSpan lifetime)
    {
        if (lifetime <= TimeSpan.Zero)
        {
            throw new DomainException("offer.invalid_lifetime", "Teklif süresi pozitif olmalı.");
        }

        PublicId = publicId;
        ResidencePermit = permit;
        PreAuthorizedCodeHash = preAuthorizedCodeHash;
        TxCodeHash = txCodeHash;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
        ExpiresAt = createdAt.Add(lifetime);
        Status = OfferStatus.Pending;
    }

    /// <summary>credential_offer_uri içinde kullanılan, tahmin edilemez kimlik.</summary>
    public Guid PublicId { get; private set; }

    public int ResidencePermitId { get; private set; }

    public ResidencePermit ResidencePermit { get; private set; } = null!;

    public string PreAuthorizedCodeHash { get; private set; } = string.Empty;

    public string TxCodeHash { get; private set; } = string.Empty;

    public int FailedTxCodeAttempts { get; private set; }

    public OfferStatus Status { get; private set; }

    public string CreatedBy { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RedeemedAt { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    /// <summary>Pre-authorized code'un saklanan özeti: base64url(SHA-256(kod)).</summary>
    public static string HashPreAuthorizedCode(string preAuthorizedCode) =>
        Base64UrlSha256(preAuthorizedCode);

    /// <summary>PIN'in saklanan özeti: base64url(SHA-256(kod + ":" + PIN)).</summary>
    public static string HashTxCode(string preAuthorizedCode, string txCode) =>
        Base64UrlSha256(preAuthorizedCode + ":" + txCode);

    /// <summary>Token uç noktası çağırır: PIN doğruysa teklifi kullanılmış sayar.</summary>
    public RedeemResult Redeem(string preAuthorizedCode, string txCode, DateTimeOffset now)
    {
        switch (Status)
        {
            case OfferStatus.Redeemed:
                return RedeemResult.AlreadyRedeemed;
            case OfferStatus.Locked:
                return RedeemResult.Locked;
            case OfferStatus.Expired:
                return RedeemResult.Expired;
        }

        if (now >= ExpiresAt)
        {
            Status = OfferStatus.Expired;
            return RedeemResult.Expired;
        }

        if (!FixedTimeEquals(HashTxCode(preAuthorizedCode, txCode), TxCodeHash))
        {
            FailedTxCodeAttempts++;
            if (FailedTxCodeAttempts >= MaxTxCodeAttempts)
            {
                Status = OfferStatus.Locked;
                return RedeemResult.Locked;
            }

            return RedeemResult.WrongTxCode;
        }

        Status = OfferStatus.Redeemed;
        RedeemedAt = now;
        return RedeemResult.Success;
    }

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));

    private static string Base64UrlSha256(string input) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
}
