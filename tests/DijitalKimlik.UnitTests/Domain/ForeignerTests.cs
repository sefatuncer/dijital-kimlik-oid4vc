using DijitalKimlik.Domain.Common;
using DijitalKimlik.Domain.Permits;

namespace DijitalKimlik.UnitTests.Domain;

public class ForeignerTests
{
    [Theory]
    [InlineData(2008, 10, 21, 18)] // bugün tam 18. doğum günü
    [InlineData(2008, 10, 22, 17)] // 18. doğum gününe bir gün var
    [InlineData(2000, 2, 29, 26)] // artık yıl doğumlu
    public void AgeOn_counts_completed_years(int year, int month, int day, int expectedAge)
    {
        var foreigner = TestData.Foreigner(birthDate: new DateOnly(year, month, day));

        Assert.Equal(expectedAge, foreigner.AgeOn(new DateOnly(2026, 10, 21)));
    }

    [Fact]
    public void Constructor_normalizes_nationality_and_passport()
    {
        var foreigner = TestData.Foreigner();

        Assert.Equal("SYR", foreigner.Nationality);
        Assert.Equal("N1234567", foreigner.PassportNumber);
    }

    [Fact]
    public void GrantPermit_rejects_overlapping_permit_of_same_type()
    {
        var foreigner = TestData.Foreigner();
        TestData.Permit(foreigner);

        var ex = Assert.Throws<DomainException>(() => foreigner.GrantPermit(
            "IK-2026-000002", PermitType.ShortTerm, new DateOnly(2026, 6, 1), new DateOnly(2027, 6, 1)));

        Assert.Equal("permit.overlapping", ex.Code);
    }

    [Fact]
    public void GrantPermit_allows_new_permit_after_previous_is_cancelled()
    {
        var foreigner = TestData.Foreigner();
        TestData.Permit(foreigner).Cancel("Adres değişikliği bildirilmedi.");

        var permit = foreigner.GrantPermit(
            "IK-2026-000002", PermitType.ShortTerm, new DateOnly(2026, 6, 1), new DateOnly(2027, 6, 1));

        Assert.Equal(2, foreigner.Permits.Count);
        Assert.True(permit.IsValidOn(new DateOnly(2026, 7, 1)));
    }
}
