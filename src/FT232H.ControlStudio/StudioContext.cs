using FT232H.Core.Devices;
using FT232H.Core.Diagnostics;
using FT232H.Core.Hardware;

namespace FT232H.ControlStudio;

/// <summary>
/// Application-level session state shared by all tabs: selected backend, connected
/// device, counters and the transaction logger. UI-agnostic so it can be tested.
/// </summary>
public sealed class StudioContext : IAsyncDisposable
{
    private IFtdiDevice? _device;

    public StudioContext(InMemoryTransactionLogger logger, CommunicationCounters counters)
    {
        Logger = logger;
        Counters = counters;
    }

    public InMemoryTransactionLogger Logger { get; }
    public CommunicationCounters Counters { get; }

    /// <summary>Active backend manager (simulation by default; hardware when selected).</summary>
    public IFtdiDeviceManager Backend { get; set; } = new Simulation.SimulatedFtdiDeviceManager();

    public bool UseSimulation => Backend is Simulation.SimulatedFtdiDeviceManager;

    public IFtdiDevice? Device => _device;

    public bool IsConnected => _device?.IsConnected == true;

    public event EventHandler? ConnectionChanged;

    public async Task ConnectAsync(DeviceSelector selector, CancellationToken cancellationToken = default)
    {
        var device = await Backend.ConnectAsync(selector, cancellationToken).ConfigureAwait(false);
        var previous = Interlocked.Exchange(ref _device, device);
        if (previous is not null)
            await previous.DisposeAsync().ConfigureAwait(false);
        Logger.Log(DiagnosticSeverity.Info, "Device", $"Connected: {device.Info}");
        ConnectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        var device = Interlocked.Exchange(ref _device, null);
        if (device is not null)
        {
            await device.DisconnectAsync(cancellationToken).ConfigureAwait(false);
            await device.DisposeAsync().ConfigureAwait(false);
            Logger.Log(DiagnosticSeverity.Info, "Device", "Disconnected.");
        }
        ConnectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void NotifyConnectionChanged() => ConnectionChanged?.Invoke(this, EventArgs.Empty);

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
    }
}