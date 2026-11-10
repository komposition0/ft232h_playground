using FT232H.Core.Devices;

namespace FT232H.Core.Hardware;

/// <summary>
/// Enumerates and connects FTDI devices. One instance per backend (real D2XX or
/// simulation). All methods are host-side asynchronous; they do not imply any
/// hardware-level timing guarantees.
/// </summary>
public interface IFtdiDeviceManager
{
    /// <summary>Enumerates all attached FTDI devices, flagging duplicate serial numbers.</summary>
    Task<IReadOnlyList<FtdiDeviceInfo>> EnumerateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a device. Throws <see cref="FtdiException"/> when the selector is missing
    /// or ambiguous (e.g. duplicate serial numbers without a location discriminator).
    /// </summary>
    Task<IFtdiDevice> ConnectAsync(DeviceSelector selector, CancellationToken cancellationToken = default);

    /// <summary>Driver/dependency diagnostics (driver version, wrapper status, etc.).</summary>
    Task<IReadOnlyList<DiagnosticLine>> GetDiagnosticsAsync(CancellationToken cancellationToken = default);
}

/// <summary>One human-readable diagnostic line for the diagnostics panel.</summary>
public sealed record DiagnosticLine(string Name, string Value, bool IsOk, string? Detail = null);

/// <summary>
/// An open connection to one physical FTDI device. This object is the single owner
/// and arbiter of the device: SPI and GPIO access both go through it, so no two
/// subsystems can issue conflicting MPSSE commands concurrently.
/// </summary>
public interface IFtdiDevice : IAsyncDisposable
{
    FtdiDeviceInfo Info { get; }
    bool IsConnected { get; }
    ISpiController Spi { get; }
    IGpioController Gpio { get; }

    /// <summary>Re-initializes the MPSSE engine after an error without re-enumerating.</summary>
    Task ReinitializeAsync(CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);
}