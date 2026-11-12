using System.Text.Json;

namespace APS.Wire;

/// <summary>Framing + id correlation for the AgentServer line-JSON protocol, over any duplex stream.
/// One in-flight request at a time (MCP tool calls are sequential).</summary>
public sealed class WireConnection(Stream stream) : IWireCaller
{
    private static readonly JsonSerializerOptions Opt = new()
    { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly StreamReader _reader = new(stream);
    private readonly StreamWriter _writer = new(stream) { AutoFlush = true };
    private readonly SemaphoreSlim _inFlight = new(1, 1);
    private int _nextId;

    public Task<JsonElement> AuthenticateAsync(string apiKey, CancellationToken ct = default) =>
        CallAsync("auth", new { key = apiKey }, ct);

    public async Task<JsonElement> CallAsync(string cmd, object? args, CancellationToken ct = default)
    {
        await _inFlight.WaitAsync(ct);
        try
        {
            int id = Interlocked.Increment(ref _nextId);
            string line = JsonSerializer.Serialize(new { v = WireProtocol.Version, id, cmd, args }, Opt);
            await _writer.WriteLineAsync(line.AsMemory(), ct);
            while (true)
            {
                string? resp = await _reader.ReadLineAsync(ct)
                    ?? throw new IOException("connection closed by server");
                using var doc = JsonDocument.Parse(resp);
                var root = doc.RootElement;
                if (root.TryGetProperty("id", out var rid) && rid.GetInt32() != id)
                    continue;                            // stale frame from a prior timeout — skip
                if (root.GetProperty("ok").GetBoolean())
                    return root.TryGetProperty("data", out var data)
                        ? data.Clone()
                        : default;
                var err = root.GetProperty("error");
                throw new WireException(
                    err.GetProperty("code").GetString() ?? "unknown",
                    err.GetProperty("message").GetString() ?? "");
            }
        }
        finally { _inFlight.Release(); }
    }
}

