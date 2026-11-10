using FT232H.Core.Text;
using Xunit;

namespace FT232H.Tests;

public class HexCodecTests
{
    [Fact]
    public void Parse_AcceptsSpacesAndPrefixes()
    {
        Assert.Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, HexCodec.Parse("DE AD BE EF"));
        Assert.Equal(new byte[] { 0x01, 0x02 }, HexCodec.Parse("0x01, 0x02"));
        Assert.Equal(new byte[] { 0xAB, 0xCD }, HexCodec.Parse("ab:cd"));
        Assert.Equal(new byte[] { 0xFF }, HexCodec.Parse("fF"));
        Assert.Empty(HexCodec.Parse(""));
    }

    [Fact]
    public void Parse_RejectsOddNibbleCount()
    {
        Assert.Throws<FormatException>(() => HexCodec.Parse("ABC"));
    }

    [Fact]
    public void Parse_RejectsNonHexCharacters()
    {
        Assert.Throws<FormatException>(() => HexCodec.Parse("XYZW"));
    }

    [Fact]
    public void TryParse_ReportsPositionOfInvalidCharacter()
    {
        bool ok = HexCodec.TryParse("00 11 GG", out _, out var error);
        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Contains("position 7", error);
    }

    [Fact]
    public void ToHex_RoundTripsThroughParse()
    {
        var data = new byte[] { 0x00, 0x0F, 0x10, 0xA5, 0xFF };
        Assert.Equal(data, HexCodec.Parse(HexCodec.ToHex(data)));
    }

    [Fact]
    public void ToAscii_ReplacesNonPrintable()
    {
        Assert.Equal("A.C", HexCodec.ToAscii(new byte[] { 0x41, 0x00, 0x43 }));
    }

    [Fact]
    public void ToBinary_FormatsGroupsOfEight()
    {
        Assert.Equal("11011110 10101101", HexCodec.ToBinary(new byte[] { 0xDE, 0xAD }));
    }
}