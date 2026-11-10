using FT232H.Core.Gpio;
using FT232H.Core.Spi;
using FT232H.Core.Validation;
using Xunit;

namespace FT232H.Tests;

public class SpiClockTests
{
    [Fact]
    public void ComputeDivisor_ThirtyMegahertz_IsDivisorOne()
    {
        var (divisor, actual) = SpiClock.ComputeDivisor(30_000_000);
        Assert.Equal(1, divisor);
        Assert.Equal(30_000_000, actual, 3);
    }

    [Fact]
    public void ComputeDivisor_OneMegahertz_IsDivisor59()
    {
        var (divisor, actual) = SpiClock.ComputeDivisor(1_000_000);
        Assert.Equal(59, divisor); // 60 MHz / (1 + 59) = 1 MHz
        Assert.Equal(1_000_000, actual, 3);
    }

    [Fact]
    public void ComputeDivisor_ClampsAboveMaxToThirtyMegahertz()
    {
        var (divisor, actual) = SpiClock.ComputeDivisor(100_000_000);
        Assert.Equal(1, divisor);
        Assert.Equal(30_000_000, actual, 3);
    }

    [Fact]
    public void ComputeDivisor_ClampsBelowMinTo915Hz()
    {
        var (divisor, actual) = SpiClock.ComputeDivisor(1);
        Assert.Equal(0xFFFF, divisor);
        Assert.Equal(SpiClock.MinClockHz, actual, 3);
    }

    [Fact]
    public void ComputeDivisor_IsAlwaysWithinHardwareRange()
    {
        foreach (double f in new[] { 0.5, 915.0, 10_000.0, 12_345.6, 1_000_000.0, 6_000_000.0, 29_999_999.0, 30_000_001.0 })
        {
            var (divisor, actual) = SpiClock.ComputeDivisor(f);
            Assert.InRange(divisor, (ushort)1, (ushort)0xFFFF);
            Assert.InRange(actual, SpiClock.MinClockHz - 0.01, SpiClock.MaxClockHz + 0.01);
        }
    }

    [Fact]
    public void ActualFrequency_MatchesFormula()
    {
        Assert.Equal(60_000_000.0 / 6, SpiClock.ActualFrequency(5), 6);
    }
}

public class ConfigurationValidatorTests
{
    [Fact]
    public void ValidConfiguration_Passes()
    {
        var config = new SpiConfiguration { ClockHz = 1_000_000, ChipSelectPin = GpioPinId.AdBus3 };
        var result = ConfigurationValidator.Validate(config);
        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(30_000_001)]
    public void InvalidClock_Fails(double clockHz)
    {
        var config = new SpiConfiguration { ClockHz = clockHz };
        Assert.False(ConfigurationValidator.Validate(config).IsValid);
    }

    [Theory]
    [InlineData(GpioPinId.AdBus0)]
    [InlineData(GpioPinId.AdBus1)]
    [InlineData(GpioPinId.AdBus2)]
    public void ChipSelectOnReservedPin_Fails(GpioPinId pin)
    {
        var config = new SpiConfiguration { ChipSelectPin = pin };
        Assert.False(ConfigurationValidator.Validate(config).IsValid);
    }

    [Fact]
    public void GpioWriteToSpiClock_Fails()
    {
        var spi = new SpiConfiguration();
        var result = ConfigurationValidator.ValidateGpioAccess(GpioPinId.AdBus0, PinFunction.Gpio, spi);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void GpioWriteToFreePin_Passes()
    {
        var spi = new SpiConfiguration();
        var result = ConfigurationValidator.ValidateGpioAccess(GpioPinId.AdBus5, PinFunction.Gpio, spi);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void GpioWriteToActiveChipSelect_Fails()
    {
        var spi = new SpiConfiguration { ChipSelectPin = GpioPinId.AcBus2 };
        var result = ConfigurationValidator.ValidateGpioAccess(GpioPinId.AcBus2, PinFunction.Gpio, spi);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void PinMap_DetectsDuplicateClock()
    {
        var map = new Dictionary<GpioPinId, PinFunction>
        {
            [GpioPinId.AdBus0] = PinFunction.SpiClock,
            [GpioPinId.AdBus4] = PinFunction.SpiClock,
            [GpioPinId.AdBus1] = PinFunction.SpiMosi,
            [GpioPinId.AdBus2] = PinFunction.SpiMiso,
        };
        Assert.False(ConfigurationValidator.ValidatePinMap(map).IsValid);
    }
}