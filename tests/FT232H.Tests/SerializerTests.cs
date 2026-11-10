using FT232H.Automation;
using FT232H.Core.Automation;
using FT232H.Core.Gpio;
using Xunit;

namespace FT232H.Tests;

public class TestSequenceSerializerTests
{
    [Fact]
    public void RoundTrip_PreservesSteps()
    {
        var sequence = new TestSequence
        {
            Name = "round trip",
            Description = "test",
            Steps =
            {
                new TestSequenceStep { Kind = TestActionKind.SpiWrite, WriteData = new byte[] { 0xAA } },
                new TestSequenceStep { Kind = TestActionKind.GpioSet, Pin = GpioPinId.AcBus3, PinValue = true },
                new TestSequenceStep
                {
                    Kind = TestActionKind.Repeat,
                    RepeatCount = 2,
                    Steps = new List<TestSequenceStep>
                    {
                        new() { Kind = TestActionKind.Delay, DelayMilliseconds = 5 },
                    },
                },
            },
        };

        string json = TestSequenceSerializer.Serialize(sequence);
        var restored = TestSequenceSerializer.Deserialize(json);

        Assert.Equal("round trip", restored.Name);
        Assert.Equal(3, restored.Steps.Count);
        Assert.Equal(TestActionKind.SpiWrite, restored.Steps[0].Kind);
        Assert.Equal(new byte[] { 0xAA }, restored.Steps[0].WriteData);
        Assert.Equal(GpioPinId.AcBus3, restored.Steps[1].Pin);
        Assert.Equal(2, restored.Steps[2].RepeatCount);
        Assert.Single(restored.Steps[2].Steps!);
    }

    [Fact]
    public void SaveAndLoad_Works()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ft232h_test_{Guid.NewGuid():N}.json");
        try
        {
            var sequence = new TestSequence { Name = "file test" };
            TestSequenceSerializer.SaveToFile(sequence, path);
            var loaded = TestSequenceSerializer.LoadFromFile(path);
            Assert.Equal("file test", loaded.Name);
        }
        finally
        {
            File.Delete(path);
        }
    }
}