using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

using DijitalKimlik.Crypto.Jose;
using DijitalKimlik.Domain.Offers;
using DijitalKimlik.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DijitalKimlik.Issuer.Oid4Vci;

/// <summary>Memurun ekranında gösterilen teklif. PIN yalnız bu ekranda görünür, hiçbir yerde saklanmaz.</summary>
public sealed record CreatedOffer(
    Guid PublicId,
    string HolderName,
    string PermitNumber,
    string TxCode,
    DateTimeOffset ExpiresAt,
    string OfferJson,
    string OfferUri,
    string WalletLink);

/// <summary>
/// Pre-authorized code akışının ilk adımı (OpenID4VCI §4.1): memur kimliği doğrulanmış kişi için
/// teklif oluşturur. Teklif, cüzdana bağlantı ya da QR kod olarak verilir; PIN ayrı kanaldan söylenir.
/// Bağlantıyı ele geçiren biri PIN'i bilmeden kimlik bilgisini alamaz.
/// </summary>
public sealed class OfferService(IssuerDbContext db, IOptions<IssuerOptions> options, TimeProvider clock)
{
    public const string PreAuthorizedCodeGrant = "urn:ietf:params:oauth:grant-type:pre-authorized_code";

    public const int TxCodeLength = 6;

    public async Task<CreatedOffer?> CreateAsync(int permitId, string createdBy, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var permit = await db.ResidencePermits
            .Include(p => p.Foreigner)
            .SingleOrDefaultAsync(p => p.Id == permitId, cancellationToken);
        if (permit is null || !permit.IsValidOn(DateOnly.FromDateTime(now.UtcDateTime)))
        {
            return null;
        }

        // 256 bit rastgele kod tahmin edilemez. PIN 6 hanelidir; tek başına zayıftır, bu yüzden
        // deneme sınırı (5) ve kodla birlikte özetlenmesi onu korur.
        var preAuthorizedCode = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var txCode = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);

        var offer = new CredentialOffer(
            Guid.NewGuid(),
            permit,
            CredentialOffer.HashPreAuthorizedCode(preAuthorizedCode),
            CredentialOffer.HashTxCode(preAuthorizedCode, txCode),
            createdBy,
            now,
            options.Value.OfferLifetime);
        db.CredentialOffers.Add(offer);
        await db.SaveChangesAsync(cancellationToken);

        var offerJson = JoseJson.Serialize(new JsonObject
        {
            ["credential_issuer"] = options.Value.Identifier,
            ["credential_configuration_ids"] = new JsonArray(CredentialIssuanceService.ConfigurationId),
            ["grants"] = new JsonObject
            {
                [PreAuthorizedCodeGrant] = new JsonObject
                {
                    ["pre-authorized_code"] = preAuthorizedCode,
                    ["tx_code"] = new JsonObject
                    {
                        ["length"] = TxCodeLength,
                        ["input_mode"] = "numeric",
                        ["description"] = "Memurun size söylediği 6 haneli PIN",
                    },
                },
            },
        });

        // Teklif değer olarak (by value) taşınır; sunucuda ayrı bir teklif adresi tutulmaz.
        var query = "credential_offer=" + Uri.EscapeDataString(offerJson);
        return new CreatedOffer(
            offer.PublicId,
            permit.Foreigner.GivenName + " " + permit.Foreigner.FamilyName,
            permit.PermitNumber,
            txCode,
            offer.ExpiresAt,
            offerJson,
            "openid-credential-offer://?" + query,
            options.Value.WalletUrl.TrimEnd('/') + "/offers/receive?" + query);
    }
}
