using DijitalKimlik.Application.Offers;
using DijitalKimlik.Domain.Offers;

using Microsoft.EntityFrameworkCore;

namespace DijitalKimlik.Infrastructure.Persistence.Offers;

/// <summary>
/// İyimser eşzamanlılık. Teklif kilitsiz okunur, kuralı Domain uygular, EF Core
/// <c>UPDATE ... WHERE RowVersion = @okunan</c> gönderir. Arada başka bir istek yazdıysa 0 satır
/// etkilenir, <see cref="DbUpdateConcurrencyException"/> fırlar; teklif yeniden okunup denenir.
/// </summary>
/// <remarks>
/// Çakışma seyrekse en ucuz yöntemdir: kilit beklemesi yoktur ve kural tek yerdedir (Domain).
/// Çakışma sıksa yeniden denemeler birikir. Bir teklif ömrü boyunca en fazla
/// <see cref="CredentialOffer.MaxTxCodeAttempts"/> kez değişir ve her çakışma başka bir yazmadan
/// kaynaklanır. Bu yüzden bir fazlası kadar deneme her zaman yeter.
/// </remarks>
internal sealed class OptimisticOfferRedeemer(IssuerDbContext db, TimeProvider time) : IOfferRedeemer
{
    private const int MaxAttempts = CredentialOffer.MaxTxCodeAttempts + 1;

    public OfferRedemptionStrategy Strategy => OfferRedemptionStrategy.Optimistic;

    public async Task<OfferRedemption?> RedeemAsync(string preAuthorizedCode, string txCode, CancellationToken cancellationToken = default)
    {
        var hash = CredentialOffer.HashPreAuthorizedCode(preAuthorizedCode);

        for (var attempt = 1; ; attempt++)
        {
            // Önceki denemenin eskimiş nesnesi izlemede kalmasın: yeniden okuma veritabanından gelsin.
            db.ChangeTracker.Clear();

            var offer = await db.CredentialOffers.SingleOrDefaultAsync(o => o.PreAuthorizedCodeHash == hash, cancellationToken);
            if (offer is null)
            {
                return null;
            }

            var result = offer.Redeem(preAuthorizedCode, txCode, time.GetUtcNow());

            try
            {
                // Değişiklik yoksa (ör. AlreadyRedeemed) SaveChanges veritabanına gitmez.
                await db.SaveChangesAsync(cancellationToken);
                return new OfferRedemption(result, offer.Id, offer.ResidencePermitId);
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts)
            {
                // Başka bir istek araya girdi. Kararımız eski veriye dayanıyordu: baştan oku.
            }
        }
    }
}
