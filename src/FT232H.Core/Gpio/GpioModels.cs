namespace FT232H.Core.Gpio;

/// <summary>The two 8-bit GPIO ports available in FT232H MPSSE mode.</summary>
public enum GpioPort
{
    /// <summary>ADBUS0..ADBUS7 (low byte). ADBUS0-3 are used by the default SPI wiring.</summary>
    AdBus = 0,

    /// <summary>ACBUS0..ACBUS7 (high byte).</summary>
    AcBus = 1,
}

/// <summary>
/// Individual FT232H MPSSE GPIO lines, distinguishing ADBUS from ACBUS.
/// </summary>
/// <remarks>
/// On the Adafruit FT232H breakout the default SPI wiring is:
/// ADBUS0 = SCK, ADBUS1 = MOSI (SDO), ADBUS2 = MISO (SDI), ADBUS3 = CS (GPIOL0).
/// Direction and value handling uses two separate bytes per port
/// (SetDataBitsLowbyte 0x80 for ADBUS, SetDataBitsHighbyte 0x82 for ACBUS).
/// </remarks>
public enum GpioPinId
{
    AdBus0 = 0,
    AdBus1 = 1,
    AdBus2 = 2,
    AdBus3 = 3,
    AdBus4 = 4,
    AdBus5 = 5,
    AdBus6 = 6,
    AdBus7 = 7,
    AcBus0 = 8,
    AcBus1 = 9,
    AcBus2 = 10,
    AcBus3 = 11,
    AcBus4 = 12,
    AcBus5 = 13,
    AcBus6 = 14,
    AcBus7 = 15,
}

/// <summary>Direction of a GPIO line.</summary>
public enum GpioDirection
{
    /// <summary>Input (high impedance). Reading returns the external pin state.</summary>
    Input = 0,

    /// <summary>Output. The pin drives to the value written by software.</summary>
    Output = 1,
}

/// <summary>Function currently assigned to a pin.</summary>
public enum PinFunction
{
    /// <summary>Free for GPIO use.</summary>
    Gpio = 0,

    /// <summary>Reserved as the SPI clock line (ADBUS0 by convention).</summary>
    SpiClock = 1,

    /// <summary>Reserved as the SPI controller-out line (ADBUS1 by convention).</summary>
    SpiMosi = 2,

    /// <summary>Reserved as the SPI controller-in line (ADBUS2 by convention).</summary>
    SpiMiso = 3,

    /// <summary>Reserved as an SPI chip-select line.</summary>
    SpiChipSelect = 4,
}