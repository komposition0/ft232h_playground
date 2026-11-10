using System.Diagnostics;
using FT232H.Core.Automation;
using FT232H.Core.Hardware;
using FT232H.Core.Spi;

namespace FT232H.Automation;

/// <summary>
/// Executes test sequences against any ISpiController/IGpioController pair (real
/// hardware or simulation). Steps run strictly in order; Repeat and ConditionalBranch
/// nest. Delays use host-side timers (Task.Delay) — they are not hardware-timed.
/// Cancellation stops the run at the next step boundary.
/// </summary>
public sealed class TestSequenceRunner : ITestSequenceRunner
{
    private const int MaxNestingDepth = 16;

    private sealed class RunState
    {
        public int Index;
        public bool Stopped;
        public readonly List<TestStepResult> Results = new();
    }

    public async Task<TestSequenceResult> RunAsync(
        TestSequence sequence,
        ISpiController spi,
        IGpioController gpio,
        IProgress<TestSequenceProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var state = new RunState();
        var total = Stopwatch.StartNew();
        string? fatal = null;

        try
        {
            await ExecuteStepsAsync(sequence.Steps, spi, gpio, state, progress,
                sequence.Steps.Count, depth: 0, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            fatal = "Sequence cancelled by user.";
        }
        catch (FtdiException ex)
        {
            fatal = $"Hardware error: {ex.Message}";
        }

        total.Stop();
        bool success = fatal is null &&
            state.Results.All(r => r.Outcome is StepOutcome.Passed or StepOutcome.Skipped);
        return new TestSequenceResult
        {
            Success = success,
            Steps = state.Results,
            HostDuration = total.Elapsed,
            Error = fatal,
        };
    }

    private async Task ExecuteStepsAsync(
        List<TestSequenceStep> steps,
        ISpiController spi,
        IGpioController gpio,
        RunState state,
        IProgress<TestSequenceProgress>? progress,
        int totalSteps,
        int depth,
        CancellationToken cancellationToken)
    {
        if (depth > MaxNestingDepth)
            throw new FtdiException(FtdiErrorCode.ConfigurationInvalid,
                $"Sequence nesting exceeds {MaxNestingDepth} levels.");

        foreach (var step in steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (state.Stopped) return;

            var sw = Stopwatch.StartNew();
            var result = await ExecuteStepAsync(step, spi, gpio, state, progress,
                totalSteps, depth, cancellationToken).ConfigureAwait(false);
            sw.Stop();

            state.Results.Add(result with { Index = state.Index, HostDuration = sw.Elapsed });
            state.Index++;
            progress?.Report(new TestSequenceProgress(state.Index, totalSteps, step.ToString()));
        }
    }

    private async Task<TestStepResult> ExecuteStepAsync(
        TestSequenceStep step,
        ISpiController spi,
        IGpioController gpio,
        RunState state,
        IProgress<TestSequenceProgress>? progress,
        int totalSteps,
        int depth,
        CancellationToken cancellationToken)
    {
        switch (step.Kind)
        {
            case TestActionKind.SpiWrite:
            {
                var r = await spi.TransferAsync(new SpiTransferRequest
                {
                    Kind = SpiTransferKind.WriteOnly,
                    WriteData = step.WriteData ?? Array.Empty<byte>(),
                }, cancellationToken).ConfigureAwait(false);
                return new TestStepResult(0, step.ToString(),
                    r.Success ? StepOutcome.Passed : StepOutcome.Failed, r.Error, TimeSpan.Zero);
            }

            case TestActionKind.SpiRead:
            {
                var r = await spi.TransferAsync(new SpiTransferRequest
                {
                    Kind = SpiTransferKind.ReadOnly,
                    ReadLength = step.ReadLength,
                }, cancellationToken).ConfigureAwait(false);
                return new TestStepResult(0, step.ToString(),
                    r.Success ? StepOutcome.Passed : StepOutcome.Failed,
                    r.Error, TimeSpan.Zero, r.ReceivedData);
            }

            case TestActionKind.SpiWriteRead:
            {
                var r = await spi.TransferAsync(new SpiTransferRequest
                {
                    Kind = SpiTransferKind.WriteRead,
                    WriteData = step.WriteData ?? Array.Empty<byte>(),
                }, cancellationToken).ConfigureAwait(false);
                return new TestStepResult(0, step.ToString(),
                    r.Success ? StepOutcome.Passed : StepOutcome.Failed,
                    r.Error, TimeSpan.Zero, r.ReceivedData);
            }

            case TestActionKind.GpioSet:
            {
                var r = await gpio.WritePinAsync(step.Pin, step.PinValue, cancellationToken).ConfigureAwait(false);
                return new TestStepResult(0, step.ToString(),
                    r.Success ? StepOutcome.Passed : StepOutcome.Failed, r.Error, TimeSpan.Zero);
            }

            case TestActionKind.GpioRead:
            {
                bool value = await gpio.ReadPinAsync(step.Pin, cancellationToken).ConfigureAwait(false);
                return new TestStepResult(0, $"{step} -> {(value ? "HIGH" : "LOW")}",
                    StepOutcome.Passed, null, TimeSpan.Zero);
            }

            case TestActionKind.Delay:
                await Task.Delay(TimeSpan.FromMilliseconds(step.DelayMilliseconds), cancellationToken)
                    .ConfigureAwait(false);
                return new TestStepResult(0, step.ToString(), StepOutcome.Passed, null, TimeSpan.Zero);
        }

        return await ExecuteControlFlowAsync(step, spi, gpio, state, progress,
            totalSteps, depth, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TestStepResult> ExecuteControlFlowAsync(
        TestSequenceStep step,
        ISpiController spi,
        IGpioController gpio,
        RunState state,
        IProgress<TestSequenceProgress>? progress,
        int totalSteps,
        int depth,
        CancellationToken cancellationToken)
    {
        switch (step.Kind)
        {
            case TestActionKind.Repeat:
            {
                for (int i = 0; i < step.RepeatCount; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (state.Stopped) break;
                    await ExecuteStepsAsync(step.Steps ?? new List<TestSequenceStep>(), spi, gpio,
                        state, progress, totalSteps, depth + 1, cancellationToken).ConfigureAwait(false);
                }
                return new TestStepResult(0, step.ToString(),
                    state.Stopped ? StepOutcome.Stopped : StepOutcome.Passed, null, TimeSpan.Zero);
            }

            case TestActionKind.CompareBytes:
            {
                // Compares against the most recent SPI read result in this run.
                byte[]? last = state.Results.LastOrDefault(r => r.ReceivedData is not null)?.ReceivedData;
                bool match = last is not null &&
                    step.ExpectedData is not null &&
                    last.AsSpan().SequenceEqual(step.ExpectedData);
                return new TestStepResult(0, step.ToString(),
                    match ? StepOutcome.Passed : StepOutcome.Failed,
                    match ? null
                        : $"Expected {Core.Text.HexCodec.ToHex(step.ExpectedData ?? Array.Empty<byte>())}, " +
                          $"got {Core.Text.HexCodec.ToHex(last ?? Array.Empty<byte>())}",
                    TimeSpan.Zero);
            }

            case TestActionKind.AssertGpio:
            {
                bool value = await gpio.ReadPinAsync(step.Pin, cancellationToken).ConfigureAwait(false);
                bool pass = value == step.PinValue;
                return new TestStepResult(0, step.ToString(),
                    pass ? StepOutcome.Passed : StepOutcome.Failed,
                    pass ? null : $"Pin was {(value ? "HIGH" : "LOW")}", TimeSpan.Zero);
            }

            case TestActionKind.ConditionalBranch:
            {
                bool value = await gpio.ReadPinAsync(step.Pin, cancellationToken).ConfigureAwait(false);
                if (value == step.PinValue && step.Steps is not null)
                {
                    await ExecuteStepsAsync(step.Steps, spi, gpio, state, progress,
                        totalSteps, depth + 1, cancellationToken).ConfigureAwait(false);
                }
                return new TestStepResult(0,
                    $"{step} -> branch {(value == step.PinValue ? "taken" : "not taken")}",
                    StepOutcome.Passed, null, TimeSpan.Zero);
            }

            case TestActionKind.LogMessage:
                return new TestStepResult(0, step.ToString(), StepOutcome.Passed, step.Message, TimeSpan.Zero);

            case TestActionKind.Stop:
                state.Stopped = true;
                return new TestStepResult(0, step.ToString(), StepOutcome.Stopped, step.StopReason, TimeSpan.Zero);

            default:
                return new TestStepResult(0, step.ToString(), StepOutcome.Error,
                    $"Unknown action kind {step.Kind}", TimeSpan.Zero);
        }
    }
}