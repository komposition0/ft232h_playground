using System.Diagnostics;
using FT232H.Core.Diagnostics;
using FT232H.Core.Hardware;
using FT232H.Core.Spi;
using FT232H.Hardware.Mpsse;

namespace FT232H.Hardware;

/// <summary>
/// MPSSE-backed SPI controller. Builds AN_108 command streams and executes them
/// through the owning <see cref="FtdiDevice"/> under its arbiter lease.
/// </summary>
public sealed class MpsseSpiController : ISpiController
{
    private readonly FtdiDevice _device;
    private SpiConfiguration _configuration = new();
    private bool _csAsserted;

    public MpsseSpiController(FtdiDevice device) => _device = device;

    public SpiConfiguration Configuration => _configuration.Clone();
    public double ActualClockHz => _configuration.ActualClockHz;

    public async Task ApplyConfigurationAsync(SpiConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var validation = Core.Validation.ConfigurationValidator.Validate(configuration);
        if (!validation.IsValid)
            throw new FtdiException(FtdiErrorCode.ConfigurationInvalid,
                "Invalid SPI configuration: " + string.Join("; ", validation.Errors));

        using var lease = await _device.Arbiter.AcquireAsync(cancellationToken).ConfigureAwait(false);
        _configuration = configuration.Clone();

        // Keep GPIO pin-reservation in sync with the active CS assignment.
        ((MpsseGpioController)_device.Gpio).ActiveSpiConfiguration = _configuration.Clone();

        var builder = new MpsseCommandBuilder().InitializeSpi(_configuration);
        _device.WriteRaw(builder.Build());
        _csAsserted = false;
    }

    public async Task SetChipSelectAsync(bool asserted, CancellationToken cancellationToken = default)
    {
        using var lease = await _device.Arbiter.AcquireAsync(cancellationToken).ConfigureAwait(false);
        var builder = new MpsseCommandBuilder()
            .SetLowByte(GetLowValue(asserted), SpiPinMap.DefaultLowDirection);
        _device.WriteRaw(builder.Build());
        _csAsserted = asserted;
    }

    private DataOutEdge OutEdge => _configuration.SampleOnRisingEdge
        ? DataOutEdge.Negative   // modes 0/3: data changes on falling edge
        : DataOutEdge.Positive;  // modes 1/2: data changes on rising edge

    private DataInEdge InEdge => _configuration.SampleOnRisingEdge
        ? DataInEdge.Positive    // modes 0/3: sample on rising edge
        : DataInEdge.Negative;   // modes 1/2: sample on falling edge

    private DataInOutEdge InOutEdge => _configuration.SampleOnRisingEdge
        ? DataInOutEdge.InOnPositiveOutOnNegative  // modes 0/3 → 0x31
        : DataInOutEdge.InOnNegativeOutOnPositive; // modes 1/2 → 0x34

    private byte GetLowValue(bool csAsserted)
    {
        byte sck = _configuration.ClockIdlesHigh ? SpiPinMap.SckBit : (byte)0;
        byte cs = csAsserted ? (byte)0 : SpiPinMap.CsMask; // active-low CS
        return (byte)(sck | cs);
    }

    public async Task<SpiTransferResult> TransferAsync(SpiTransferRequest request, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var lease = await _device.Arbiter.AcquireAsync(cancellationToken).ConfigureAwait(false);

            var builder = new MpsseCommandBuilder();
            bool autoCs = _configuration.ChipSelectMode == ChipSelectMode.Automatic;
            bool releaseCs = autoCs && !request.HoldChipSelect;

            if (autoCs && !_csAsserted)
            {
                builder.SetLowByte(GetLowValue(csAsserted: true), SpiPinMap.DefaultLowDirection);
                _csAsserted = true;
            }

            byte[] received;
            switch (request.Kind)
            {
                case SpiTransferKind.WriteOnly:
                    builder.ClockDataOut(OutEdge, _configuration.BitOrder, request.WriteData);
                    received = Array.Empty<byte>();
                    break;

                case SpiTransferKind.ReadOnly:
                    builder.ClockDataIn(InEdge, _configuration.BitOrder, request.ReadLength);
                    received = new byte[request.ReadLength];
                    break;

                case SpiTransferKind.WriteRead:
                    builder.ClockDataInOut(InOutEdge, _configuration.BitOrder, request.WriteData);
                    received = new byte[request.WriteData.Length];
                    break;

                case SpiTransferKind.WriteThenRead:
                    builder.ClockDataOut(OutEdge, _configuration.BitOrder, request.WriteData);
                    builder.ClockDataIn(InEdge, _configuration.BitOrder, request.ReadLength);
                    received = new byte[request.ReadLength];
                    break;

                default:
                    throw new FtdiException(FtdiErrorCode.ConfigurationInvalid,
                        $"Unknown transfer kind {request.Kind}");
            }

            builder.SendImmediate();

            if (releaseCs)
            {
                builder.SetLowByte(GetLowValue(csAsserted: false), SpiPinMap.DefaultLowDirection);
                _csAsserted = false;
            }

            _device.WriteRaw(builder.Build());

            if (received.Length > 0)
                received = _device.ReadRaw(received.Length);

            stopwatch.Stop();
            _device.Logger.Record(new TransactionRecord
            {
                Sequence = _device.Logger.NextSequence(),
                TimestampUtc = DateTimeOffset.UtcNow,
                Kind = TransactionKind.Spi,
                Operation = request.Kind.ToString(),
                Written = request.WriteData,
                Read = received,
                HostObservedDuration = stopwatch.Elapsed,
                Success = true,
            });

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
            _device.Logger.Record(new TransactionRecord
            {
                Sequence = _device.Logger.NextSequence(),
                TimestampUtc = DateTimeOffset.UtcNow,
                Kind = TransactionKind.Spi,
                Operation = request.Kind.ToString(),
                Written = request.WriteData,
                HostObservedDuration = stopwatch.Elapsed,
                Success = false,
                Error = ex.Message,
            });
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
}