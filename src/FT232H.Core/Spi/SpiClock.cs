namespace FT232H.Core.Spi;

/// <summary>
/// SPI clock frequency math for the FT232H MPSSE.
/// </summary>
/// <remarks>
/// AN_108: TCK = 60 MHz / (1 + Divisor) when the divide-by-5 prescaler is
/// disabled (MPSSE opcode 0x8A), which is the configuration this library uses.
/// The 16-bit divisor spans 0..65535; per FTDI documentation the MPSSE output
/// clock is specified up to 30 MHz, so this class clamps divisor to 1..65535.
/// Divisor 0 (60 MHz) is not exposed as a valid configuration because it exceeds
/// the documented MPSSE/SPI clock specification.
/// Reported "actual" frequencies are computed from the divisor formula; they must
/// be confirmed with a measurement instrument on real hardware.
/// </remarks>
public static class SpiClock
{
    /// <summary>FT232H MPSSE base clock when the divide-by-5 prescaler is disabled.</summary>
    public const double BaseClockHz = 60_000_000.0;

    /// <summary>Maximum documented MPSSE clock frequency (divisor = 1).</summary>
    public const double MaxClockHz = 30_000_000.0;

    /// <summary>Minimum clock frequency (divisor = 65535): 60 MHz / 65536 = ~915.5 Hz.</summary>
    public const double MinClockHz = BaseClockHz / 65_536.0;

    /// <summary>
    /// Computes the clock divisor for a requested frequency, clamped to the
    /// hardware range, and the actual frequency the divisor will produce.
    /// </summary>
    public static (ushort Divisor, double ActualHz) ComputeDivisor(double requestedHz)
    {
        if (double.IsNaN(requestedHz) || double.IsInfinity(requestedHz) || requestedHz <= 0)
            requestedHz = MinClockHz;

        double clamped = Math.Clamp(requestedHz, MinClockHz, MaxClockHz);

        // divisor = round(BaseClock / f) - 1
        int raw = (int)Math.Round(BaseClockHz / clamped) - 1;
        ushort divisor = (ushort)Math.Clamp(raw, 1, 0xFFFF);
        double actual = BaseClockHz / (divisor + 1.0);
        return (divisor, actual);
    }

    /// <summary>Computes the actual clock frequency produced by a divisor.</summary>
    public static double ActualFrequency(ushort divisor) => BaseClockHz / (divisor + 1.0);
}