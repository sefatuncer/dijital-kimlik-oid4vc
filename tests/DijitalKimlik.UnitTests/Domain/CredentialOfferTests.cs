using DijitalKimlik.Domain.Offers;

namespace DijitalKimlik.UnitTests.Domain;

public class CredentialOfferTests
{
    private const string PreAuthorizedCode = "SplxlOBeZQQYbYS6WxSbIA";
    private const string TxCode = "493536";

    private static CredentialOffer NewOffer() => new(
        Guid.NewGuid(),
        TestData.Permit(),
        CredentialOffer.HashPreAuthorizedCode(PreAuthorizedCode),
        CredentialOffer.HashTxCode(PreAuthorizedCode, TxCode),
        createdBy: "memur01",
        createdAt: TestData.Now,
        lifetime: TimeSpan.FromMinutes(10));

    [Fact]
    public void Codes_are_not_stored_in_plain_text()
    {
        var offer = NewOffer();

        Assert.DoesNotContain(PreAuthorizedCode, offer.PreAuthorizedCodeHash, StringComparison.Ordinal);
        Assert.NotEqual(TxCode, offer.TxCodeHash);
    }

    [Fact]
    public void Redeem_with_correct_pin_succeeds_once()
    {
        var offer = NewOffer();

        Assert.Equal(RedeemResult.Success, offer.Redeem(PreAuthorizedCode, TxCode, TestData.Now.AddMinutes(1)));
        Assert.Equal(RedeemResult.AlreadyRedeemed, offer.Redeem(PreAuthorizedCode, TxCode, TestData.Now.AddMinutes(2)));
    }

    [Fact]
    public void Offer_locks_after_max_wrong_pins_even_if_next_pin_is_correct()
    {
        var offer = NewOffer();

        for (var i = 1; i < CredentialOffer.MaxTxCodeAttempts; i++)
        {
            Assert.Equal(RedeemResult.WrongTxCode, offer.Redeem(PreAuthorizedCode, "000000", TestData.Now));
        }

        Assert.Equal(RedeemResult.Locked, offer.Redeem(PreAuthorizedCode, "000000", TestData.Now));
        Assert.Equal(RedeemResult.Locked, offer.Redeem(PreAuthorizedCode, TxCode, TestData.Now));
        Assert.Equal(OfferStatus.Locked, offer.Status);
    }

    [Fact]
    public void Redeem_after_lifetime_expires_offer()
    {
        var offer = NewOffer();

        Assert.Equal(RedeemResult.Expired, offer.Redeem(PreAuthorizedCode, TxCode, TestData.Now.AddMinutes(10)));
        Assert.Equal(OfferStatus.Expired, offer.Status);
    }
}
