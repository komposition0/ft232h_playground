namespace FT232H.Core.Spi;

/// <summary>Kind of SPI transfer to perform.</summary>
public enum SpiTransferKind
{
    /// <summary>Clock out payload bytes only; no bytes are captured.</summary>
    WriteOnly = 0,

    /// <summary>Clock in bytes only; MOSI is driven with 0x00 filler.</summary>
    ReadOnly = 1,

    /// <summary>
    /// Simultaneous full-duplex exchange: one byte is clocked in for every byte
    /// clocked out (MPSSE in-and-out commands).
    /// </summary>
    WriteRead = 2,

    /// <summary>
    /// Staged: clock out payload bytes, then clock in <see cref="SpiTransferRequest.ReadLength"/>
    /// bytes, all within one continuous chip-select assertion.
    /// </summary>
    WriteThenRead = 3,
}

/// <summary>A single SPI transfer request.</summary>
public sealed class SpiTransferRequest
{
    /// <summary>Bytes to transmit. May be empty for read-only transfers.</summary>
    public byte[] WriteData { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Number of bytes to receive. For <see cref="SpiTransferKind.WriteRead"/> this is
    /// implicitly <c>WriteData.Length</c> (full duplex). For read-only and staged
    /// transfers it is the explicit receive length.
    /// </summary>
    public int ReadLength { get; set; }

    public SpiTransferKind Kind { get; set; } = SpiTransferKind.WriteRead;

    /// <summary>When true, chip select stays asserted after the transfer (manual mode or staged sequences).</summary>
    public bool HoldChipSelect { get; set; }
}

/// <summary>Result of an SPI transfer.</summary>
public sealed class SpiTransferResult
{
    public required bool Success { get; init; }
    public byte[] ReceivedData { get; init; } = Array.Empty<byte>();
    public TimeSpan HostObservedDuration { get; init; }
    public double ActualClockHz { get; init; }
    public string? Error { get; init; }
    public Exception? Exception { get; init; }
}