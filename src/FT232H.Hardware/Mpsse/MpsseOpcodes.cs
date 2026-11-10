namespace FT232H.Hardware.Mpsse;

/// <summary>
/// MPSSE command opcodes per FTDI AN_108 ("Command Processor for MPSSE and MCU
/// Host Bus Emulation Modes", FT_000109 v1.5). Values cross-checked against
/// ftdi-mpsse (Rust) and MPSSELight (C#), both of which encode AN_108 directly.
/// </summary>
public static class MpsseOpcodes
{
    // --- Data shifting: bytes out (MSB) ---
    /// <summary>Clock data bytes out, data changes on +ve edge, MSB first.</summary>
    public const byte BytesOutPosEdgeMsb = 0x10;

    /// <summary>Clock data bytes out, data changes on -ve edge, MSB first.</summary>
    public const byte BytesOutNegEdgeMsb = 0x11;

    // --- Data shifting: bits out (MSB) ---
    public const byte BitsOutPosEdgeMsb = 0x12;
    public const byte BitsOutNegEdgeMsb = 0x13;

    // --- Data shifting: bytes in (MSB) ---
    /// <summary>Clock data bytes in, sampled on +ve edge, MSB first.</summary>
    public const byte BytesInPosEdgeMsb = 0x20;

    /// <summary>Clock data bytes in, sampled on -ve edge, MSB first.</summary>
    public const byte BytesInNegEdgeMsb = 0x24;

    // --- Data shifting: bits in (MSB) ---
    public const byte BitsInPosEdgeMsb = 0x22;
    public const byte BitsInNegEdgeMsb = 0x26;

    // --- Data shifting: bytes in and out (MSB) ---
    /// <summary>Full duplex bytes: in on +ve edge, out on -ve edge, MSB first (SPI modes 0/3).</summary>
    public const byte BytesInOutInPosOutNegMsb = 0x31;

    /// <summary>Full duplex bytes: in on -ve edge, out on +ve edge, MSB first (SPI modes 1/2).</summary>
    public const byte BytesInOutInNegOutPosMsb = 0x34;

    // --- Data shifting: bits in and out (MSB) ---
    public const byte BitsInOutInPosOutNegMsb = 0x33;
    public const byte BitsInOutInNegOutPosMsb = 0x36;

    // --- LSB-first variants ---
    public const byte BytesOutPosEdgeLsb = 0x18;
    public const byte BytesOutNegEdgeLsb = 0x19;
    public const byte BitsOutPosEdgeLsb = 0x1A;
    public const byte BitsOutNegEdgeLsb = 0x1B;
    public const byte BytesInPosEdgeLsb = 0x28;
    public const byte BytesInNegEdgeLsb = 0x2C;
    public const byte BitsInPosEdgeLsb = 0x2A;
    public const byte BitsInNegEdgeLsb = 0x2E;
    public const byte BytesInOutInPosOutNegLsb = 0x39;
    public const byte BytesInOutInNegOutPosLsb = 0x3C;
    public const byte BitsInOutInPosOutNegLsb = 0x3B;
    public const byte BitsInOutInNegOutPosLsb = 0x3E;

    // --- GPIO / control ---
    /// <summary>Set Data bits LowByte (ADBUS0-7): [0x80, value, direction].</summary>
    public const byte SetDataBitsLowByte = 0x80;

    /// <summary>Get Data bits LowByte (ADBUS0-7): [0x81] → 1 byte response.</summary>
    public const byte GetDataBitsLowByte = 0x81;

    /// <summary>Set Data bits HighByte (ACBUS0-7): [0x82, value, direction].</summary>
    public const byte SetDataBitsHighByte = 0x82;

    /// <summary>Get Data bits HighByte (ACBUS0-7): [0x83] → 1 byte response.</summary>
    public const byte GetDataBitsHighByte = 0x83;

    /// <summary>Enable TDI/TDO loopback: [0x84].</summary>
    public const byte EnableLoopback = 0x84;

    /// <summary>Disable TDI/TDO loopback: [0x85].</summary>
    public const byte DisableLoopback = 0x85;

    /// <summary>Set TCK divisor: [0x86, divisorLow, divisorHigh].</summary>
    public const byte SetClockDivisor = 0x86;

    /// <summary>Send immediate: [0x87] — flush device buffer back to host.</summary>
    public const byte SendImmediate = 0x87;

    /// <summary>Wait on GPIOL1 (ADBUS1) high: [0x88].</summary>
    public const byte WaitOnIoHigh = 0x88;

    /// <summary>Wait on GPIOL1 (ADBUS1) low: [0x89].</summary>
    public const byte WaitOnIoLow = 0x89;

    /// <summary>Disable clock divide-by-5 (FT232H/FT2232H/FT4232H): [0x8A]. Base clock becomes 60 MHz.</summary>
    public const byte DisableClockDivideBy5 = 0x8A;

    /// <summary>Enable clock divide-by-5: [0x8B].</summary>
    public const byte EnableClockDivideBy5 = 0x8B;

    /// <summary>Enable 3-phase data clocking (I2C-style): [0x8C].</summary>
    public const byte Enable3PhaseClocking = 0x8C;

    /// <summary>Disable 3-phase data clocking: [0x8D].</summary>
    public const byte Disable3PhaseClocking = 0x8D;
}