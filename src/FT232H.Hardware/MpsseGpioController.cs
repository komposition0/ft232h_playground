using FT232H.Core.Diagnostics;
using FT232H.Core.Gpio;
using FT232H.Core.Hardware;
using FT232H.Hardware.Mpsse;

namespace FT232H.Hardware;

/// <summary>
/// MPSSE-backed GPIO controller for ADBUS0-7 (low byte, 0x80/0x81) and
/// ACBUS0-7 (high byte, 0x82/0x83). Direction bits: 1 = output, 0 = input.
/// Pins reserved by the active SPI configuration are protected from writes.
/// </summary>
public sealed class MpsseGpioController : IGpioController
{
    private readonly FtdiDevice _device;
    private readonly object _stateLock = new();

    // Cached hardware state (mirrors what was last written).
    private byte _lowValue, _lowDirection = 0x00;
    private byte _highValue, _highDirection = 0x00;

    public MpsseGpioController(FtdiDevice device) => _device = device;

    /// <summary>Active SPI configuration used for pin-reservation checks (set by the device owner).</summary>
    public FT232H.Core.Spi.SpiConfiguration ActiveSpiConfiguration { get; set; } = new();

    public IReadOnlyList<GpioPinState> GetPinStates()
    {
        lock (_stateLock) return BuildStates(lowRead: _lowValue, highRead: _highValue);
    }

    public async Task<IReadOnlyList<GpioPinState>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        using var lease = await _device.Arbiter.AcquireAsync(cancellationToken).ConfigureAwait(false);
        var builder = new MpsseCommandBuilder()
            .GetLowByte()
            .GetHighByte()
            .SendImmediate();
        _device.WriteRaw(builder.Build());
        var data = _device.ReadRaw(2);

