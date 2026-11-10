using System.Runtime.InteropServices;
using FT232H.Core.Devices;
using FT232H.Core.Hardware;

namespace FT232H.Hardware;

/// <summary>
/// Maps the FTD2XX_NET wrapper onto the FT232H.Core interfaces. This class owns
/// the FTDI handle lifecycle: enumeration, open, MPSSE initialization and
/// deterministic cleanup. All async methods are host-side async (D2XX itself is
/// synchronous; calls are wrapped so the UI never blocks).
/// </summary>
public sealed class FtdiDeviceManager : IFtdiDeviceManager
{
    public Task<IReadOnlyList<FtdiDeviceInfo>> EnumerateAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() => (IReadOnlyList<FtdiDeviceInfo>)EnumerateCore(), cancellationToken);
    }

    private static List<FtdiDeviceInfo> EnumerateCore()
    {
        FTD2XX_NET.FTDI ftdi;
        try
        {
            ftdi = new FTD2XX_NET.FTDI();
        }
        catch (DllNotFoundException ex)
        {
            throw new FtdiException(FtdiErrorCode.DriverNotFound,
                "FTD2XX.DLL was not found. Install the FTDI D2XX driver from ftdichip.com.", ex);
        }
        catch (Exception ex)
        {
            throw new FtdiException(FtdiErrorCode.DriverNotFound,
                "Failed to load the FTDI D2XX driver: " + ex.Message, ex);
        }

        uint count = 0;
        var status = ftdi.GetNumberOfDevices(ref count);
        if (status != FTD2XX_NET.FTDI.FT_STATUS.FT_OK)
            throw new FtdiException(FtdiErrorCode.DriverNotFound, $"Device enumeration failed: {status}");

        if (count == 0)
            return new List<FtdiDeviceInfo>();

        var nodes = new FTD2XX_NET.FTDI.FT_DEVICE_INFO_NODE[count];
        status = ftdi.GetDeviceList(nodes);
        if (status != FTD2XX_NET.FTDI.FT_STATUS.FT_OK)
            throw new FtdiException(FtdiErrorCode.DriverNotFound, $"GetDeviceList failed: {status}");

        var infos = new List<FtdiDeviceInfo>((int)count);
        for (int i = 0; i < count; i++)
        {
            var node = nodes[i];
            infos.Add(new FtdiDeviceInfo
            {
                Index = i,
                SerialNumber = node.SerialNumber ?? string.Empty,
                Description = node.Description ?? string.Empty,
                DeviceType = MapDeviceType(node.Type),
                LocationId = node.LocId.ToString("X8"),
                IsOpen = (node.Flags & 0x1) != 0,
            });
        }

        // Flag duplicate serial numbers: not all boards have unique serials, so
        // serial-only selection can be ambiguous. LocationId disambiguates.
        var duplicateSerials = infos
            .Where(i => !string.IsNullOrEmpty(i.SerialNumber))
            .GroupBy(i => i.SerialNumber)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);

        return infos
            .Select(i => i with { HasDuplicateSerial = duplicateSerials.Contains(i.SerialNumber) })
            .ToList();
    }

    public Task<IFtdiDevice> ConnectAsync(DeviceSelector selector, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ConnectCore(selector), cancellationToken);
    }

    private static IFtdiDevice ConnectCore(DeviceSelector selector)
    {
        var devices = EnumerateCore();
        FtdiDeviceInfo target = ResolveTarget(devices, selector);

        var ftdi = new FTD2XX_NET.FTDI();
        FTD2XX_NET.FTDI.FT_STATUS status;
        if (selector.LocationId is not null && uint.TryParse(selector.LocationId,
                System.Globalization.NumberStyles.HexNumber, null, out uint locId))
        {
            status = ftdi.OpenByLocation(locId);
        }
        else if (!string.IsNullOrEmpty(target.SerialNumber) && !target.HasDuplicateSerial)
        {
            status = ftdi.OpenBySerialNumber(target.SerialNumber);
        }
        else
        {
            status = ftdi.OpenByIndex((uint)target.Index);
        }

        if (status != FTD2XX_NET.FTDI.FT_STATUS.FT_OK)
            throw new FtdiException(FtdiErrorCode.ConnectionFailed,
                $"Could not open device {selector}: D2XX status {status}");

        return new FtdiDevice(ftdi, target);
    }

    private static FtdiDeviceInfo ResolveTarget(IReadOnlyList<FtdiDeviceInfo> devices, DeviceSelector selector)
    {
        if (selector.Index is int index)
        {
            var match = devices.FirstOrDefault(d => d.Index == index);
            return match ?? throw new FtdiException(FtdiErrorCode.DeviceNotFound, $"No device at index {index}.");
        }

        if (selector.LocationId is not null)
        {
            var byLocation = devices.Where(d =>
                string.Equals(d.LocationId, selector.LocationId, StringComparison.OrdinalIgnoreCase)).ToList();
            if (byLocation.Count == 1) return byLocation[0];
            throw new FtdiException(FtdiErrorCode.DeviceNotFound,
                $"No unique device at USB location {selector.LocationId}.");
        }

        if (selector.SerialNumber is not null)
        {
            var bySerial = devices.Where(d =>
                string.Equals(d.SerialNumber, selector.SerialNumber, StringComparison.Ordinal)).ToList();
            if (bySerial.Count == 1) return bySerial[0];
            if (bySerial.Count > 1)
                throw new FtdiException(FtdiErrorCode.AmbiguousDevice,
                    $"Serial '{selector.SerialNumber}' matches {bySerial.Count} devices (duplicate serials). " +
                    "Select by USB location instead.");
            throw new FtdiException(FtdiErrorCode.DeviceNotFound,
                $"No device with serial '{selector.SerialNumber}'.");
        }

        throw new FtdiException(FtdiErrorCode.DeviceNotFound, "Device selector is empty.");
    }

    public Task<IReadOnlyList<DiagnosticLine>> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<DiagnosticLine>>(() =>
        {
            var lines = new List<DiagnosticLine>();
            try
            {
                var ftdi = new FTD2XX_NET.FTDI();
                uint libVersion = 0;
                var status = ftdi.GetLibraryVersion(ref libVersion);
                lines.Add(new DiagnosticLine("FTD2XX_NET library",
                    status == FTD2XX_NET.FTDI.FT_STATUS.FT_OK
                        ? $"{(libVersion >> 16) & 0xFF}.{(libVersion >> 8) & 0xFF}.{libVersion & 0xFF}"
                        : status.ToString(),
                    status == FTD2XX_NET.FTDI.FT_STATUS.FT_OK));

                uint drvVersion = 0;
                status = ftdi.GetDriverVersion(ref drvVersion);
                lines.Add(new DiagnosticLine("D2XX driver",
                    status == FTD2XX_NET.FTDI.FT_STATUS.FT_OK
                        ? $"{(drvVersion >> 16) & 0xFF}.{(drvVersion >> 8) & 0xFF}.{drvVersion & 0xFF}"
                        : status.ToString(),
                    status == FTD2XX_NET.FTDI.FT_STATUS.FT_OK));

                uint count = 0;
                status = ftdi.GetNumberOfDevices(ref count);
                lines.Add(new DiagnosticLine("Devices found", count.ToString(),
                    status == FTD2XX_NET.FTDI.FT_STATUS.FT_OK));

                lines.Add(new DiagnosticLine("Process architecture",
                    RuntimeInformation.ProcessArchitecture.ToString(), true));
            }
            catch (DllNotFoundException ex)
            {
                lines.Add(new DiagnosticLine("FTD2XX.DLL", "NOT FOUND", false,
                    "Install the FTDI D2XX driver from ftdichip.com. " + ex.Message));
            }
            catch (Exception ex)
            {
                lines.Add(new DiagnosticLine("D2XX diagnostics", "ERROR", false, ex.Message));
            }

            return lines;
        }, cancellationToken);
    }

    private static FtdiDeviceType MapDeviceType(FTD2XX_NET.FTDI.FT_DEVICE type) => type switch
    {
        FTD2XX_NET.FTDI.FT_DEVICE.FT_DEVICE_232H => FtdiDeviceType.Ft232H,
        FTD2XX_NET.FTDI.FT_DEVICE.FT_DEVICE_2232H => FtdiDeviceType.Ft2232H,
        FTD2XX_NET.FTDI.FT_DEVICE.FT_DEVICE_4232H => FtdiDeviceType.Ft4232H,
        FTD2XX_NET.FTDI.FT_DEVICE.FT_DEVICE_232R => FtdiDeviceType.Ft232R,
        FTD2XX_NET.FTDI.FT_DEVICE.FT_DEVICE_BM => FtdiDeviceType.Ft232Bm,
        FTD2XX_NET.FTDI.FT_DEVICE.FT_DEVICE_AM => FtdiDeviceType.Ft232Am,
        FTD2XX_NET.FTDI.FT_DEVICE.FT_DEVICE_2232 => FtdiDeviceType.Ft2232C,
        FTD2XX_NET.FTDI.FT_DEVICE.FT_DEVICE_X_SERIES => FtdiDeviceType.FtXSeries,
        _ => FtdiDeviceType.Other,
    };
}