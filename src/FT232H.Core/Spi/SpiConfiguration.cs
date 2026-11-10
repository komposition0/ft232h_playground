using FT232H.Core.Gpio;

namespace FT232H.Core.Spi;

/// <summary>
/// SPI bus configuration for one FT232H MPSSE channel.
/// </summary>
public sealed class SpiConfiguration : IEquatable<SpiConfiguration>
{
    public static readonly IReadOnlyList<GpioPinId> DefaultReservedPins = Array.AsReadOnly(new[]
    {
        GpioPinId.AdBus0, GpioPinId.AdBus1, GpioPinId.AdBus2,
    });

    public SpiMode Mode { get; set; } = SpiMode.Mode0;

    /// <summary>Requested clock frequency in Hz (clamped to hardware range at use time).</summary>
    public double ClockHz { get; set; } = 1_000_000.0;

    public BitOrder BitOrder { get; set; } = BitOrder.MsbFirst;

    /// <summary>Chip-select GPIO line. ADBUS3 (GPIOL0) is the conventional default.</summary>
    public GpioPinId ChipSelectPin { get; set; } = GpioPinId.AdBus3;

    public ChipSelectMode ChipSelectMode { get; set; } = ChipSelectMode.Automatic;

    /// <summary>True when CS (and SCK idle level) is held between staged transfers.</summary>
    public bool HoldChipSelectBetweenStages { get; set; }

    /// <summary>Actual clock frequency the configured request maps to.</summary>
    public double ActualClockHz
    {
        get
        {
            var (_, actual) = SpiClock.ComputeDivisor(ClockHz);
            return actual;
        }
    }

    /// <summary>True when the clock idles HIGH (SPI modes 2 and 3).</summary>
    public bool ClockIdlesHigh => Mode is SpiMode.Mode2 or SpiMode.Mode3;

    /// <summary>True when data is sampled on the rising edge (SPI modes 0 and 3).</summary>
    public bool SampleOnRisingEdge => Mode is SpiMode.Mode0 or SpiMode.Mode3;

    public SpiConfiguration Clone() => new()
    {
        Mode = Mode,
        ClockHz = ClockHz,
        BitOrder = BitOrder,
        ChipSelectPin = ChipSelectPin,
        ChipSelectMode = ChipSelectMode,
        HoldChipSelectBetweenStages = HoldChipSelectBetweenStages,
    };

    public bool Equals(SpiConfiguration? other) =>
        other is not null &&
        Mode == other.Mode &&
        ClockHz.Equals(other.ClockHz) &&
        BitOrder == other.BitOrder &&
        ChipSelectPin == other.ChipSelectPin &&
        ChipSelectMode == other.ChipSelectMode &&
        HoldChipSelectBetweenStages == other.HoldChipSelectBetweenStages;

    public override bool Equals(object? obj) => Equals(obj as SpiConfiguration);

    public override int GetHashCode() => HashCode.Combine(Mode, ClockHz, BitOrder, ChipSelectPin, ChipSelectMode);
}