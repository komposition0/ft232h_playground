namespace FT232H.Core.Devices;

/// <summary>Identifies an FTDI device type as reported by D2XX.</summary>
public enum FtdiDeviceType
{
    Unknown = 0,
    Ft232Am = 1,
    Ft232Bm = 2,
    Ft2232C = 3,
    Ft232R = 4,
    Ft2232D = 5,
    Ft232H = 6,
    Ft2232H = 7,
    Ft4232H = 8,
    FtXSeries = 9,
    Other = 99,
}

/// <summary>
/// Immutable snapshot of one enumerated FTDI device.
/// </summary>
public sealed record FtdiDeviceInfo
{
    public required int Index { get; init; }
    public string SerialNumber { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public FtdiDeviceType DeviceType { get; init; }
    public string LocationId { get; init; } = string.Empty;
    public bool IsOpen { get; init; }

    /// <summary>True when more than one enumerated device reports the same serial number.</summary>
    public bool HasDuplicateSerial { get; init; }

    public bool IsFt232H => DeviceType == FtdiDeviceType.Ft232H;

    public override string ToString() =>
        $"[{Index}] {Description} SN='{SerialNumber}' {(HasDuplicateSerial ? "(DUP)" : string.Empty)}".Trim();
}

/// <summary>
/// Selects a physical device for connection. Because not all FT232H boards have
/// unique serial numbers, selection by serial alone may be ambiguous; in that case
/// <see cref="LocationId"/> (USB port location) disambiguates.
/// </summary>
public sealed record DeviceSelector
{
    public int? Index { get; init; }
    public string? SerialNumber { get; init; }
    public string? LocationId { get; init; }

    public static DeviceSelector ByIndex(int index) => new() { Index = index };
    public static DeviceSelector BySerial(string serial, string? locationId = null) =>
        new() { SerialNumber = serial, LocationId = locationId };
    public static DeviceSelector ByLocation(string locationId) => new() { LocationId = locationId };

    public override string ToString() =>
        Index is not null ? $"index {Index}" :
        LocationId is not null && SerialNumber is not null ? $"SN '{SerialNumber}' @ {LocationId}" :
        SerialNumber is not null ? $"SN '{SerialNumber}'" :
        LocationId is not null ? $"location {LocationId}" : "<empty>";
}