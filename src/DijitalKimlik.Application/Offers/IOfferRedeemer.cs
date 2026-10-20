using DijitalKimlik.Domain.Offers;

namespace DijitalKimlik.Application.Offers;

/// <summary>
/// Aynı teklifi aynı anda kullanmaya çalışan isteklere karşı üç eşzamanlılık yöntemi. Üçü de
/// aynı sözleşmeyi uygular; hangisinin çalışacağı yapılandırmadan seçilir
/// (<c>Persistence:OfferRedemptionStrategy</c>).
/// </summary>
public enum OfferRedemptionStrategy
{
    /// <summary>İyimser: kilit yok, rowversion ile çakışma kaydederken yakalanır ve yeniden denenir.</summary>
    Optimistic = 1,

    /// <summary>Kötümser: satır okunurken UPDLOCK ile kilitlenir, diğer istekler sırada bekler.</summary>
    Pessimistic = 2,

    /// <summary>Atomik: karar, tek bir koşullu UPDATE'in WHERE koşulunda veritabanınca verilir.</summary>
    Atomic = 3,
}

/// <summary>Teklifin kullanılma sonucu ve sonraki adımlar için gereken kimlikler.</summary>
public sealed record OfferRedemption(RedeemResult Result, int OfferId, int ResidencePermitId);

/// <summary>OpenID4VCI token uç noktasının pre-authorized code ve PIN doğrulaması.</summary>
public interface IOfferRedeemer
{
    OfferRedemptionStrategy Strategy { get; }

    /// <returns>Kod hiçbir teklife ait değilse <see langword="null"/> (OAuth: <c>invalid_grant</c>).</returns>
    Task<OfferRedemption?> RedeemAsync(string preAuthorizedCode, string txCode, CancellationToken cancellationToken = default);
}
