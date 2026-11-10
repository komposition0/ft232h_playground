using FT232H.Core.Spi;
using FT232H.Hardware.Mpsse;
using Xunit;

namespace FT232H.Tests;

/// <summary>
/// Golden-vector tests: every expected byte sequence is taken from FTDI AN_108
/// (FT_000109 v1.5). If a test here fails, the MPSSE encoding is wrong on the wire.
/// </summary>
public class MpsseCommandBuilderTests
{
    [Fact]
    public void SetLowByte_EncodesValueAndDirection()
    {
        var bytes = new MpsseCommandBuilder().SetLowByte(0x08, 0x0B).Build();
        Assert.Equal(new byte[] { 0x80, 0x08, 0x0B }, bytes);
    }

    [Fact]
    public void SetHighByte_EncodesValueAndDirection()
    {
        var bytes = new MpsseCommandBuilder().SetHighByte(0xFF, 0xFF).Build();
        Assert.Equal(new byte[] { 0x82, 0xFF, 0xFF }, bytes);
    }

    [Fact]
    public void GetBytes_EncodeSingleOpcodes()
    {
        Assert.Equal(new byte[] { 0x81 }, new MpsseCommandBuilder().GetLowByte().Build());
        Assert.Equal(new byte[] { 0x83 }, new MpsseCommandBuilder().GetHighByte().Build());
    }

    [Fact]
    public void SetClockDivisor_EncodesLittleEndian()
    {
        // divisor 0x0123 → [0x86, 0x23, 0x01]
        var bytes = new MpsseCommandBuilder().SetClockDivisor(0x0123).Build();
        Assert.Equal(new byte[] { 0x86, 0x23, 0x01 }, bytes);
    }

    [Fact]
    public void ClockDivideAndLoopback_Opcodes()
    {
        Assert.Equal(new byte[] { 0x8A }, new MpsseCommandBuilder().DisableClockDivideBy5().Build());
        Assert.Equal(new byte[] { 0x84 }, new MpsseCommandBuilder().EnableLoopback().Build());
        Assert.Equal(new byte[] { 0x85 }, new MpsseCommandBuilder().DisableLoopback().Build());
        Assert.Equal(new byte[] { 0x87 }, new MpsseCommandBuilder().SendImmediate().Build());
    }

    [Fact]
    public void ClockDataOut_MsbNegativeEdge_UsesOpcode0x11()
    {
        // Mode 0/3 write: data changes on falling edge, MSB first.
        var bytes = new MpsseCommandBuilder()
            .ClockDataOut(DataOutEdge.Negative, BitOrder.MsbFirst, new byte[] { 0xAB, 0xCD })
            .Build();
        Assert.Equal(new byte[] { 0x11, 0x01, 0x00, 0xAB, 0xCD }, bytes);
    }

    [Fact]
    public void ClockDataOut_LengthFieldIsCountMinusOne()
    {
        var payload = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        var bytes = new MpsseCommandBuilder()
            .ClockDataOut(DataOutEdge.Negative, BitOrder.MsbFirst, payload)
            .Build();
        Assert.Equal(0x11, bytes[0]);
        Assert.Equal(0xFF, bytes[1]); // 256 - 1 = 0x00FF little endian
        Assert.Equal(0x00, bytes[2]);
        Assert.Equal(259, bytes.Length);
    }

    [Theory]
    [InlineData(DataInEdge.Positive, BitOrder.MsbFirst, 0x20)]
    [InlineData(DataInEdge.Negative, BitOrder.MsbFirst, 0x24)]
    [InlineData(DataInEdge.Positive, BitOrder.LsbFirst, 0x28)]
    [InlineData(DataInEdge.Negative, BitOrder.LsbFirst, 0x2C)]
    public void ClockDataIn_UsesCorrectOpcodePerEdgeAndOrder(DataInEdge edge, BitOrder order, byte expectedOpcode)
    {
        var bytes = new MpsseCommandBuilder().ClockDataIn(edge, order, 4).Build();
        Assert.Equal(new byte[] { expectedOpcode, 0x03, 0x00 }, bytes);
    }

