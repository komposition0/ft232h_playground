using FT232H.Core.Buffers;
using FT232H.Core.Devices;
using FT232H.Core.Hardware;
using FT232H.Simulation;
using Xunit;

namespace FT232H.Tests;

public class RingBufferTests
{
    [Fact]
    public void Add_OverwritesOldestWhenFull()
    {
        var buffer = new RingBuffer<int>(3);
        buffer.Add(1); buffer.Add(2); buffer.Add(3); buffer.Add(4);
        Assert.Equal(new[] { 2, 3, 4 }, buffer.ToArray());
    }

    [Fact]
    public void Clear_EmptiesBuffer()
    {
        var buffer = new RingBuffer<int>(3);
        buffer.Add(1);
        buffer.Clear();
        Assert.Equal(0, buffer.Count);
        Assert.Empty(buffer.ToArray());
    }

    [Fact]
    public void InvalidCapacity_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RingBuffer<int>(0));
    }
}

public class SimulatedDeviceManagerTests
{
    [Fact]
    public async Task Enumerate_FlagsDuplicateSerials()
    {
        var manager = new SimulatedFtdiDeviceManager(new SimulationOptions { IncludeDuplicateSerials = true });
        var devices = await manager.EnumerateAsync();
        Assert.True(devices.Count(d => d.HasDuplicateSerial) == 2);
    }

    [Fact]
    public async Task ConnectByDuplicateSerial_Alone_IsAmbiguous()
    {
        var manager = new SimulatedFtdiDeviceManager(new SimulationOptions { IncludeDuplicateSerials = true });
        var ex = await Assert.ThrowsAsync<FtdiException>(() =>
            manager.ConnectAsync(DeviceSelector.BySerial("FTSIM0001")));
        Assert.Equal(FtdiErrorCode.AmbiguousDevice, ex.Code);
    }

    [Fact]
    public async Task ConnectBySerialPlusLocation_Disambiguates()
    {
        var manager = new SimulatedFtdiDeviceManager(new SimulationOptions { IncludeDuplicateSerials = true });
        await using var device = await manager.ConnectAsync(DeviceSelector.BySerial("FTSIM0001", "SIM0001"));
        Assert.Equal("SIM0001", device.Info.LocationId);
        Assert.True(device.IsConnected);
    }

    [Fact]
    public async Task ConnectMissingDevice_Fails()
    {
        var manager = new SimulatedFtdiDeviceManager();
        await Assert.ThrowsAsync<FtdiException>(() =>
            manager.ConnectAsync(DeviceSelector.BySerial("DOESNOTEXIST")));
    }
}