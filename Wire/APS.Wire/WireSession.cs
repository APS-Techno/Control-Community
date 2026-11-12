using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;

namespace APS.Wire;

/// <summary>Streaming Agent-wire session. One background reader routes ordinary replies to their
/// callers by <c>id</c> and hands every <c>push</c> frame to <see cref="Pushes"/>. A push frame never
/// carries <c>id</c>; a reply always does; a <c>snapshot</c> push may legitimately precede the
/// <c>bus.subscribe</c> ack (Control/docs/agent-wire-bus.md §3) — both are handled here. Use this,
/// not <see cref="WireConnection"/>, whenever the connection subscribes to the bus: the inline reader
/// of <see cref="WireConnection"/> would choke on unsolicited frames.</summary>
public sealed class WireSession : IWireSession
{
    private static readonly JsonSerializerOptions Opt = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly Stream _stream;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly Channel<JsonElement> _pushes;
    private readonly CancellationTokenSource _cts = new();
    private Task _readLoop = Task.CompletedTask;
    private int _nextId;

    public JsonElement AuthData { get; private set; }
    public ChannelReader<JsonElement> Pushes => _pushes.Reader;

    private WireSession(Stream stream, int pushQueueDepth)
    {
        _stream = stream;
        _reader = new StreamReader(stream);
        _writer = new StreamWriter(stream) { AutoFlush = true };
        _pushes = Channel.CreateBounded<JsonElement>(new BoundedChannelOptions(pushQueueDepth)
        { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
    }

    /// <summary>Authenticates over an already-open duplex stream and starts the reader loop.
    /// Throws <see cref="WireException"/> (server code, e.g. <c>auth_failed</c>) or
    /// <see cref="IOException"/> if the server drops the socket on a bad key.</summary>
    public static async Task<WireSession> OpenAsync(Stream stream, string apiKey, CancellationToken ct,
        int pushQueueDepth = 10_000)
    {
        var s = new WireSession(stream, pushQueueDepth);
        s._readLoop = Task.Run(s.ReadLoopAsync, CancellationToken.None);
        try
        {
            s.AuthData = await s.CallAsync("auth", new { key = apiKey }, ct);
            return s;
        }
        catch
        {
            await s.DisposeAsync();
            throw;
        }
    }

    /// <summary>TLS connect (<see cref="WireTls"/>) + <see cref="OpenAsync"/>. Every failure is a
    /// <see cref="WireException"/> (<c>unreachable</c>, <c>tls</c>, <c>disconnected</c>, or the server's code).</summary>
    public static async Task<WireSession> ConnectAsync(WireEndpoint ep, Action<string> log, CancellationToken ct)
    {
        var ssl = await WireTls.ConnectAsync(ep.Host, ep.Port, ep.CertSha256, log, ct);
        try
        {
            return await OpenAsync(ssl, ep.ApiKey, ct);
        }
        catch (IOException ex)
        {
            throw new WireException("disconnected",
                $"connection to APS Control at {ep.Host}:{ep.Port} dropped during auth ({ex.Message})", ex);
        }
    }

    public bool HasCapability(string name)
    {
        if (AuthData.ValueKind != JsonValueKind.Object
            || !AuthData.TryGetProperty("capabilities", out var caps)
            || caps.ValueKind != JsonValueKind.Array) return false;
        foreach (var c in caps.EnumerateArray())
            if (c.ValueKind == JsonValueKind.String && string.Equals(c.GetString(), name, StringComparison.Ordinal))
                return true;
        return false;
    }

    public async Task<JsonElement> CallAsync(string cmd, object? args, CancellationToken ct = default, string? actor = null)
    {
        int id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        try
        {
            object request = actor is null
                ? new { v = WireProtocol.Version, id, cmd, args }
                : new { v = WireProtocol.Version, id, cmd, args, actor };
            string line = JsonSerializer.Serialize(request, Opt);
            await _writeLock.WaitAsync(ct);
            try { await _writer.WriteLineAsync(line.AsMemory(), ct); }
            finally { _writeLock.Release(); }
            using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
            return await tcs.Task;
        }
        finally { _pending.TryRemove(id, out _); }
    }

    private async Task ReadLoopAsync()
    {
        Exception? cause = null;
        try
        {
            while (true)
            {
                string? line = await _reader.ReadLineAsync(_cts.Token);
                if (line is null) { cause = new IOException("connection closed by server"); break; }
                if (string.IsNullOrWhiteSpace(line)) continue;
                JsonElement root;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    root = doc.RootElement.Clone();
                }
                catch (JsonException) { continue; }                 // garbage line: keep the session alive

                if (root.ValueKind != JsonValueKind.Object) continue;
                if (root.TryGetProperty("push", out _))
                {
                    await _pushes.Writer.WriteAsync(root, _cts.Token);
                    continue;
                }
                if (root.TryGetProperty("id", out var rid) && rid.ValueKind == JsonValueKind.Number
                    && _pending.TryRemove(rid.GetInt32(), out var tcs))
                {
                    if (root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
                        tcs.TrySetResult(root.TryGetProperty("data", out var data) ? data : default);
                    else
                    {
                        string code = "unknown", message = "";
                        if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Object)
                        {
                            if (err.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String) code = c.GetString()!;
                            if (err.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String) message = m.GetString()!;
                        }
                        tcs.TrySetException(new WireException(code, message));
                    }
                }
                // else: a reply nobody waits on any more (caller cancelled) — dropped
            }
        }
        catch (OperationCanceledException) { cause = null; }        // DisposeAsync
        catch (ObjectDisposedException) { cause = null; }
        catch (Exception ex) { cause = ex; }

        var fail = cause ?? new ObjectDisposedException(nameof(WireSession));
        foreach (var kv in _pending) kv.Value.TrySetException(fail);
        _pending.Clear();
        _pushes.Writer.TryComplete(cause);
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { _stream.Dispose(); } catch { }
        try { await _readLoop; } catch { }
        _writeLock.Dispose();
        _cts.Dispose();
    }
}
