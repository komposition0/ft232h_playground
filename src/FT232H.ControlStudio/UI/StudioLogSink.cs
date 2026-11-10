using FT232H.Core.Diagnostics;

namespace FT232H.ControlStudio.UI;

/// <summary>
/// Thread-safe sink bridging background log producers to the UI log pane.
/// The UI polls <see cref="Drain"/> on a timer; producers never block on the UI.
/// </summary>
public sealed class StudioLogSink
{
    public static readonly StudioLogSink Instance = new();

    private readonly object _lock = new();
    private readonly Queue<DiagnosticEvent> _pending = new();
    private readonly int _maxBuffer = 2000;

    public void Append(DiagnosticSeverity severity, string source, string message, string? detail = null)
    {
        var evt = new DiagnosticEvent
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            Severity = severity,
            Source = source,
            Message = detail is null ? message : $"{message} | {detail}",
        };

        lock (_lock)
        {
            _pending.Enqueue(evt);
            while (_pending.Count > _maxBuffer)
                _pending.Dequeue();
        }
    }

    /// <summary>Removes and returns all pending events.</summary>
    public IReadOnlyList<DiagnosticEvent> Drain()
    {
        lock (_lock)
        {
            var drained = _pending.ToArray();
            _pending.Clear();
            return drained;
        }
    }
}