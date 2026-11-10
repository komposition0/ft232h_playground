namespace FT232H.Core.Diagnostics;

/// <summary>Category of a logged transaction.</summary>
public enum TransactionKind
{
    Spi = 0,
    Gpio = 1,
    System = 2,
}

/// <summary>
/// One logged communication transaction with host-observed timing.
/// </summary>
/// <remarks>
/// All timestamps and durations in this record are HOST-OBSERVED values captured
/// with <see cref="System.Diagnostics.Stopwatch"/> on the PC. They include USB
/// transfer latency, driver buffering and scheduling jitter. They are NOT
/// hardware-level signal timing and must never be presented as oscilloscope- or
/// logic-analyzer-grade measurements.
/// </remarks>
public sealed record TransactionRecord
{
    public required long Sequence { get; init; }
    public required DateTimeOffset TimestampUtc { get; init; }
    public required TransactionKind Kind { get; init; }
    public string Operation { get; init; } = string.Empty;
    public byte[] Written { get; init; } = Array.Empty<byte>();
    public byte[] Read { get; init; } = Array.Empty<byte>();
    public TimeSpan HostObservedDuration { get; init; }
    public bool Success { get; init; } = true;
    public string? Error { get; init; }
    public string? Notes { get; init; }
}

/// <summary>
/// Thread-safe live communication counters for the GUI status bar.
/// </summary>
public sealed class CommunicationCounters
{
    private long _bytesWritten;
    private long _bytesRead;
    private long _transactions;
    private long _errors;
    private long _gpioOperations;

    public long BytesWritten => Interlocked.Read(ref _bytesWritten);
    public long BytesRead => Interlocked.Read(ref _bytesRead);
    public long Transactions => Interlocked.Read(ref _transactions);
    public long Errors => Interlocked.Read(ref _errors);
    public long GpioOperations => Interlocked.Read(ref _gpioOperations);

    public void AddWritten(int count) => Interlocked.Add(ref _bytesWritten, count);
    public void AddRead(int count) => Interlocked.Add(ref _bytesRead, count);
    public void AddTransaction() => Interlocked.Increment(ref _transactions);
    public void AddError() => Interlocked.Increment(ref _errors);
    public void AddGpioOperation() => Interlocked.Increment(ref _gpioOperations);

    public void Reset()
    {
        Interlocked.Exchange(ref _bytesWritten, 0);
        Interlocked.Exchange(ref _bytesRead, 0);
        Interlocked.Exchange(ref _transactions, 0);
        Interlocked.Exchange(ref _errors, 0);
        Interlocked.Exchange(ref _gpioOperations, 0);
    }
}

/// <summary>Severity of a diagnostic event.</summary>
public enum DiagnosticSeverity
{
    Trace = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
}

/// <summary>One line in the diagnostic/event log.</summary>
public sealed record DiagnosticEvent
{
    public required DateTimeOffset TimestampUtc { get; init; }
    public required DiagnosticSeverity Severity { get; init; }
    public required string Source { get; init; }
    public required string Message { get; init; }
}