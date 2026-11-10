using FT232H.Core.Devices;
using FT232H.Core.Diagnostics;
using FT232H.Core.Hardware;
using FT232H.Hardware.Mpsse;

namespace FT232H.Hardware;

/// <summary>
/// One open FT232H device running in MPSSE mode. Owns the D2XX handle, the MPSSE
/// initialization sequence and the transfer arbiter that serializes SPI/GPIO use.
/// </summary>
public sealed class FtdiDevice : IFtdiDevice
{
    private readonly FTD2XX_NET.FTDI _ftdi;
    private readonly DeviceArbiter _arbiter = new();
    private readonly object _ioLock = new();
    private bool _disposed;

    internal FtdiDevice(FTD2XX_NET.FTDI ftdi, FtdiDeviceInfo info, InMemoryTransactionLogger? logger = null)
    {
        _ftdi = ftdi;
        Info = info;
        Logger = logger ?? new InMemoryTransactionLogger();
        Spi = new MpsseSpiController(this);
        Gpio = new MpsseGpioController(this);
        InitializeMpsse();
    }

    public FtdiDeviceInfo Info { get; }
    public bool IsConnected => !_disposed && _ftdi.IsOpen;
    public ISpiController Spi { get; }
    public IGpioController Gpio { get; }
    public InMemoryTransactionLogger Logger { get; }

    /// <summary>Arbiter ensuring single-owner MPSSE access per physical device.</summary>
    public DeviceArbiter Arbiter => _arbiter;

    /// <summary>
    /// MPSSE bring-up per FTDI AN_108 / D2XX application notes:
    /// reset the device, purge buffers, enable MPSSE bit mode (0x02), then
    /// synchronize the command processor with a bad-command echo (0xFA, 0xAA).
    /// </summary>
    private void InitializeMpsse()
    {
        lock (_ioLock)
        {
            Check(FTD2XX_NET.FTDI.FT_STATUS.FT_OK, "Pre-init");
            Check(_ftdi.ResetDevice(), "ResetDevice");
            Check(_ftdi.Purge(0x1 | 0x2), "Purge");           // FT_PURGE_RX | FT_PURGE_TX
            Check(_ftdi.SetBitMode(0x00, 0x00), "SetBitMode(reset)");
            Check(_ftdi.SetBitMode(0x00, 0x02), "SetBitMode(MPSSE)"); // FT_BIT_MODE_MPSSE
            Check(_ftdi.SetLatency(2), "SetLatency");          // ms; 2 ms is FTDI's practical default
            Check(_ftdi.SetTimeouts(3000, 3000), "SetTimeouts");
            Check(_ftdi.InTransferSize(65536), "InTransferSize");

            // Bad-command sync: an invalid opcode echoes [0xFA, opcode].
            var sync = new MpsseCommandBuilder().SyncBadCommand(0xAA).Build();
            WriteRaw(sync);
            var echo = ReadRaw(2, 3000);
            if (echo.Length < 2 || echo[0] != 0xFA || echo[1] != 0xAA)
                throw new FtdiException(FtdiErrorCode.ConnectionFailed,
                    "MPSSE synchronization failed: expected [FA AA] echo from bad-command probe. " +
                    $"Got: {BitConverter.ToString(echo)}");
        }
    }

    public Task ReinitializeAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            lock (_ioLock)
            {
                InitializeMpsse();
            }
        }, cancellationToken);
    }

    /// <summary>Writes raw MPSSE command bytes. Caller must hold the arbiter lease.</summary>
    internal void WriteRaw(byte[] commands)
    {
        if (_disposed) throw new FtdiException(FtdiErrorCode.Disposed, "Device is disposed.");
        uint written = 0;
        var status = _ftdi.Write(commands, (uint)commands.Length, ref written);
        Check(status, "Write");
        if (written != commands.Length)
            throw new FtdiException(FtdiErrorCode.TransferFailed,
                $"Short USB write: {written}/{commands.Length} bytes.");
    }

    /// <summary>Reads exactly <paramref name="expected"/> bytes within a host-side timeout.</summary>
    internal byte[] ReadRaw(int expected, int timeoutMs = 3000)
    {
        if (_disposed) throw new FtdiException(FtdiErrorCode.Disposed, "Device is disposed.");
        var buffer = new byte[expected];
        int total = 0;
        var deadline = Environment.TickCount64 + timeoutMs;
        while (total < expected)
        {
            uint available = 0;
            Check(_ftdi.GetRxBytesAvailable(ref available), "GetRxBytesAvailable");
            if (available == 0)
            {
                if (Environment.TickCount64 > deadline)
                    throw new FtdiException(FtdiErrorCode.Timeout,
                        $"Read timed out after {timeoutMs} ms with {total}/{expected} bytes.");
                Thread.Sleep(1);
                continue;
            }

            int want = (int)Math.Min(available, (uint)(expected - total));
            uint read = 0;
            var chunk = new byte[want];
            var status = _ftdi.Read(chunk, (uint)want, ref read);
            Check(status, "Read");
            Array.Copy(chunk, 0, buffer, total, (int)read);
            total += (int)read;
        }
        return buffer;
    }

    private void Check(FTD2XX_NET.FTDI.FT_STATUS status, string operation)
    {
        if (status != FTD2XX_NET.FTDI.FT_STATUS.FT_OK)
            throw new FtdiException(FtdiErrorCode.TransferFailed, $"{operation} failed: D2XX status {status}");
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            lock (_ioLock)
            {
                if (_disposed) return;
                _ftdi.SetBitMode(0x00, 0x00); // leave MPSSE mode
                _ftdi.Close();
                _disposed = true;
            }
        }, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
    }
}