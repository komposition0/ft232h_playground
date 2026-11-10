namespace FT232H.Core.Hardware;

/// <summary>Error categories for FTDI/hardware operations.</summary>
public enum FtdiErrorCode
{
    Unknown = 0,
    DriverNotFound = 1,
    DeviceNotFound = 2,
    DeviceBusy = 3,
    AmbiguousDevice = 4,
    ConnectionFailed = 5,
    NotConnected = 6,
    ConfigurationInvalid = 7,
    TransferFailed = 8,
    Timeout = 9,
    PinReserved = 10,
    NotSupported = 11,
    Disposed = 12,
}

/// <summary>Exception thrown by hardware and simulation backends.</summary>
public sealed class FtdiException : Exception
{
    public FtdiErrorCode Code { get; }

    public FtdiException(FtdiErrorCode code, string message, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
    }
}