using System.Buffers.Text;
using System.IO.Compression;

namespace DijitalKimlik.Crypto.StatusLists;

/// <summary>
/// Token Status List (draft-ietf-oauth-status-list-21): her kimlik bilgisinin durumu, sıkıştırılmış
/// bir bit dizisinde kendi sırasında (<c>idx</c>) tutulur. Doğrulayıcı listeyi bir kez indirir;
/// hangi kimlik bilgisini sorduğunu düzenleyiciye söylemez.
/// Kurallar: girdiler bayta en düşük bitten (0) başlayarak yerleşir; bayt dizisi zlib (RFC 1950,
/// DEFLATE'i saran biçim) ile sıkıştırılıp base64url yazılır.
/// </summary>
public sealed class TokenStatusList
{
    /// <summary>Çözülen liste için üst sınır: sıkıştırma bombasına karşı.</summary>
    public const int MaxDecodedBytes = 16 * 1024 * 1024;

    private readonly byte[] _bytes;

    public TokenStatusList(int bits, int size)
    {
        if (bits is not (1 or 2 or 4 or 8))
        {
            throw new ArgumentOutOfRangeException(nameof(bits), "bits 1, 2, 4 ya da 8 olmalı.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        Bits = bits;
        _bytes = new byte[((size * bits) + 7) / 8];
        Size = _bytes.Length * 8 / bits;
    }

    private TokenStatusList(int bits, byte[] bytes)
    {
        Bits = bits;
        _bytes = bytes;
        Size = bytes.Length * 8 / bits;
    }

    public int Bits { get; }

    /// <summary>Listedeki girdi sayısı.</summary>
    public int Size { get; }

    public byte this[int index]
    {
        get
        {
            var (byteIndex, shift) = Locate(index);
            return (byte)((_bytes[byteIndex] >> shift) & Mask);
        }

        set
        {
            if (value > Mask)
            {
                throw new ArgumentOutOfRangeException(nameof(value), $"{Bits} bitlik listede değer en çok {Mask} olabilir.");
            }

            var (byteIndex, shift) = Locate(index);
            _bytes[byteIndex] = (byte)((_bytes[byteIndex] & ~(Mask << shift)) | (value << shift));
        }
    }

    private int Mask => (1 << Bits) - 1;

    public byte[] ToUncompressedBytes() => (byte[])_bytes.Clone();

    /// <summary>Status List Token'daki <c>status_list.lst</c> değeri.</summary>
    public string Encode()
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(_bytes);
        }

        return Base64Url.EncodeToString(output.ToArray());
    }

    public static TokenStatusList Decode(string lst, int bits)
    {
        if (bits is not (1 or 2 or 4 or 8))
        {
            throw new CryptoValidationException("invalid_status_list", "bits 1, 2, 4 ya da 8 olmalı.");
        }

        try
        {
            using var input = new MemoryStream(Base64Url.DecodeFromChars(lst));
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = zlib.Read(buffer)) > 0)
            {
                if (output.Length + read > MaxDecodedBytes)
                {
                    throw new CryptoValidationException("invalid_status_list", "Durum listesi çok büyük.");
                }

                output.Write(buffer, 0, read);
            }

            return new TokenStatusList(bits, output.ToArray());
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException)
        {
            throw new CryptoValidationException("invalid_status_list", $"Durum listesi çözülemedi: {ex.Message}");
        }
    }

    private (int ByteIndex, int Shift) Locate(int index)
    {
        // Sınır dışı sıra için hiçbir şey söylenemez; çağıran bunu ret sayar (taslak §8.3, 6. adım).
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Size);
        var perByte = 8 / Bits;
        return (index / perByte, (index % perByte) * Bits);
    }
}
