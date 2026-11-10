using FT232H.Core.Diagnostics;
using FT232H.Core.Spi;

namespace FT232H.Core.Diagnostics;

/// <summary>
/// Logs communication transactions and diagnostic events. Implementations must be
/// thread-safe; hardware paths call into this from worker threads.
/// </summary>
public interface ITransactionLogger
{
    void Record(TransactionRecord record);
    void Log(DiagnosticSeverity severity, string source, string message);

    /// <summary>Raised (on the recording thread) for each recorded transaction.</summary>
    event EventHandler<TransactionRecord>? TransactionLogged;

    /// <summary>Raised (on the recording thread) for each diagnostic event.</summary>
    event EventHandler<DiagnosticEvent>? DiagnosticLogged;

    IReadOnlyList<TransactionRecord> Snapshot();
    void Clear();
}