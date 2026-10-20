using DijitalKimlik.Domain.Common;
using DijitalKimlik.Domain.Credentials;

namespace DijitalKimlik.UnitTests.Domain;

public class IssuedCredentialTests
{
    [Fact]
    public void New_credential_is_active_and_valid_in_status_list()
    {
        var credential = TestData.Credential();

        Assert.Equal(CredentialStatus.Active, credential.Status);
        Assert.Equal(0x00, credential.StatusListValue);
    }

    [Fact]
    public void Suspend_then_reinstate_returns_to_active()
    {
        var credential = TestData.Credential();

        credential.Suspend("Belge incelemesi", TestData.Now);
        Assert.Equal(0x02, credential.StatusListValue);

        credential.Reinstate(TestData.Now.AddDays(1));
        Assert.Equal(CredentialStatus.Active, credential.Status);
        Assert.Null(credential.StatusReason);
    }

    [Fact]
    public void Revoked_is_terminal()
    {
        var credential = TestData.Credential();
        credential.Revoke("İkamet izni iptal edildi", TestData.Now);

        var ex = Assert.Throws<DomainException>(() => credential.Reinstate(TestData.Now));

        Assert.Equal("credential.invalid_transition", ex.Code);
        Assert.Equal(0x01, credential.StatusListValue);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Revoke_requires_reason(string reason)
    {
        var credential = TestData.Credential();

        var ex = Assert.Throws<DomainException>(() => credential.Revoke(reason, TestData.Now));

        Assert.Equal("credential.reason_required", ex.Code);
        Assert.Equal(CredentialStatus.Active, credential.Status);
    }

    [Fact]
    public void TryExpire_only_after_expiry_time()
    {
        var credential = TestData.Credential(expiresAt: TestData.Now.AddDays(30));

        Assert.False(credential.TryExpire(TestData.Now.AddDays(29)));
        Assert.True(credential.TryExpire(TestData.Now.AddDays(30)));
        Assert.Equal(CredentialStatus.Expired, credential.Status);

        // İkinci çağrı bir şey değiştirmez (idempotent).
        Assert.False(credential.TryExpire(TestData.Now.AddDays(31)));
    }
}
