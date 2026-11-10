using FT232H.Automation;
using FT232H.Core.Automation;
using FT232H.Core.Devices;
using FT232H.Core.Gpio;
using FT232H.Core.Spi;
using FT232H.Simulation;
using Xunit;

namespace FT232H.Tests;

public class TestSequenceRunnerTests
{
    private static async Task<(TestSequenceRunner Runner, FT232H.Core.Hardware.IFtdiDevice Device)> CreateAsync()
    {
        var manager = new SimulatedFtdiDeviceManager(new SimulationOptions
        {
            LatencyMilliseconds = 0,
            SpiLoopback = true,
        });
        var device = await manager.ConnectAsync(DeviceSelector.ByIndex(0));
        await device.Spi.ApplyConfigurationAsync(new SpiConfiguration());
        return (new TestSequenceRunner(), device);
    }

    [Fact]
    public async Task SimpleSequence_Passes()
    {
        var (runner, device) = await CreateAsync();
        await using var _ = device;

        var sequence = new TestSequence
        {
            Name = "smoke",
            Steps =
            {
                new TestSequenceStep { Kind = TestActionKind.SpiWrite, WriteData = new byte[] { 0x01, 0x02 } },
                new TestSequenceStep { Kind = TestActionKind.Delay, DelayMilliseconds = 1 },
                new TestSequenceStep { Kind = TestActionKind.GpioSet, Pin = GpioPinId.AdBus4, PinValue = true },
                new TestSequenceStep { Kind = TestActionKind.LogMessage, Message = "done" },
            },
        };

        var result = await runner.RunAsync(sequence, device.Spi, device.Gpio);
        Assert.True(result.Success);
        Assert.Equal(4, result.Steps.Count);
    }

    [Fact]
    public async Task CompareBytes_AgainstLoopback_Passes()
    {
        var (runner, device) = await CreateAsync();
        await using var _ = device;

        var sequence = new TestSequence
        {
            Steps =
            {
                new TestSequenceStep { Kind = TestActionKind.SpiWriteRead, WriteData = new byte[] { 0xCA, 0xFE } },
                new TestSequenceStep { Kind = TestActionKind.CompareBytes, ExpectedData = new byte[] { 0xCA, 0xFE } },
            },
        };

        var result = await runner.RunAsync(sequence, device.Spi, device.Gpio);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task CompareBytes_Mismatch_Fails()
    {
        var (runner, device) = await CreateAsync();
        await using var _ = device;

        var sequence = new TestSequence
        {
            Steps =
            {
                new TestSequenceStep { Kind = TestActionKind.SpiWriteRead, WriteData = new byte[] { 0x00 } },
                new TestSequenceStep { Kind = TestActionKind.CompareBytes, ExpectedData = new byte[] { 0xFF } },
            },
        };

        var result = await runner.RunAsync(sequence, device.Spi, device.Gpio);
        Assert.False(result.Success);
    }

    [Fact]
    public async Task Repeat_ExecutesNestedSteps()
    {
        var (runner, device) = await CreateAsync();
        await using var _ = device;

        var sequence = new TestSequence
        {
            Steps =
            {
                new TestSequenceStep
                {
                    Kind = TestActionKind.Repeat,
                    RepeatCount = 3,
                    Steps = new List<TestSequenceStep>
                    {
                        new() { Kind = TestActionKind.GpioSet, Pin = GpioPinId.AdBus4, PinValue = true },
                        new() { Kind = TestActionKind.GpioSet, Pin = GpioPinId.AdBus4, PinValue = false },
                    },
                },
            },
        };

        var result = await runner.RunAsync(sequence, device.Spi, device.Gpio);
        Assert.True(result.Success);
        Assert.Equal(7, result.Steps.Count); // 1 repeat + 3×2 nested
    }

    [Fact]
    public async Task Stop_EndsSequence()
    {
        var (runner, device) = await CreateAsync();
        await using var _ = device;

        var sequence = new TestSequence
        {
            Steps =
            {
                new TestSequenceStep { Kind = TestActionKind.Stop, StopReason = "enough" },
                new TestSequenceStep { Kind = TestActionKind.SpiWrite, WriteData = new byte[] { 0x01 } },
            },
        };

        var result = await runner.RunAsync(sequence, device.Spi, device.Gpio);
        Assert.Single(result.Steps);
    }

    [Fact]
    public async Task Cancellation_StopsRun()
    {
        var (runner, device) = await CreateAsync();
        await using var _ = device;

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var sequence = new TestSequence
        {
            Steps = { new TestSequenceStep { Kind = TestActionKind.Delay, DelayMilliseconds = 100 } },
        };

        var result = await runner.RunAsync(sequence, device.Spi, device.Gpio, null, cts.Token);
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }
}