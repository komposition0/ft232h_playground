using FT232H.Core.Gpio;
using FT232H.Core.Hardware;
using FT232H.Core.Spi;

namespace FT232H.Core.Automation;

/// <summary>Kind of action in a test sequence.</summary>
public enum TestActionKind
{
    SpiWrite = 0,
    SpiRead = 1,
    SpiWriteRead = 2,
    GpioSet = 3,
    GpioRead = 4,
    Delay = 5,
    Repeat = 6,
    CompareBytes = 7,
    AssertGpio = 8,
    ConditionalBranch = 9,
    LogMessage = 10,
    Stop = 11,
}

/// <summary>Outcome of one executed step.</summary>
public enum StepOutcome
{
    Passed = 0,
    Failed = 1,
    Skipped = 2,
    Stopped = 3,
    Error = 4,
}

/// <summary>
/// One action in a test sequence. Serialized as JSON; field availability depends
/// on <see cref="Kind"/>.
/// </summary>
public sealed class TestSequenceStep
{
    public TestActionKind Kind { get; set; }

    // SPI actions
    public byte[]? WriteData { get; set; }
    public int ReadLength { get; set; }

    // GPIO actions
    public GpioPinId Pin { get; set; }
    public bool PinValue { get; set; }

    // Delay
    public int DelayMilliseconds { get; set; }

    // Repeat / ConditionalBranch: nested steps
    public List<TestSequenceStep>? Steps { get; set; }
    public int RepeatCount { get; set; } = 1;

    // CompareBytes / AssertGpio
    public byte[]? ExpectedData { get; set; }

    // ConditionalBranch: pin to test; branches when value equals PinValue
    // LogMessage
    public string? Message { get; set; }

    // Stop
    public string? StopReason { get; set; }

    public override string ToString() => Kind switch
    {
        TestActionKind.SpiWrite => $"SPI write {WriteData?.Length ?? 0} B",
        TestActionKind.SpiRead => $"SPI read {ReadLength} B",
        TestActionKind.SpiWriteRead => $"SPI write/read {WriteData?.Length ?? 0} B",
        TestActionKind.GpioSet => $"GPIO {Pin} = {(PinValue ? "HIGH" : "LOW")}",
        TestActionKind.GpioRead => $"GPIO read {Pin}",
        TestActionKind.Delay => $"Delay {DelayMilliseconds} ms",
        TestActionKind.Repeat => $"Repeat {RepeatCount}x ({Steps?.Count ?? 0} steps)",
        TestActionKind.CompareBytes => $"Compare {ExpectedData?.Length ?? 0} B",
        TestActionKind.AssertGpio => $"Assert {Pin} == {(PinValue ? "HIGH" : "LOW")}",
        TestActionKind.ConditionalBranch => $"If {Pin} == {(PinValue ? "HIGH" : "LOW")} ({Steps?.Count ?? 0} steps)",
        TestActionKind.LogMessage => $"Log: {Message}",
        TestActionKind.Stop => $"Stop: {StopReason}",
        _ => Kind.ToString(),
    };
}

/// <summary>Root of a serializable test sequence.</summary>
public sealed class TestSequence
{
    public string Name { get; set; } = "Untitled sequence";
    public string? Description { get; set; }
    public List<TestSequenceStep> Steps { get; set; } = new();
}

/// <summary>Result of executing one step.</summary>
public sealed record TestStepResult(
    int Index,
    string Description,
    StepOutcome Outcome,
    string? Detail,
    TimeSpan HostDuration,
    byte[]? ReceivedData = null);

/// <summary>Result of executing a full sequence.</summary>
public sealed class TestSequenceResult
{
    public required bool Success { get; init; }
    public required IReadOnlyList<TestStepResult> Steps { get; init; }
    public required TimeSpan HostDuration { get; init; }
    public string? Error { get; init; }
    public int PassedCount => Steps.Count(s => s.Outcome == StepOutcome.Passed);
    public int FailedCount => Steps.Count(s => s.Outcome is StepOutcome.Failed or StepOutcome.Error);
}

/// <summary>Progress report emitted while a sequence runs.</summary>
public sealed record TestSequenceProgress(int CompletedSteps, int TotalSteps, string CurrentDescription);

/// <summary>
/// Runs <see cref="TestSequence"/> definitions against a device. Host-side
/// asynchronous; step delays are host timers, not hardware-timed.
/// </summary>
public interface ITestSequenceRunner
{
    Task<TestSequenceResult> RunAsync(
        TestSequence sequence,
        ISpiController spi,
        IGpioController gpio,
        IProgress<TestSequenceProgress>? progress = null,
        CancellationToken cancellationToken = default);
}