        lock (_stateLock)
        {
            _lowValue = data[0];
            _highValue = data[1];
            return BuildStates(data[0], data[1]);
        }
    }

    public async Task<bool> ReadPinAsync(GpioPinId pin, CancellationToken cancellationToken = default)
    {
        var states = await ReadAllAsync(cancellationToken).ConfigureAwait(false);
        return states.First(s => s.Pin == pin).Value;
    }

    public async Task<GpioOperationResult> SetDirectionAsync(GpioPinId pin, GpioDirection direction, CancellationToken cancellationToken = default)
    {
        var guard = Core.Validation.ConfigurationValidator.ValidateGpioAccess(
            pin, PinFunction.Gpio, ActiveSpiConfiguration);
        if (!guard.IsValid)
            return GpioOperationResult.Fail(string.Join("; ", guard.Errors));

        try
        {
            using var lease = await _device.Arbiter.AcquireAsync(cancellationToken).ConfigureAwait(false);
            lock (_stateLock)
            {
                if (pin <= GpioPinId.AdBus7)
                {
                    byte mask = (byte)(1 << (int)pin);
                    _lowDirection = direction == GpioDirection.Output
                        ? (byte)(_lowDirection | mask)
                        : (byte)(_lowDirection & ~mask);
                }
                else
                {
                    byte mask = (byte)(1 << ((int)pin - 8));
                    _highDirection = direction == GpioDirection.Output
                        ? (byte)(_highDirection | mask)
                        : (byte)(_highDirection & ~mask);
                }

                var builder = BuildStateBuilder();
                _device.WriteRaw(builder.Build());
            }
            _device.Logger.Record(new TransactionRecord
            {
                Sequence = _device.Logger.NextSequence(),
                TimestampUtc = DateTimeOffset.UtcNow,
                Kind = TransactionKind.Gpio,
                Operation = $"SetDirection {pin}={direction}",
                Success = true,
            });
            return GpioOperationResult.Ok();
        }
        catch (FtdiException ex)
        {
            return GpioOperationResult.Fail(ex.Message, ex);
        }
    }

    public async Task<GpioOperationResult> WritePinAsync(GpioPinId pin, bool value, CancellationToken cancellationToken = default)
    {
        var guard = Core.Validation.ConfigurationValidator.ValidateGpioAccess(
            pin, PinFunction.Gpio, ActiveSpiConfiguration);
        if (!guard.IsValid)
            return GpioOperationResult.Fail(string.Join("; ", guard.Errors));

        try
        {
            using var lease = await _device.Arbiter.AcquireAsync(cancellationToken).ConfigureAwait(false);
            lock (_stateLock)
            {
                if (pin <= GpioPinId.AdBus7)
                {
                    byte mask = (byte)(1 << (int)pin);
                    _lowValue = value ? (byte)(_lowValue | mask) : (byte)(_lowValue & ~mask);
                    _lowDirection |= mask; // driving a value implies output
                }
                else
                {
                    byte mask = (byte)(1 << ((int)pin - 8));
                    _highValue = value ? (byte)(_highValue | mask) : (byte)(_highValue & ~mask);
                    _highDirection |= mask;
                }

                var builder = BuildStateBuilder();
                _device.WriteRaw(builder.Build());
            }
            _device.Logger.Record(new TransactionRecord
            {
                Sequence = _device.Logger.NextSequence(),
                TimestampUtc = DateTimeOffset.UtcNow,
                Kind = TransactionKind.Gpio,
                Operation = $"Write {pin}={(value ? "HIGH" : "LOW")}",
                Success = true,
            });
            return GpioOperationResult.Ok();
        }
        catch (FtdiException ex)
        {
            return GpioOperationResult.Fail(ex.Message, ex);
        }
    }

    public async Task<GpioOperationResult> PulseAsync(GpioPinId pin, TimeSpan width, CancellationToken cancellationToken = default)
    {
        // Software-timed pulse: NOT microsecond-accurate. See docs/ELECTRICAL-LIMITATIONS.md.
        var high = await WritePinAsync(pin, true, cancellationToken).ConfigureAwait(false);
        if (!high.Success) return high;

        try
        {
            await Task.Delay(width, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await WritePinAsync(pin, false, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        return await WritePinAsync(pin, false, cancellationToken).ConfigureAwait(false);
    }

    private MpsseCommandBuilder BuildStateBuilder() =>
        new MpsseCommandBuilder()
            .SetLowByte(_lowValue, _lowDirection)
            .SetHighByte(_highValue, _highDirection);

    private List<GpioPinState> BuildStates(byte lowRead, byte highRead)
    {
        var states = new List<GpioPinState>(16);
        for (int i = 0; i < 8; i++)
        {
            var pin = (GpioPinId)i;
            bool isOutput = (_lowDirection & (1 << i)) != 0;
            bool value = isOutput ? (_lowValue & (1 << i)) != 0 : (lowRead & (1 << i)) != 0;
            states.Add(new GpioPinState(pin,
                isOutput ? GpioDirection.Output : GpioDirection.Input,
                value, ResolveFunction(pin), IsReserved(pin)));
        }
        for (int i = 0; i < 8; i++)
        {
            var pin = (GpioPinId)(i + 8);
            bool isOutput = (_highDirection & (1 << i)) != 0;
            bool value = isOutput ? (_highValue & (1 << i)) != 0 : (highRead & (1 << i)) != 0;
            states.Add(new GpioPinState(pin,
                isOutput ? GpioDirection.Output : GpioDirection.Input,
                value, ResolveFunction(pin), IsReserved(pin)));
        }
        return states;
    }

    private PinFunction ResolveFunction(GpioPinId pin) => pin switch
    {
        GpioPinId.AdBus0 => PinFunction.SpiClock,
        GpioPinId.AdBus1 => PinFunction.SpiMosi,
        GpioPinId.AdBus2 => PinFunction.SpiMiso,
        GpioPinId.AdBus3 => PinFunction.SpiChipSelect,
        _ => PinFunction.Gpio,
    };

    private bool IsReserved(GpioPinId pin) =>
        pin is GpioPinId.AdBus0 or GpioPinId.AdBus1 or GpioPinId.AdBus2 ||
        pin == (ActiveSpiConfiguration?.ChipSelectPin ?? GpioPinId.AdBus3);
}