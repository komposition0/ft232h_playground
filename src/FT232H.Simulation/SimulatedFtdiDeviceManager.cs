using FT232H.Core.Devices;
using FT232H.Core.Hardware;

namespace FT232H.Simulation;

/// <summary>Configurable behavior of the simulated backend.</summary>
public sealed class SimulationOptions
{
    /// <summary>Virtual device count exposed by the simulated manager.</summary>
    public int VirtualDeviceCount { get; set; } = 2;

    /// <summary>
    /// When true, one pair of virtual devices reports the SAME serial number so the
    /// duplicate-serial selection flow can be exercised without hardware.
    /// </summary>
    public bool IncludeDuplicateSerials { get; set; } = true;

    /// <summary>Simulated per-operation latency in milliseconds (host-side).</summary>
    public int LatencyMilliseconds { get; set; } = 5;

    /// <summary>When true, SPI writes echo back their payload (loopback cable).</summary>
    public bool SpiLoopback { get; set; } = true;

    /// <summary>Fixed byte returned for SPI reads when loopback is off.</summary>
    public byte SpiReadFillByte { get; set; } = 0xA5;
}

/// <summary>
/// Simulated FT232H device manager. Implements the same contracts as the real
/// D2XX backend so the GUI, automation and tests run without hardware.
/// </summary>
public sealed class SimulatedFtdiDeviceManager : IFtdiDeviceManager
{
    private readonly SimulationOptions _options;
    private readonly List<SimulatedFtdiDevice> _openDevices = new();

    public SimulatedFtdiDeviceManager(SimulationOptions? options = null)
    {
        _options = options ?? new SimulationOptions();
    }

    public SimulationOptions Options => _options;

    public Task<IReadOnlyList<FtdiDeviceInfo>> EnumerateAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<FtdiDeviceInfo>();
        for (int i = 0; i < _options.VirtualDeviceCount; i++)
        {
            // Devices 0/1 share a serial when duplicate simulation is enabled.
            bool duplicate = _options.IncludeDuplicateSerials && i < 2;
            string serial = duplicate ? "FTSIM0001" : $"FTSIM{i + 1:0000}";
            list.Add(new FtdiDeviceInfo
            {
                Index = i,
                SerialNumber = serial,
                Description = $"Simulated FT232H #{i + 1}",
                DeviceType = FtdiDeviceType.Ft232H,
                LocationId = $"SIM{i:0000}",
                HasDuplicateSerial = duplicate,
            });
        }
        return Task.FromResult<IReadOnlyList<FtdiDeviceInfo>>(list);
    }

    public async Task<IFtdiDevice> ConnectAsync(DeviceSelector selector, CancellationToken cancellationToken = default)
    {
        var devices = await EnumerateAsync(cancellationToken).ConfigureAwait(false);

        FtdiDeviceInfo target;
        if (selector.Index is int index)
        {
            target = devices.FirstOrDefault(d => d.Index == index)
                ?? throw new FtdiException(FtdiErrorCode.DeviceNotFound, $"No simulated device at index {index}.");
        }
        else if (selector.LocationId is not null)
        {
            target = devices.FirstOrDefault(d =>
                string.Equals(d.LocationId, selector.LocationId, StringComparison.OrdinalIgnoreCase))
                ?? throw new FtdiException(FtdiErrorCode.DeviceNotFound,
                    $"No simulated device at location {selector.LocationId}.");
        }
        else if (selector.SerialNumber is not null)
        {
            var matches = devices.Where(d =>
                string.Equals(d.SerialNumber, selector.SerialNumber, StringComparison.Ordinal)).ToList();
            if (matches.Count == 1)
                target = matches[0];
            else if (matches.Count > 1)
                throw new FtdiException(FtdiErrorCode.AmbiguousDevice,
                    $"Serial '{selector.SerialNumber}' matches {matches.Count} simulated devices (duplicate serials). " +
                    "Select by USB location instead.");
            else
                throw new FtdiException(FtdiErrorCode.DeviceNotFound,
                    $"No simulated device with serial '{selector.SerialNumber}'.");
        }
        else
        {
            throw new FtdiException(FtdiErrorCode.DeviceNotFound, "Device selector is empty.");
        }

        var device = new SimulatedFtdiDevice(target, _options);
        _openDevices.Add(device);
        return device;
    }

    public Task<IReadOnlyList<DiagnosticLine>> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<DiagnosticLine> lines = new[]
        {
            new DiagnosticLine("Backend", "Simulation", true, "No physical hardware required."),
            new DiagnosticLine("Virtual devices", _options.VirtualDeviceCount.ToString(), true),
            new DiagnosticLine("Duplicate serial scenario", _options.IncludeDuplicateSerials ? "enabled" : "disabled", true),
            new DiagnosticLine("SPI loopback", _options.SpiLoopback ? "enabled" : "disabled", true),
            new DiagnosticLine("Simulated latency", $"{_options.LatencyMilliseconds} ms", true),
        };
        return Task.FromResult(lines);
    }
}