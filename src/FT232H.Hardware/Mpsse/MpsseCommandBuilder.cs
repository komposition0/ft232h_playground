using FT232H.Core.Spi;

namespace FT232H.Hardware.Mpsse;

/// <summary>Edge semantics for write-only shift commands.</summary>
/// <remarks>
/// Careful: for OUT commands the name refers to the edge on which the DATA
/// CHANGES; for IN commands it refers to the edge on which the data is SAMPLED.
/// This asymmetry is per AN_108 and is a classic source of SPI mode bugs.
/// </remarks>
public enum DataOutEdge
{
    /// <summary>Data changes on the rising clock edge (opcode 0x10/0x18).</summary>
    Positive = 0,

    /// <summary>Data changes on the falling clock edge (opcode 0x11/0x19).</summary>
    Negative = 1,
}

/// <summary>Edge semantics for read-only shift commands (edge on which data is sampled).</summary>
public enum DataInEdge
{
    /// <summary>Data sampled on the rising clock edge (opcode 0x20/0x28).</summary>
    Positive = 0,

    /// <summary>Data sampled on the falling clock edge (opcode 0x24/0x2C).</summary>
    Negative = 1,
}

/// <summary>Edge pairing for full-duplex in/out shift commands.</summary>
public enum DataInOutEdge
{
    /// <summary>Sample on +ve edge, change data on -ve edge (opcode 0x31/0x39) — SPI modes 0 and 3.</summary>
    InOnPositiveOutOnNegative = 0,

    /// <summary>Sample on -ve edge, change data on +ve edge (opcode 0x34/0x3C) — SPI modes 1 and 2.</summary>
    InOnNegativeOutOnPositive = 1,
}

/// <summary>
/// Pure MPSSE command-sequence builder. Produces byte arrays exactly as specified
/// by AN_108; no I/O is performed here, which makes every command shape unit-testable
/// against golden vectors.
/// </summary>
public sealed class MpsseCommandBuilder
{
    private readonly List<byte> _bytes = new();

    /// <summary>Maximum payload bytes per single MPSSE shift command (16-bit length field, length-1 encoded).</summary>
    public const int MaxShiftLength = 0x10000;

    public MpsseCommandBuilder Append(byte opcode)
    {
        _bytes.Add(opcode);
        return this;
    }

    public MpsseCommandBuilder AppendRaw(params byte[] bytes)
    {
        _bytes.AddRange(bytes);
        return this;
    }

    /// <summary>[0x80, value, direction] — drives ADBUS0-7. Direction bit 1 = output.</summary>
    public MpsseCommandBuilder SetLowByte(byte value, byte direction) =>
        AppendRaw(MpsseOpcodes.SetDataBitsLowByte, value, direction);

    /// <summary>[0x82, value, direction] — drives ACBUS0-7.</summary>
    public MpsseCommandBuilder SetHighByte(byte value, byte direction) =>
        AppendRaw(MpsseOpcodes.SetDataBitsHighByte, value, direction);

    /// <summary>[0x81] — request ADBUS state; 1 byte response follows.</summary>
    public MpsseCommandBuilder GetLowByte() => Append(MpsseOpcodes.GetDataBitsLowByte);

    /// <summary>[0x83] — request ACBUS state; 1 byte response follows.</summary>
    public MpsseCommandBuilder GetHighByte() => Append(MpsseOpcodes.GetDataBitsHighByte);

    /// <summary>[0x86, lo, hi] — TCK = 60 MHz / (1 + divisor) with divide-by-5 disabled.</summary>
    public MpsseCommandBuilder SetClockDivisor(ushort divisor) =>
        AppendRaw(MpsseOpcodes.SetClockDivisor, (byte)(divisor & 0xFF), (byte)(divisor >> 8));

    /// <summary>[0x8A] — disable the divide-by-5 prescaler (60 MHz base clock).</summary>
    public MpsseCommandBuilder DisableClockDivideBy5() => Append(MpsseOpcodes.DisableClockDivideBy5);

    /// <summary>[0x84] — loopback on (TDI→TDO internally).</summary>
    public MpsseCommandBuilder EnableLoopback() => Append(MpsseOpcodes.EnableLoopback);

    /// <summary>[0x85] — loopback off.</summary>
    public MpsseCommandBuilder DisableLoopback() => Append(MpsseOpcodes.DisableLoopback);

    /// <summary>[0x87] — flush device FIFO to host immediately.</summary>
    public MpsseCommandBuilder SendImmediate() => Append(MpsseOpcodes.SendImmediate);

    /// <summary>MPSSE synchronization helper: sending an invalid opcode makes the device echo [0xFA, opcode].</summary>
    public MpsseCommandBuilder SyncBadCommand(byte badCommand = 0xAA) => AppendRaw(badCommand);

    /// <summary>
    /// Clocks out <paramref name="data"/> bytes. Payloads longer than
    /// <see cref="MaxShiftLength"/> are split into multiple commands
    /// (the caller's chip-select assertion stays continuous across them).
    /// </summary>
    public MpsseCommandBuilder ClockDataOut(DataOutEdge edge, BitOrder order, ReadOnlySpan<byte> data)
    {
        byte opcode = (edge, order) switch
        {
            (DataOutEdge.Positive, BitOrder.MsbFirst) => MpsseOpcodes.BytesOutPosEdgeMsb,
            (DataOutEdge.Negative, BitOrder.MsbFirst) => MpsseOpcodes.BytesOutNegEdgeMsb,
            (DataOutEdge.Positive, BitOrder.LsbFirst) => MpsseOpcodes.BytesOutPosEdgeLsb,
            (DataOutEdge.Negative, BitOrder.LsbFirst) => MpsseOpcodes.BytesOutNegEdgeLsb,
            _ => throw new ArgumentOutOfRangeException(nameof(edge)),
        };
        return AppendShiftCommand(opcode, data);
    }

