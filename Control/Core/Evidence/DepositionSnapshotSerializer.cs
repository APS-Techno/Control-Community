using System.Text.Json;
using System.Text.Json.Serialization;

namespace APS.SystemControl.Core.Evidence;

public static class DepositionSnapshotSerializer
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = null, // preserve dictionary keys verbatim (reading ids, "control"/"worker")
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string ToJson(DepositionSnapshot snapshot) => JsonSerializer.Serialize(snapshot, Opts);

    public static DepositionSnapshot FromJson(string json) =>
        JsonSerializer.Deserialize<DepositionSnapshot>(json, Opts)
        ?? throw new InvalidDataException("Empty or invalid deposition snapshot JSON.");
}
