using FT232H.Core.Gpio;
using FT232H.Core.Spi;

namespace FT232H.Core.Validation;

/// <summary>Result of a validation pass.</summary>
public sealed class ValidationResult
{
    private readonly List<string> _errors = new();
    private readonly List<string> _warnings = new();

    public bool IsValid => _errors.Count == 0;
    public IReadOnlyList<string> Errors => _errors;
    public IReadOnlyList<string> Warnings => _warnings;

    public void AddError(string message) => _errors.Add(message);
    public void AddWarning(string message) => _warnings.Add(message);

    public static ValidationResult Success() => new();
}

/// <summary>Validates SPI configuration and GPIO pin-assignment conflicts.</summary>
public static class ConfigurationValidator
{
    /// <summary>Validates an SPI configuration against FT232H constraints.</summary>
    public static ValidationResult Validate(SpiConfiguration config)
    {
        var result = new ValidationResult();

        if (config.ClockHz <= 0 || double.IsNaN(config.ClockHz) || double.IsInfinity(config.ClockHz))
            result.AddError("Clock frequency must be a positive finite number.");
        else if (config.ClockHz > SpiClock.MaxClockHz)
            result.AddError($"Clock frequency {config.ClockHz:0.##} Hz exceeds the FT232H MPSSE maximum of {SpiClock.MaxClockHz:0.##} Hz (30 MHz).");
        else if (config.ClockHz < SpiClock.MinClockHz)
            result.AddError($"Clock frequency {config.ClockHz:0.##} Hz is below the FT232H MPSSE minimum of {SpiClock.MinClockHz:0.##} Hz.");

        if (config.ChipSelectPin is GpioPinId.AdBus0 or GpioPinId.AdBus1 or GpioPinId.AdBus2)
            result.AddError($"Chip select cannot use {config.ChipSelectPin}: that line is reserved for SPI clock/data.");

        if (!Enum.IsDefined(config.Mode))
            result.AddError($"Unknown SPI mode value {(int)config.Mode}.");
        if (!Enum.IsDefined(config.BitOrder))
            result.AddError($"Unknown bit order value {(int)config.BitOrder}.");
        if (!Enum.IsDefined(config.ChipSelectMode))
            result.AddError($"Unknown chip-select mode value {(int)config.ChipSelectMode}.");

        return result;
    }

    /// <summary>
    /// Validates GPIO operations against pins currently reserved by an SPI
    /// configuration, so the user cannot accidentally drive SPI lines.
    /// </summary>
    public static ValidationResult ValidateGpioAccess(
        GpioPinId pin,
        PinFunction function,
        SpiConfiguration? activeSpiConfiguration)
    {
        var result = new ValidationResult();

        if (activeSpiConfiguration is null)
            return result;

        if (function == PinFunction.SpiClock || function == PinFunction.SpiMosi ||
            function == PinFunction.SpiMiso || function == PinFunction.SpiChipSelect)
        {
            result.AddError($"{pin} is assigned to {function} and cannot be used as GPIO while SPI is active.");
            return result;
        }

        if (pin == GpioPinId.AdBus0)
            result.AddError("ADBUS0 is the SPI clock line (SCK) and cannot be used as GPIO while SPI is active.");
        else if (pin == GpioPinId.AdBus1)
            result.AddError("ADBUS1 is the SPI MOSI line and cannot be used as GPIO while SPI is active.");
        else if (pin == GpioPinId.AdBus2)
            result.AddError("ADBUS2 is the SPI MISO line and cannot be used as GPIO while SPI is active.");
        else if (pin == activeSpiConfiguration.ChipSelectPin)
            result.AddError($"{pin} is the active SPI chip-select line and cannot be used as GPIO while SPI is active.");

        return result;
    }

    /// <summary>Pin-function map used for conflict detection in the Pin Mapping Designer.</summary>
    public static ValidationResult ValidatePinMap(IReadOnlyDictionary<GpioPinId, PinFunction> pinMap)
    {
        var result = new ValidationResult();

        int clocks = 0, mosi = 0, miso = 0, cs = 0;
        foreach (var function in pinMap.Values)
        {
            switch (function)
            {
                case PinFunction.SpiClock: clocks++; break;
                case PinFunction.SpiMosi: mosi++; break;
                case PinFunction.SpiMiso: miso++; break;
                case PinFunction.SpiChipSelect: cs++; break;
            }
        }

        if (clocks > 1) result.AddError("More than one pin is assigned as SPI clock.");
        if (mosi > 1) result.AddError("More than one pin is assigned as SPI MOSI.");
        if (miso > 1) result.AddError("More than one pin is assigned as SPI MISO.");
        if (cs > 1) result.AddError("More than one pin is assigned as SPI chip select.");
        if (clocks == 1 && (mosi == 0 || miso == 0))
            result.AddWarning("SPI clock is assigned but MOSI and/or MISO are not; SPI transfers will fail.");

        return result;
    }
}