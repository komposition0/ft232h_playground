using System.Diagnostics;
using FT232H.Core.Diagnostics;
using FT232H.Core.Hardware;
using FT232H.Core.Spi;

namespace FT232H.Simulation;

/// <summary>
/// Simulated SPI controller. In loopback mode the transmitted bytes come back
/// unchanged (as with a loopback cable or a device that echoes). Timing is
/// host-observed with configurable simulated latency.
/// </summary>
public sealed class SimulatedSpiController : ISpiController
{
    private readonly SimulatedFtdiDevice _device;
    private readonly SimulationOptions _options;
    private readonly InMemoryTransactionLogger _logger;
    private readonly DeviceArbiterLike _arbiter;
    private SpiConfiguration _configuration = new();
    private bool _csAsserted;

    public SimulatedSpiController(
        SimulatedFtdiDevice device,
        SimulationOptions options,
        InMemoryTransactionLogger logger,
        DeviceArbiterLike arbiter)
    {
        _device = device;
        _options = options;
        _logger = logger;
        _arbiter = arbiter;
    }

    public SpiConfiguration Configuration => _configuration.Clone();
    public double ActualClockHz => _configuration.ActualClockHz;
    public bool IsChipSelectAsserted => _csAsserted;

    public Task ApplyConfigurationAsync(SpiConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var validation = Core.Validation.ConfigurationValidator.Validate(configuration);
        if (!validation.IsValid)
            throw new FtdiException(FtdiErrorCode.ConfigurationInvalid,
                "Invalid SPI configuration: " + string.Join("; ", validation.Errors));
        _configuration = configuration.Clone();
        _csAsserted = false;

        // Keep GPIO pin-reservation in sync with the active CS assignment.
        ((SimulatedGpioController)_device.Gpio).ActiveSpiConfiguration = _configuration.Clone();

        _logger.Log(DiagnosticSeverity.Info, "SimSPI",
            $"Configuration applied: mode {_configuration.Mode}, {_configuration.ActualClockHz:0.##} Hz, CS {_configuration.ChipSelectPin}");
        return Task.CompletedTask;
    }

    public async Task<SpiTransferResult> TransferAsync(SpiTransferRequest request, CancellationToken cancellationToken = default)
    {
        _device.CheckConnected();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var lease = await _arbiter.AcquireAsync(cancellationToken).ConfigureAwait(false);

            bool autoCs = _configuration.ChipSelectMode == ChipSelectMode.Automatic;
            if (autoCs && !_csAsserted) _csAsserted = true;

            if (_configuration.ChipSelectMode == ChipSelectMode.Manual && !_csAsserted)
                throw new FtdiException(FtdiErrorCode.TransferFailed,
                    "Manual chip-select mode: assert CS before transferring.");

            await SimulatedLatency.DelayAsync(_options, cancellationToken).ConfigureAwait(false);

            // WriteThenRead: real hardware discards MISO bytes during the write
            // stage, so the simulator returns only the read-phase data too.
            byte[] received = request.Kind switch
            {
                SpiTransferKind.WriteOnly => Array.Empty<byte>(),
                SpiTransferKind.ReadOnly => Fill(request.ReadLength),
                SpiTransferKind.WriteRead => Echo(request.WriteData),
                SpiTransferKind.WriteThenRead => Fill(request.ReadLength),
                _ => throw new FtdiException(FtdiErrorCode.ConfigurationInvalid, $"Unknown transfer kind {request.Kind}"),
            };

            bool releaseCs = autoCs && !request.HoldChipSelect;
            if (releaseCs) _csAsserted = false;

            stopwatch.Stop();
            _logger.Record(SimRecord.Spi(_logger, request.Kind.ToString(), request.WriteData, received, stopwatch, true, null));

            return new SpiTransferResult
            {
                Success = true,
                ReceivedData = received,
                HostObservedDuration = stopwatch.Elapsed,
                ActualClockHz = ActualClockHz,
            };
        }
        catch (FtdiException ex)
        {
            stopwatch.Stop();
            _logger.Record(SimRecord.Spi(_logger, request.Kind.ToString(), request.WriteData, Array.Empty<byte>(), stopwatch, false, ex.Message));
            return new SpiTransferResult
            {
                Success = false,
                HostObservedDuration = stopwatch.Elapsed,
                ActualClockHz = ActualClockHz,
                Error = ex.Message,
                Exception = ex,
            };
        }
    }

    public Task SetChipSelectAsync(bool asserted, CancellationToken cancellationToken = default)
    {
        _device.CheckConnected();
        _csAsserted = asserted;
        _logger.Log(DiagnosticSeverity.Trace, "SimSPI", $"Chip select {(asserted ? "asserted" : "released")}");
        return Task.CompletedTask;
    }

    private byte[] Echo(byte[] data) => _options.SpiLoopback ? (byte[])data.Clone() : Fill(data.Length);

    private byte[] Fill(int length)
    {
        var data = new byte[length];
        if (!_options.SpiLoopback)
            Array.Fill(data, _options.SpiReadFillByte);
        return data;
    }
}