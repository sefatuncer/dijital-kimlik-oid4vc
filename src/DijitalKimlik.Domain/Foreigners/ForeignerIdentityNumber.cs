using DijitalKimlik.Domain.Common;

namespace DijitalKimlik.Domain.Foreigners;

/// <summary>
/// Yabancı kimlik numarası (value object): 11 hane, "99" ile başlar. Son iki hane, T.C. kimlik
/// numarasındaki algoritmayla hesaplanan kontrol haneleridir.
/// </summary>
/// <remarks>
/// Value object'in kimliği yoktur, değeriyle karşılaştırılır (record eşitliği). Geçersiz bir
/// numara hiç oluşturulamaz; bu yüzden numarayı kullanan kodun tekrar doğrulama yapması gerekmez.
/// </remarks>
public sealed record ForeignerIdentityNumber
{
    public const int Length = 11;
    public const string Prefix = "99";

    private ForeignerIdentityNumber(string value) => Value = value;

    public string Value { get; }

    public static ForeignerIdentityNumber Create(string? input) =>
        TryCreate(input, out var number)
            ? number
            : throw new DomainException("foreigner.invalid_identity_number",
                "Yabancı kimlik numarası 99 ile başlayan, kontrol haneleri doğru 11 haneli bir sayı olmalı.");

    public static bool TryCreate(string? input, out ForeignerIdentityNumber number)
    {
        number = null!;
        var value = input?.Trim();
        if (value is null || value.Length != Length || !value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        if (!value.All(char.IsAsciiDigit) || ComputeCheckDigits(value.AsSpan(0, 9)) != value[9..])
        {
            return false;
        }

        number = new ForeignerIdentityNumber(value);
        return true;
    }

    /// <summary>
    /// İlk 9 haneden 10. ve 11. haneyi hesaplar:
    /// 10. hane = ((1+3+5+7+9. haneler) * 7 - (2+4+6+8. haneler)) mod 10,
    /// 11. hane = (ilk 10 hanenin toplamı) mod 10.
    /// </summary>
    public static string ComputeCheckDigits(ReadOnlySpan<char> firstNine)
    {
        if (firstNine.Length != 9)
        {
            throw new ArgumentException("Tam 9 hane gerekli.", nameof(firstNine));
        }

        int odd = 0, even = 0;
        for (var i = 0; i < 9; i++)
        {
            var digit = firstNine[i] - '0';
            if (i % 2 == 0)
            {
                odd += digit;
            }
            else
            {
                even += digit;
            }
        }

        // C#'ta % negatif sonuç verebilir; +10 ve tekrar mod ile 0-9 aralığına çekilir.
        var tenth = (((odd * 7) - even) % 10 + 10) % 10;
        var eleventh = (odd + even + tenth) % 10;
        return $"{tenth}{eleventh}";
    }

    public override string ToString() => Value;
}
