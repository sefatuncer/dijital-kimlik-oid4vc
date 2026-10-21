using System.Data;

using DijitalKimlik.Application.Offers;
using DijitalKimlik.Domain.Offers;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DijitalKimlik.Infrastructure.Persistence.Offers;

/// <summary>
/// Kötümser eşzamanlılık. Satır, transaction içinde <c>WITH (UPDLOCK, ROWLOCK)</c> ile okunur.
/// UPDLOCK transaction bitene kadar tutulur ve iki UPDLOCK birbiriyle uyumsuzdur. Bu yüzden aynı
/// teklifi okumak isteyen ikinci istek, birincisi commit edene kadar bekler ve güncel durumu görür.
/// </summary>
/// <remarks>
/// Çakışma sık ve yeniden deneme pahalıysa uygundur. Bedeli: kilit süresi boyunca bekleme ve
/// birden çok satır kilitlenirse deadlock riski. Kilit sırası her yerde aynı tutulmalıdır.
/// Kural yine Domain'dedir.
/// </remarks>
internal sealed class PessimisticOfferRedeemer(IssuerDbContext db, TimeProvider time) : IOfferRedeemer
{
    public OfferRedemptionStrategy Strategy => OfferRedemptionStrategy.Pessimistic;

    public Task<OfferRedemption?> RedeemAsync(string preAuthorizedCode, string txCode, CancellationToken cancellationToken = default)
    {
        var hash = CredentialOffer.HashPreAuthorizedCode(preAuthorizedCode);

        // EnableRetryOnFailure açıkken kullanıcı transaction'ı doğrudan açılamaz: geçici bir hatada
        // EF yalnız son komutu değil, transaction'ın tamamını yeniden çalıştırabilmelidir. Bu yüzden
        // transaction, execution strategy'nin tekrar çalıştırabileceği bir temsilcinin içindedir.
        var strategy = db.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(
            async ct =>
            {
                db.ChangeTracker.Clear();
                await using var transaction = await db.Database.BeginTransactionAsync(ct);

                // FromSql değerleri parametre olarak gönderir (SQL injection yok). EF bu sorguyu
                // alt sorgu olarak sarar; tablo ipucu alt sorgunun içinde de geçerlidir.
                // Parametrenin tipi sütunla aynı olmalı: char(43). Düz string nvarchar olarak gider;
                // SQL Server sütunu dönüştürür (CONVERT_IMPLICIT), indeksi kullanamaz ve tabloyu
                // tarar. UPDLOCK ile tarama, başka tekliflerin kilitli satırlarında da bekler ve
                // farklı teklifler birbirini sıraya sokar. Ölçüm aracı (tools/DijitalKimlik.Olcum)
                // bunu 8 işçide 161 işlem/sn ile yakaladı; diğer yöntemler 1.000'in üzerindeydi.
                var hashParameter = new SqlParameter("@hash", SqlDbType.Char, 43) { Value = hash };
                var offer = await db.CredentialOffers
                    .FromSql($"SELECT * FROM issuer.CredentialOffers WITH (UPDLOCK, ROWLOCK) WHERE PreAuthorizedCodeHash = {hashParameter}")
                    .SingleOrDefaultAsync(ct);
                if (offer is null)
                {
                    return null;
                }

                var result = offer.Redeem(preAuthorizedCode, txCode, time.GetUtcNow());
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                return new OfferRedemption(result, offer.Id, offer.ResidencePermitId);
            },
            cancellationToken);
    }
}
