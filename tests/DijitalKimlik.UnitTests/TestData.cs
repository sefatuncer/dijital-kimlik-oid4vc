using DijitalKimlik.Domain.Credentials;
using DijitalKimlik.Domain.Foreigners;
using DijitalKimlik.Domain.Permits;

namespace DijitalKimlik.UnitTests;

/// <summary>Testlerde tekrar eden nesne kurulumları (Object Mother deseni).</summary>
internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);

    public static Foreigner Foreigner(string identityNumber = "99123456740", DateOnly? birthDate = null) =>
        new(
            ForeignerIdentityNumber.Create(identityNumber),
            "Amina",
            "Haddad",
            birthDate ?? new DateOnly(1995, 4, 12),
            "SYR",
            "n1234567",
            Now);

    public static ResidencePermit Permit(Foreigner? foreigner = null) =>
        (foreigner ?? Foreigner()).GrantPermit(
            "IK-2026-000001",
            PermitType.ShortTerm,
            new DateOnly(2026, 1, 1),
            new DateOnly(2027, 1, 1));

    public static IssuedCredential Credential(DateTimeOffset? expiresAt = null) =>
        new(
            "urn:uuid:7f4b8c1e-0000-4000-8000-000000000001",
            Permit(),
            "urn:demo-goc:residence-permit:1",
            "holder-jkt",
            statusListId: 1,
            statusListIndex: 4242,
            issuedAt: Now,
            expiresAt: expiresAt ?? Now.AddYears(1));
}
