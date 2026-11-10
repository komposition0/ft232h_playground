using FT232H.Core.Gpio;

namespace FT232H.Core.Hardware;

/// <summary>Snapshot of one GPIO line.</summary>
public sealed record GpioPinState(
    GpioPinId Pin,
    GpioDirection Direction,
    bool Value,
    PinFunction Function,
    bool IsReservedBySpi);

/// <summary>Result of a GPIO operation.</summary>
public sealed record GpioOperationResult(bool Success, string? Error, Exception? Exception = null)
{
    public static GpioOperationResult Ok() => new(true, null);
    public static GpioOperationResult Fail(string error, Exception? ex = null) => new(false, error, ex);
}

/// <summary>
/// GPIO controller for the FT232H ADBUS and ACBUS lines.
/// </summary>
/// <remarks>
/// Input "polling" here is software polling over USB: the host periodically reads
/// the pin state. It is NOT a hardware interrupt and has host-side latency and
/// jitter in the millisecond range. Software-generated pulses are likewise not
/// microsecond-accurate; see the electrical/timing documentation.
/// </remarks>
public interface IGpioController
{
    /// <summary>Current state of all 16 MPSSE GPIO lines.</summary>
    IReadOnlyList<GpioPinState> GetPinStates();

    /// <summary>Reads all pin values from hardware (host-observed latency).</summary>
    Task<IReadOnlyList<GpioPinState>> ReadAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads a single pin value from hardware.</summary>
    Task<bool> ReadPinAsync(GpioPinId pin, CancellationToken cancellationToken = default);

    /// <summary>Sets the direction of one line. Fails when the line is reserved by SPI.</summary>
    Task<GpioOperationResult> SetDirectionAsync(GpioPinId pin, GpioDirection direction, CancellationToken cancellationToken = default);

    /// <summary>Drives one output line high/low. Fails when the line is reserved by SPI.</summary>
    Task<GpioOperationResult> WritePinAsync(GpioPinId pin, bool value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a pulse on an output line (assert then deassert with a host-side delay).
    /// Timing is software-controlled and NOT microsecond-accurate.
    /// </summary>
    Task<GpioOperationResult> PulseAsync(GpioPinId pin, TimeSpan width, CancellationToken cancellationToken = default);
}