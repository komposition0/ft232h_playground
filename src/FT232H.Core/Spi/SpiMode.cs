namespace FT232H.Core.Spi;

/// <summary>
/// SPI clock polarity/phase modes as defined by the Motorola SPI convention.
/// </summary>
/// <remarks>
/// FT232H MPSSE edge semantics (AN_108):
/// <list type="bullet">
/// <item>Modes 0 and 3 shift data out on the falling edge and sample on the rising
/// edge (MPSSE "in on +ve / out on -ve", opcode 0x31).</item>
/// <item>Modes 1 and 2 shift data out on the rising edge and sample on the falling
/// edge (MPSSE "in on -ve / out on +ve", opcode 0x34).</item>
/// <item>Modes 0/1 idle the clock LOW; modes 2/3 idle the clock HIGH (set via the
/// SCK bit of the ADBUS data byte).</item>
/// </list>
/// Hardware verification of modes 1 and 2 (CPHA = 1) with a logic analyzer is
/// required before these modes are considered validated on real hardware.
/// </remarks>
public enum SpiMode
{
    /// <summary>CPOL = 0, CPHA = 0. Clock idles low; sample on rising edge.</summary>
    Mode0 = 0,

    /// <summary>CPOL = 0, CPHA = 1. Clock idles low; sample on falling edge.</summary>
    Mode1 = 1,

    /// <summary>CPOL = 1, CPHA = 0. Clock idles high; sample on falling edge.</summary>
    Mode2 = 2,

    /// <summary>CPOL = 1, CPHA = 1. Clock idles high; sample on rising edge.</summary>
    Mode3 = 3,
}

/// <summary>Bit shift order for SPI payload bytes.</summary>
public enum BitOrder
{
    /// <summary>Most significant bit first (standard for most SPI peripherals).</summary>
    MsbFirst = 0,

    /// <summary>Least significant bit first. Supported by MPSSE LSB-first shift commands.</summary>
    LsbFirst = 1,
}

/// <summary>Chip-select assertion control strategy.</summary>
public enum ChipSelectMode
{
    /// <summary>
    /// The controller asserts CS before and releases CS after each transfer automatically.
    /// </summary>
    Automatic = 0,

    /// <summary>
    /// The application asserts/releases CS explicitly (e.g. for multi-stage transfers
    /// that must keep CS held continuously). Transfers only run while CS is asserted.
    /// </summary>
    Manual = 1,
}