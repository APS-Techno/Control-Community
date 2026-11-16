using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using APS.Agent.Core.Providers;

namespace APS.Agent.Core.Journal;

public sealed record UsageRecord(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("cost")] double Cost);

public sealed record AuditSidecar(
    [property: JsonPropertyName("trial_id")] string TrialId,
    [property: JsonPropertyName("task")] string Task,
    [property: JsonPropertyName("selector")] string Selector,
    [property: JsonPropertyName("thinking")] string Thinking,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("answer")] string Answer,
    [property: JsonPropertyName("duration_s")] double DurationSeconds,
    [property: JsonPropertyName("catalog_estimated_cost_usd")] double CatalogEstimatedCostUsd,
    [property: JsonPropertyName("tool_call_count")] int ToolCallCount,
    [property: JsonPropertyName("config_digest")] string ConfigDigest,
    [property: JsonPropertyName("tool_schema_digest")] string ToolSchemaDigest,
    [property: JsonPropertyName("skill_digest")] string SkillDigest,
    [property: JsonPropertyName("journal_file")] string JournalFile,
    [property: JsonPropertyName("journal_sha256")] string JournalSha256,
    [property: JsonPropertyName("usage_records")] IReadOnlyList<UsageRecord> UsageRecords,
    [property: JsonPropertyName("fingerprint")] string? Fingerprint = null,
    [property: JsonPropertyName("error_category")] string? ErrorCategory = null);

public sealed class AuditJournalWriter : IAsyncDisposable
{
    private readonly StreamWriter _writer;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public AuditJournalWriter(string journalPath)
    {
        string? dir = Path.GetDirectoryName(journalPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);
        _writer = new StreamWriter(journalPath, append: false, new UTF8Encoding(false));
    }

    public async Task WriteEventAsync(string type, object payload, CancellationToken ct = default)
    {
        var node = JsonSerializer.SerializeToNode(payload, JsonOpts) as JsonObject ?? new JsonObject();
        node["at"] = DateTimeOffset.UtcNow.ToString("O");
        node["type"] = type;
        await _writer.WriteLineAsync(node.ToJsonString().AsMemory(), ct).ConfigureAwait(false);
        await _writer.FlushAsync(ct).ConfigureAwait(false);
    }

    public Task WriteUsageAsync(string key, double cost, ProviderUsage usage, CancellationToken ct = default) =>
        WriteEventAsync("usage", new
        {
            key,
            cost,
            input_tokens = usage.InputTokens,
            output_tokens = usage.OutputTokens,
            total_tokens = usage.TotalTokens,
        }, ct);

    public static string ComputeFileSha256(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
    }

    public static string WriteSidecar(string sidecarPath, AuditSidecar sidecar)
    {
        string? dir = Path.GetDirectoryName(sidecarPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);
        string json = JsonSerializer.Serialize(sidecar, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        });
        File.WriteAllText(sidecarPath, json + "\n", new UTF8Encoding(false));
        return sidecar.JournalSha256;
    }

    public async ValueTask DisposeAsync()
    {
        await _writer.FlushAsync().ConfigureAwait(false);
        await _writer.DisposeAsync().ConfigureAwait(false);
    }
}
