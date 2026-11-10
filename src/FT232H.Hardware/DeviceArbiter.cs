using FT232H.Core.Hardware;

namespace FT232H.Hardware;

/// <summary>
/// Serializes all access to one physical device so SPI, GPIO and automated tests
/// can never issue interleaved/conflicting MPSSE command streams. The first
/// acquirer owns the device until the lease is disposed.
/// </summary>
public sealed class DeviceArbiter
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _holders;

    /// <summary>True while a lease is held.</summary>
    public bool IsBusy => Volatile.Read(ref _holders) > 0;

    /// <summary>
    /// Acquires exclusive access. Waits until the current holder releases or the
    /// token is cancelled. Throws <see cref="FtdiException"/> on cancellation so
    /// callers can surface it uniformly.
    /// </summary>
    public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            throw new FtdiException(FtdiErrorCode.DeviceBusy, "Timed out waiting for exclusive device access.", ex);
        }

        Interlocked.Increment(ref _holders);
        return new Lease(this);
    }

    private void Release()
    {
        Interlocked.Decrement(ref _holders);
        _gate.Release();
    }

    private sealed class Lease : IDisposable
    {
        private DeviceArbiter? _owner;
        public Lease(DeviceArbiter owner) => _owner = owner;

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.Release();
        }
    }
}