    /// <summary>Clocks in <paramref name="length"/> bytes (MOSI is not driven).</summary>
    public MpsseCommandBuilder ClockDataIn(DataInEdge edge, BitOrder order, int length)
    {
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
        byte opcode = (edge, order) switch
        {
            (DataInEdge.Positive, BitOrder.MsbFirst) => MpsseOpcodes.BytesInPosEdgeMsb,
            (DataInEdge.Negative, BitOrder.MsbFirst) => MpsseOpcodes.BytesInNegEdgeMsb,
            (DataInEdge.Positive, BitOrder.LsbFirst) => MpsseOpcodes.BytesInPosEdgeLsb,
            (DataInEdge.Negative, BitOrder.LsbFirst) => MpsseOpcodes.BytesInNegEdgeLsb,
            _ => throw new ArgumentOutOfRangeException(nameof(edge)),
        };

        int remaining = length;
        while (remaining > 0)
        {
            int chunk = Math.Min(remaining, MaxShiftLength);
            _bytes.Add(opcode);
            AppendLength(chunk);
            remaining -= chunk;
        }
        return this;
    }

    /// <summary>
    /// Full-duplex exchange: one byte clocked in for every byte clocked out,
    /// in a single continuous shift command chain under one CS assertion.
    /// </summary>
    public MpsseCommandBuilder ClockDataInOut(DataInOutEdge edge, BitOrder order, ReadOnlySpan<byte> data)
    {
        byte opcode = (edge, order) switch
        {
            (DataInOutEdge.InOnPositiveOutOnNegative, BitOrder.MsbFirst) => MpsseOpcodes.BytesInOutInPosOutNegMsb,
            (DataInOutEdge.InOnNegativeOutOnPositive, BitOrder.MsbFirst) => MpsseOpcodes.BytesInOutInNegOutPosMsb,
            (DataInOutEdge.InOnPositiveOutOnNegative, BitOrder.LsbFirst) => MpsseOpcodes.BytesInOutInPosOutNegLsb,
            (DataInOutEdge.InOnNegativeOutOnPositive, BitOrder.LsbFirst) => MpsseOpcodes.BytesInOutInNegOutPosLsb,
            _ => throw new ArgumentOutOfRangeException(nameof(edge)),
        };
        return AppendShiftCommand(opcode, data);
    }

    /// <summary>Builds the byte-array command stream.</summary>
    public byte[] Build() => _bytes.ToArray();

    public int Length => _bytes.Count;

    public void Clear() => _bytes.Clear();

    /// <summary>Encodes a full SPI-mode preamble (clock divisor, idle level, no loopback).</summary>
    public MpsseCommandBuilder InitializeSpi(SpiConfiguration config)
    {
        var (divisor, _) = SpiClock.ComputeDivisor(config.ClockHz);

        DisableClockDivideBy5();
        SetClockDivisor(divisor);
        DisableLoopback();

        // SCK idle level is bit 0 of ADBUS; assert configured idle state while
        // keeping CS deasserted (bit 3 high = inactive for active-low CS).
        byte sckIdle = config.ClockIdlesHigh ? (byte)0x01 : (byte)0x00;
        SetLowByte((byte)(sckIdle | SpiPinMap.CsMask), SpiPinMap.DefaultLowDirection);
        return this;
    }

    private MpsseCommandBuilder AppendShiftCommand(byte opcode, ReadOnlySpan<byte> data)
    {
        int offset = 0;
        while (offset < data.Length)
        {
            int chunk = Math.Min(data.Length - offset, MaxShiftLength);
            _bytes.Add(opcode);
            AppendLength(chunk);
            for (int i = 0; i < chunk; i++)
                _bytes.Add(data[offset + i]);
            offset += chunk;
        }
        return this;
    }

    /// <summary>Appends the MPSSE 16-bit (count - 1) little-endian length field.</summary>
    private void AppendLength(int count)
    {
        int encoded = count - 1;
        _bytes.Add((byte)(encoded & 0xFF));
        _bytes.Add((byte)((encoded >> 8) & 0xFF));
    }

    public override string ToString()
    {
        var sb = new System.Text.StringBuilder(_bytes.Count * 3);
        foreach (byte b in _bytes)
            sb.Append(b.ToString("X2")).Append(' ');
        return sb.ToString().TrimEnd();
    }
}

/// <summary>
/// ADBUS pin roles for the standard FT232H SPI wiring
/// (SCK = ADBUS0, MOSI = ADBUS1, MISO = ADBUS2, CS = ADBUS3/GPIOL0).
/// </summary>
public static class SpiPinMap
{
    public const byte SckBit = 0x01;
    public const byte MosiBit = 0x02;
    public const byte MisoBit = 0x04;
    public const byte CsBit = 0x08;
    public const byte CsMask = 0x08;

    /// <summary>Default direction byte: SCK, MOSI, CS are outputs; MISO (bit 2) is input.</summary>
    public const byte DefaultLowDirection = 0x0B;
}
