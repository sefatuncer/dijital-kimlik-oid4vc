using DijitalKimlik.Application.Offers;
using DijitalKimlik.Domain.Offers;

using Microsoft.EntityFrameworkCore;

namespace DijitalKimlik.Infrastructure.Persistence.Offers;

/// <summary>
/// Atomik koşullu güncelleme (EF Core <c>ExecuteUpdateAsync</c>). Uygulama "durum Pending mi?"
/// diye sorup sonra yazmaz. Koşul, UPDATE'in WHERE kısmındadır ve karar tek komutta verilir.
/// İki istek aynı anda gelirse SQL Server satırı sırayla günceller; ikincisinde koşul artık
/// tutmaz ve 0 satır etkilenir.
/// </summary>
/// <remarks>
/// Kilit uygulama ile veritabanı arasındaki gidiş-dönüş boyunca tutulmaz, yeniden deneme de yoktur.
/// Bedeli: kural Domain'den SQL'e taşınır (iki yerde yaşar) ve ExecuteUpdate change tracker'ı atlar.
/// ExecuteUpdate güncellenen satırın yeni değerlerini döndürmez. Yanlış PIN'de sonucu ayırt etmek
/// için durum ayrıca okunur; yoğun yarışta WrongTxCode yerine Locked dönebilir, ama kayıt hep
/// doğrudur. Tek turda kesin sonuç için <c>UPDATE ... OUTPUT</c> kullanılır (Faz 3, saklı yordam).
/// </remarks>
internal sealed class AtomicOfferRedeemer(IssuerDbContext db, TimeProvider time) : IOfferRedeemer
{
    public OfferRedemptionStrategy Strategy => OfferRedemptionStrategy.Atomic;

    public async Task<OfferRedemption?> RedeemAsync(string preAuthorizedCode, string txCode, CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow();
        var hash = CredentialOffer.HashPreAuthorizedCode(preAuthorizedCode);
        var txHash = CredentialOffer.HashTxCode(preAuthorizedCode, txCode);
        var offers = db.CredentialOffers.Where(o => o.PreAuthorizedCodeHash == hash);

        var ids = await offers.AsNoTracking()
            .Select(o => new { o.Id, o.ResidencePermitId })
            .SingleOrDefaultAsync(cancellationToken);
        if (ids is null)
        {
            return null;
        }

        var open = offers.Where(o => o.Status == OfferStatus.Pending && o.ExpiresAt > now);

        // 1) Doğru PIN: UPDATE ... SET Status = Redeemed WHERE <hash> AND Status = Pending AND ...
        var redeemed = await open
            .Where(o => o.TxCodeHash == txHash)
            .ExecuteUpdateAsync(
                s => s.SetProperty(o => o.Status, OfferStatus.Redeemed)
                      .SetProperty(o => o.RedeemedAt, now),
                cancellationToken);
        if (redeemed == 1)
        {
            return new OfferRedemption(RedeemResult.Success, ids.Id, ids.ResidencePermitId);
        }

        // 2) Yanlış PIN: sayacı veritabanında artır, beşincide kilitle. SET listesindeki ifadeler
        // satırın eski değerlerini görür: "FailedTxCodeAttempts + 1" artırmadan önceki sayıya göredir.
        await open
            .Where(o => o.TxCodeHash != txHash)
            .ExecuteUpdateAsync(
                s => s.SetProperty(o => o.FailedTxCodeAttempts, o => o.FailedTxCodeAttempts + 1)
                      .SetProperty(
                          o => o.Status,
                          o => o.FailedTxCodeAttempts + 1 >= CredentialOffer.MaxTxCodeAttempts ? OfferStatus.Locked : o.Status),
                cancellationToken);

        var state = await offers.AsNoTracking()
            .Select(o => new { o.Status, o.ExpiresAt })
            .SingleAsync(cancellationToken);

        var result = state.Status switch
        {
            OfferStatus.Redeemed => RedeemResult.AlreadyRedeemed,
            OfferStatus.Locked => RedeemResult.Locked,
            OfferStatus.Expired => RedeemResult.Expired,

            // Süresi geçen teklif burada Expired yapılmaz: temizlik işi toplu günceller (Faz 11).
            _ when state.ExpiresAt <= now => RedeemResult.Expired,
            _ => RedeemResult.WrongTxCode,
        };

        return new OfferRedemption(result, ids.Id, ids.ResidencePermitId);
    }
}
