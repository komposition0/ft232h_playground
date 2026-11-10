using System.Text.Json;
using System.Text.Json.Serialization;

namespace FT232H.Automation;

/// <summary>JSON persistence for test sequences (System.Text.Json).</summary>
public static class TestSequenceSerializer
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(Core.Automation.TestSequence sequence) =>
        JsonSerializer.Serialize(sequence, Options);

    public static Core.Automation.TestSequence Deserialize(string json) =>
        JsonSerializer.Deserialize<Core.Automation.TestSequence>(json, Options)
            ?? throw new JsonException("Test sequence JSON deserialized to null.");

    public static void SaveToFile(Core.Automation.TestSequence sequence, string path) =>
        File.WriteAllText(path, Serialize(sequence));

    public static Core.Automation.TestSequence LoadFromFile(string path) =>
        Deserialize(File.ReadAllText(path));
}