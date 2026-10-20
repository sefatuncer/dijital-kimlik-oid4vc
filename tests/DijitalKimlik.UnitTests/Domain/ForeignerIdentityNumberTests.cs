using DijitalKimlik.Domain.Common;
using DijitalKimlik.Domain.Foreigners;

namespace DijitalKimlik.UnitTests.Domain;

public class ForeignerIdentityNumberTests
{
    [Theory]
    [InlineData("99123456740")]
    [InlineData("99000000042")]
    [InlineData(" 99123456740 ")] // baştaki ve sondaki boşluk kırpılır
    public void TryCreate_accepts_valid_numbers(string input)
    {
        var ok = ForeignerIdentityNumber.TryCreate(input, out var number);

        Assert.True(ok);
        Assert.Equal(input.Trim(), number.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("99123456741")] // 11. hane yanlış
    [InlineData("99123456750")] // 10. hane yanlış
    [InlineData("9912345674")] // 10 hane
    [InlineData("991234567400")] // 12 hane
    [InlineData("10000000146")] // kontrol haneleri doğru ama 99 ile başlamıyor (T.C. kimlik numarası biçimi)
    [InlineData("99A23456740")]
    public void TryCreate_rejects_invalid_numbers(string? input)
    {
        Assert.False(ForeignerIdentityNumber.TryCreate(input, out _));
    }

    [Fact]
    public void Create_throws_domain_exception_with_code()
    {
        var ex = Assert.Throws<DomainException>(() => ForeignerIdentityNumber.Create("123"));

        Assert.Equal("foreigner.invalid_identity_number", ex.Code);
    }

    [Fact]
    public void ComputeCheckDigits_matches_hand_calculation()
    {
        // 9,9,1,2,3,4,5,6,7 -> tek sıradakiler 9+1+3+5+7=25, çift sıradakiler 9+2+4+6=21
        // 10. hane = (25*7 - 21) mod 10 = 154 mod 10 = 4
        // 11. hane = (9+9+1+2+3+4+5+6+7+4) mod 10 = 50 mod 10 = 0
        Assert.Equal("40", ForeignerIdentityNumber.ComputeCheckDigits("991234567"));
    }

    [Fact]
    public void Value_objects_with_same_value_are_equal()
    {
        var a = ForeignerIdentityNumber.Create("99123456740");
        var b = ForeignerIdentityNumber.Create("99123456740");

        Assert.Equal(a, b);
        Assert.NotSame(a, b);
    }
}
