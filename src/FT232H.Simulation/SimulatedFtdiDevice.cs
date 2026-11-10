using System.Diagnostics;
using FT232H.Core.Devices;
using FT232H.Core.Diagnostics;
using FT232H.Core.Hardware;

namespace FT232H.Simulation;

/// <summary>
/// One simulated FT232H device with SPI loopback and a virtual GPIO state model.
/// Behaves like the real <c>FtdiDevice</c> contract, including pin reservations.
/// </summary>
public sealed class SimulatedFtdiDevice : IFtdiDevice
{
    private readonly SimulationOptions _options;
    private readonly DeviceArbiterLike _arbiter = new();
    private bool _disposed;

    public SimulatedFtdiDevice(FtdiDeviceInfo info, SimulationOptions options)
    {
        Info = info;
        _options = options;
        Logger = new InMemoryTransactionLogger();
        Spi = new SimulatedSpiController(this, options, Logger, _arbiter);
        Gpio = new SimulatedGpioController(this, options, Logger, _arbiter);
    }

    public FtdiDeviceInfo Info { get; }
    public bool IsConnected => !_disposed;
    public ISpiController Spi { get; }
    public IGpioController Gpio { get; }
    public InMemoryTransactionLogger Logger { get; }
    public SimulationOptions Options => _options;
    public DeviceArbiterLike Arbiter => _arbiter;

    /// <summary>Virtual pin values for the GPIO simulator (bit per line, low byte then high byte).</summary>
    internal byte[] VirtualPinValues { get; } = new byte[2];

    public Task ReinitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new FtdiException(FtdiErrorCode.Disposed, "Simulated device is disposed.");
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _disposed = true;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }

    internal void CheckConnected()
    {
        if (_disposed) throw new FtdiException(FtdiErrorCode.NotConnected, "Simulated device is not connected.");
    }
}

/// <summary>
/// Semaphore-based single-owner lease, mirroring <c>FT232H.Hardware.DeviceArbiter</c>
/// so the simulation exercises the same serialization behavior.
/// </summary>
public sealed class DeviceArbiterLike
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            throw new FtdiException(FtdiErrorCode.DeviceBusy, "Timed out waiting for exclusive device access.", ex);
        }
        return new Lease(_gate);
    }

    private sealed class Lease : IDisposable
    {
        private SemaphoreSlim? _gate;
        public Lease(SemaphoreSlim gate) => _gate = gate;
        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
}

/// <summary>Shared simulated-latency helper.</summary>
internal static class SimulatedLatency
{
    public static Task DelayAsync(SimulationOptions options, CancellationToken ct) =>
        options.LatencyMilliseconds > 0
            ? Task.Delay(options.LatencyMilliseconds, ct)
            : Task.CompletedTask;
}

/// <summary>Small helper to produce host-observed timing records.</summary>
internal static class SimRecord
{
    public static TransactionRecord Spi(InMemoryTransactionLogger logger, string op, byte[] tx, byte[] rx, Stopwatch sw, bool ok, string? err) =>
        new()
        {
            Sequence = logger.NextSequence(),
            TimestampUtc = DateTimeOffset.UtcNow,
            Kind = TransactionKind.Spi,
            Operation = op,
            Written = tx,
            Read = rx,
            HostObservedDuration = sw.Elapsed,
            Success = ok,
            Error = err,
        };
}