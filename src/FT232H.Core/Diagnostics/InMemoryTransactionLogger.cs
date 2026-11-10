using FT232H.Core.Diagnostics;

namespace FT232H.Core.Diagnostics;

/// <summary>
/// Thread-safe in-memory transaction logger with bounded history.
/// Used by both the simulation and hardware backends.
/// </summary>
public sealed class InMemoryTransactionLogger : ITransactionLogger
{
    private readonly object _lock = new();
    private readonly List<TransactionRecord> _records = new();
    private readonly int _maxRecords;
    private long _sequence;

    public InMemoryTransactionLogger(int maxRecords = 10_000)
    {
        if (maxRecords <= 0) throw new ArgumentOutOfRangeException(nameof(maxRecords));
        _maxRecords = maxRecords;
    }

    public event EventHandler<TransactionRecord>? TransactionLogged;
    public event EventHandler<DiagnosticEvent>? DiagnosticLogged;

    public void Record(TransactionRecord record)
    {
        lock (_lock)
        {
            _records.Add(record);
            if (_records.Count > _maxRecords)
                _records.RemoveRange(0, _records.Count - _maxRecords);
        }
        TransactionLogged?.Invoke(this, record);
    }

    public void Log(DiagnosticSeverity severity, string source, string message)
    {
        var evt = new DiagnosticEvent
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            Severity = severity,
            Source = source,
            Message = message,
        };
        DiagnosticLogged?.Invoke(this, evt);
    }

    /// <summary>Allocates the next monotonic sequence number.</summary>
    public long NextSequence() => Interlocked.Increment(ref _sequence);

    public IReadOnlyList<TransactionRecord> Snapshot()
    {
        lock (_lock) return _records.ToArray();
    }

    public void Clear()
    {
        lock (_lock) _records.Clear();
    }
}