using FT232H.Core.Devices;
using FT232H.Core.Gpio;
using FT232H.Core.Hardware;
using FT232H.Core.Spi;
using FT232H.Simulation;
using Xunit;

namespace FT232H.Tests;

public class SimulatedSpiTests
{
    private static async Task<IFtdiDevice> ConnectAsync(bool loopback = true)
    {
        var manager = new SimulatedFtdiDeviceManager(new SimulationOptions
        {
            LatencyMilliseconds = 0,
            SpiLoopback = loopback,
        });
        return await manager.ConnectAsync(DeviceSelector.ByIndex(0));
    }

    [Fact]
    public async Task WriteRead_Loopback_EchoesPayload()
    {
        await using var device = await ConnectAsync();
        await device.Spi.ApplyConfigurationAsync(new SpiConfiguration());
        var result = await device.Spi.TransferAsync(new SpiTransferRequest
        {
            Kind = SpiTransferKind.WriteRead,
            WriteData = new byte[] { 0xDE, 0xAD },
        });
        Assert.True(result.Success);
        Assert.Equal(new byte[] { 0xDE, 0xAD }, result.ReceivedData);
    }

    [Fact]
    public async Task ReadOnly_ReturnsRequestedLength()
    {
        await using var device = await ConnectAsync(loopback: false);
        await device.Spi.ApplyConfigurationAsync(new SpiConfiguration());
        var result = await device.Spi.TransferAsync(new SpiTransferRequest
        {
            Kind = SpiTransferKind.ReadOnly,
            ReadLength = 4,
        });
        Assert.True(result.Success);
        Assert.Equal(4, result.ReceivedData.Length);
        Assert.All(result.ReceivedData, b => Assert.Equal(0xA5, b));
    }

    [Fact]
    public async Task ManualCs_WithoutAssertion_Fails()
    {
        await using var device = await ConnectAsync();
        await device.Spi.ApplyConfigurationAsync(new SpiConfiguration { ChipSelectMode = ChipSelectMode.Manual });
        var result = await device.Spi.TransferAsync(new SpiTransferRequest
        {
            Kind = SpiTransferKind.WriteOnly,
            WriteData = new byte[] { 0x01 },
        });
        Assert.False(result.Success);
    }

    [Fact]
    public async Task ManualCs_WithAssertion_Succeeds()
    {
        await using var device = await ConnectAsync();
        await device.Spi.ApplyConfigurationAsync(new SpiConfiguration { ChipSelectMode = ChipSelectMode.Manual });
        await device.Spi.SetChipSelectAsync(true);
        var result = await device.Spi.TransferAsync(new SpiTransferRequest
        {
            Kind = SpiTransferKind.WriteOnly,
            WriteData = new byte[] { 0x01 },
        });
        Assert.True(result.Success);
    }

    [Fact]
    public async Task InvalidConfiguration_IsRejected()
    {
        await using var device = await ConnectAsync();
        await Assert.ThrowsAsync<FtdiException>(() =>
            device.Spi.ApplyConfigurationAsync(new SpiConfiguration { ClockHz = 90_000_000 }));
    }
}

public class SimulatedGpioTests
{
    [Fact]
    public async Task WriteAndRead_Pin()
    {
        var manager = new SimulatedFtdiDeviceManager(new SimulationOptions { LatencyMilliseconds = 0 });
        await using var device = await manager.ConnectAsync(DeviceSelector.ByIndex(0));
        var gpio = device.Gpio;

        var write = await gpio.WritePinAsync(GpioPinId.AdBus5, true);
        Assert.True(write.Success);
        Assert.True(await gpio.ReadPinAsync(GpioPinId.AdBus5));
    }

    [Fact]
    public async Task WriteToSpiReservedPin_IsRejected()
    {
        var manager = new SimulatedFtdiDeviceManager(new SimulationOptions { LatencyMilliseconds = 0 });
        await using var device = await manager.ConnectAsync(DeviceSelector.ByIndex(0));

        foreach (var pin in new[] { GpioPinId.AdBus0, GpioPinId.AdBus1, GpioPinId.AdBus2, GpioPinId.AdBus3 })
        {
            var result = await device.Gpio.WritePinAsync(pin, true);
            Assert.False(result.Success);
            Assert.NotNull(result.Error);
            Assert.Contains("SPI", result.Error);
        }
    }

    [Fact]
    public async Task Pulse_TogglesBackToLow()
    {
        var manager = new SimulatedFtdiDeviceManager(new SimulationOptions { LatencyMilliseconds = 0 });
        await using var device = await manager.ConnectAsync(DeviceSelector.ByIndex(0));
        var result = await device.Gpio.PulseAsync(GpioPinId.AcBus1, TimeSpan.FromMilliseconds(5));
        Assert.True(result.Success);
        Assert.False(await device.Gpio.ReadPinAsync(GpioPinId.AcBus1));
    }
}