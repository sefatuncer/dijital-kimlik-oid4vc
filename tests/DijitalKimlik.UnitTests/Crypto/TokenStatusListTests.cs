using DijitalKimlik.Crypto.StatusLists;

namespace DijitalKimlik.UnitTests.Crypto;

public class TokenStatusListTests
{
    [Fact]
    public void One_bit_vector_decodes_lsb_first()
    {
        var list = TokenStatusList.Decode("eNrbuRgAAhcBXQ", bits: 1);

        Assert.Equal(new byte[] { 0xB9, 0xA3 }, list.ToUncompressedBytes());
        Assert.Equal(new byte[] { 1, 0, 0, 1, 1, 1, 0, 1, 1, 1, 0, 0, 0, 1, 0, 1 }, Enumerable.Range(0, 16).Select(i => list[i]));
    }

    [Fact]
    public void Two_bit_vector_decodes()
    {
        var list = TokenStatusList.Decode("eNo76fITAAPfAgc", bits: 2);

        Assert.Equal(new byte[] { 0xC9, 0x44, 0xF9 }, list.ToUncompressedBytes());
        Assert.Equal(new byte[] { 1, 2, 0, 3, 0, 1, 0, 1, 1, 2, 3, 3 }, Enumerable.Range(0, 12).Select(i => list[i]));
    }

    [Fact]
    public void Large_vector_has_exactly_the_listed_revocations()
    {
        var list = TokenStatusList.Decode(SpecVectors.LargeStatusList1Bit, bits: 1);
        int[] revoked = [0, 1993, 25460, 159495, 495669, 554353, 645645, 723232, 854545, 934534, 1000345];

        Assert.Equal(1 << 20, list.Size);
        Assert.Equal(revoked, Enumerable.Range(0, list.Size).Where(i => list[i] == 1));
    }

    [Fact]
    public void Written_statuses_survive_encoding()
    {
        var list = new TokenStatusList(bits: 2, size: 16);
        list[0] = 1;
        list[5] = 2;
        list[15] = 3;

        var decoded = TokenStatusList.Decode(list.Encode(), bits: 2);

        // Sıkıştırılmış bayt dizisi tek değildir; karşılaştırma çözülmüş hâl üzerinden yapılır.
        Assert.Equal(list.ToUncompressedBytes(), decoded.ToUncompressedBytes());
        Assert.Equal(2, decoded[5]);
        Assert.Equal(0, decoded[6]);
        Assert.StartsWith("eN", list.Encode(), StringComparison.Ordinal);
    }

    [Fact]
    public void Index_outside_the_list_says_nothing_and_throws()
    {
        var list = new TokenStatusList(bits: 2, size: 8);

        Assert.Throws<ArgumentOutOfRangeException>(() => list[8]);
        Assert.Throws<ArgumentOutOfRangeException>(() => list[0] = 4);
    }
}