    [Theory]
    [InlineData(DataInOutEdge.InOnPositiveOutOnNegative, BitOrder.MsbFirst, 0x31)]
    [InlineData(DataInOutEdge.InOnNegativeOutOnPositive, BitOrder.MsbFirst, 0x34)]
    [InlineData(DataInOutEdge.InOnPositiveOutOnNegative, BitOrder.LsbFirst, 0x39)]
    [InlineData(DataInOutEdge.InOnNegativeOutOnPositive, BitOrder.LsbFirst, 0x3C)]
    public void ClockDataInOut_UsesCorrectOpcodePerEdgeAndOrder(DataInOutEdge edge, BitOrder order, byte expectedOpcode)
    {
        var bytes = new MpsseCommandBuilder().ClockDataInOut(edge, order, new byte[] { 0x01 }).Build();
        Assert.Equal(new byte[] { expectedOpcode, 0x00, 0x00, 0x01 }, bytes);
    }

    [Theory]
    [InlineData(DataOutEdge.Positive, BitOrder.MsbFirst, 0x10)]
    [InlineData(DataOutEdge.Negative, BitOrder.MsbFirst, 0x11)]
    [InlineData(DataOutEdge.Positive, BitOrder.LsbFirst, 0x18)]
    [InlineData(DataOutEdge.Negative, BitOrder.LsbFirst, 0x19)]
    public void ClockDataOut_UsesCorrectOpcodePerEdgeAndOrder(DataOutEdge edge, BitOrder order, byte expectedOpcode)
    {
        var bytes = new MpsseCommandBuilder().ClockDataOut(edge, order, new byte[] { 0x55 }).Build();
        Assert.Equal(new byte[] { expectedOpcode, 0x00, 0x00, 0x55 }, bytes);
    }

    [Fact]
    public void LongPayload_IsSplitIntoMultipleCommandsWithContinuousData()
    {
        // One command maxes out at 65536 bytes; 65537 bytes → 2 commands.
        var payload = new byte[65537];
        var bytes = new MpsseCommandBuilder()
            .ClockDataOut(DataOutEdge.Negative, BitOrder.MsbFirst, payload)
            .Build();
        // cmd1: 0x11 len(0xFFFF) + 65536 data; cmd2: 0x11 len(0x0000) + 1 data
        Assert.Equal(3 + 65536 + 3 + 1, bytes.Length);
        Assert.Equal(0x11, bytes[0]);
        Assert.Equal(0xFF, bytes[1]);
        Assert.Equal(0xFF, bytes[2]);
    }

    [Fact]
    public void InitializeSpi_Mode0_EmitsExpectedPreamble()
    {
        var config = new SpiConfiguration { ClockHz = 6_000_000, Mode = SpiMode.Mode0 };
        var bytes = new MpsseCommandBuilder().InitializeSpi(config).Build();

        // 0x8A (disable /5), 0x86 divisor(9): 60 MHz/(1+9) = 6 MHz,
        // 0x85 (loopback off), 0x80 value(0x08 = CS high, SCK low) dir(0x0B)
        Assert.Equal(new byte[] { 0x8A, 0x86, 0x09, 0x00, 0x85, 0x80, 0x08, 0x0B }, bytes);
    }

    [Fact]
    public void InitializeSpi_Mode3_KeepsClockIdleHigh()
    {
        var config = new SpiConfiguration { ClockHz = 6_000_000, Mode = SpiMode.Mode3 };
        var bytes = new MpsseCommandBuilder().InitializeSpi(config).Build();
        // value = SCK high (0x01) | CS deasserted (0x08) = 0x09
        Assert.Equal(new byte[] { 0x8A, 0x86, 0x09, 0x00, 0x85, 0x80, 0x09, 0x0B }, bytes);
    }

    [Fact]
    public void SpiMode_EdgeMapping_MatchesAN108()
    {
        // Modes 0/3 → 0x31 (sample +ve, change -ve); modes 1/2 → 0x34 (sample -ve, change +ve).
        Assert.True(new SpiConfiguration { Mode = SpiMode.Mode0 }.SampleOnRisingEdge);
        Assert.True(new SpiConfiguration { Mode = SpiMode.Mode3 }.SampleOnRisingEdge);
        Assert.False(new SpiConfiguration { Mode = SpiMode.Mode1 }.SampleOnRisingEdge);
        Assert.False(new SpiConfiguration { Mode = SpiMode.Mode2 }.SampleOnRisingEdge);

        Assert.False(new SpiConfiguration { Mode = SpiMode.Mode0 }.ClockIdlesHigh);
        Assert.False(new SpiConfiguration { Mode = SpiMode.Mode1 }.ClockIdlesHigh);
        Assert.True(new SpiConfiguration { Mode = SpiMode.Mode2 }.ClockIdlesHigh);
        Assert.True(new SpiConfiguration { Mode = SpiMode.Mode3 }.ClockIdlesHigh);
    }
}