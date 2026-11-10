using FT232H.Core.Diagnostics;
using FT232H.Core.Gpio;
using FT232H.Core.Hardware;

namespace FT232H.Simulation;

/// <summary>
/// Simulated GPIO controller with a 16-line virtual pin model. Mirrors the pin
/// reservation rules of the real hardware controller (SPI lines are protected),
/// so unsafe flows are caught in simulation exactly as they are on hardware.
/// </summary>
public sealed class SimulatedGpioController : IGpioController
{
    private readonly SimulatedFtdiDevice _device;
    private readonly SimulationOptions _options;
    private readonly InMemoryTransactionLogger _logger;
    private readonly DeviceArbiterLike _arbiter;
    private readonly object _lock = new();
    private readonly GpioPinState[] _states;

    public SimulatedGpioController(
        SimulatedFtdiDevice device,
        SimulationOptions options,
        InMemoryTransactionLogger logger,
        DeviceArbiterLike arbiter)
    {
        _device = device;
        _options = options;
        _logger = logger;
        _arbiter = arbiter;

        _states = new GpioPinState[16];
        for (int i = 0; i < 16; i++)
        {
            var pin = (GpioPinId)i;
            _states[i] = new GpioPinState(
                pin,
                GpioDirection.Input,
                Value: false,
                Function: ResolveFunction(pin),
                IsReservedBySpi: IsReserved(pin));
        }
    }

    /// <summary>Active SPI configuration used for pin-reservation checks.</summary>
    public FT232H.Core.Spi.SpiConfiguration ActiveSpiConfiguration { get; set; } = new();

    public IReadOnlyList<GpioPinState> GetPinStates()
    {
        lock (_lock) return _states.ToArray();
    }

    public async Task<IReadOnlyList<GpioPinState>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        _device.CheckConnected();
        using var lease = await _arbiter.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await SimulatedLatency.DelayAsync(_options, cancellationToken).ConfigureAwait(false);
        lock (_lock) return _states.ToArray();
    }

    public async Task<bool> ReadPinAsync(GpioPinId pin, CancellationToken cancellationToken = default)
    {
        var states = await ReadAllAsync(cancellationToken).ConfigureAwait(false);
        return states.First(s => s.Pin == pin).Value;
    }

    public async Task<GpioOperationResult> SetDirectionAsync(GpioPinId pin, GpioDirection direction, CancellationToken cancellationToken = default)
    {
        _device.CheckConnected();
        var guard = Core.Validation.ConfigurationValidator.ValidateGpioAccess(
            pin, PinFunction.Gpio, ActiveSpiConfiguration);
        if (!guard.IsValid)
            return GpioOperationResult.Fail(string.Join("; ", guard.Errors));

        using var lease = await _arbiter.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await SimulatedLatency.DelayAsync(_options, cancellationToken).ConfigureAwait(false);

        lock (_lock)
        {
            int index = (int)pin;
            _states[index] = _states[index] with { Direction = direction };
        }
        _logger.Record(new TransactionRecord
        {
            Sequence = _logger.NextSequence(),
            TimestampUtc = DateTimeOffset.UtcNow,
            Kind = TransactionKind.Gpio,
            Operation = $"SetDirection {pin}={direction}",
            Success = true,
        });
        return GpioOperationResult.Ok();
    }

    public async Task<GpioOperationResult> WritePinAsync(GpioPinId pin, bool value, CancellationToken cancellationToken = default)
    {
        _device.CheckConnected();
        var guard = Core.Validation.ConfigurationValidator.ValidateGpioAccess(
            pin, PinFunction.Gpio, ActiveSpiConfiguration);
        if (!guard.IsValid)
            return GpioOperationResult.Fail(string.Join("; ", guard.Errors));

        using var lease = await _arbiter.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await SimulatedLatency.DelayAsync(_options, cancellationToken).ConfigureAwait(false);

        lock (_lock)
        {
            int index = (int)pin;
            _states[index] = _states[index] with { Direction = GpioDirection.Output, Value = value };
        }
        _logger.Record(new TransactionRecord
        {
            Sequence = _logger.NextSequence(),
            TimestampUtc = DateTimeOffset.UtcNow,
            Kind = TransactionKind.Gpio,
            Operation = $"Write {pin}={(value ? "HIGH" : "LOW")}",
            Success = true,
        });
        return GpioOperationResult.Ok();
    }

    public async Task<GpioOperationResult> PulseAsync(GpioPinId pin, TimeSpan width, CancellationToken cancellationToken = default)
    {
        // Software-timed pulse — same caveat as hardware: NOT microsecond-accurate.
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

    private static PinFunction ResolveFunction(GpioPinId pin) => pin switch
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