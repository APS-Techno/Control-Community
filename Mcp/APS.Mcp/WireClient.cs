using System.Net.Security;
using System.Net.Sockets;
using System.Text.Json;
using APS.Wire;

namespace APS.Mcp;

/// <summary>Owns the TLS connection to AgentServer: lazy connect, cert-fingerprint pinning,
/// auth on connect, single retry on a dropped connection. The transport itself (TCP/TLS/pinning
/// and its operator-facing error texts) lives in <see cref="WireTls"/> (APS.Wire).</summary>
public sealed class WireClient(McpBridgeConfig cfg) : IWireCaller, IAsyncDisposable
{
    /// <summary>Commands whose request may have already executed on the server even though the
    /// response never made it back — re-sending them on a dropped connection risks duplicate
    /// execution (a valve toggled twice, a recipe started twice, etc.), so they are never
    /// auto-retried.</summary>
    private static readonly HashSet<string> MutatingCommands = new(StringComparer.Ordinal)
    {
        "recipe.start", "recipe.upload", "recipe.abort", "recipe.resolve_pause",
        "device.command",
    };

    /// <summary>True if a connection error on <paramref name="cmd"/> is safe to silently retry
    /// once. `auth` is idempotent (issued internally during connect) and every read/describe/
    /// status command is safe by construction — only the five mutating commands above are not.
    /// `recipe.resolve_pause` is in that set for a sharper reason than the others: after a Retry the
    /// fault pause REOPENS within seconds, so a silent re-send can answer a DIFFERENT pause with a
    /// stale decision rather than failing harmlessly as "no pause waiting".</summary>
    public static bool IsRetryable(string cmd) => !MutatingCommands.Contains(cmd);

    private readonly SemaphoreSlim _sync = new(1, 1);
    private SslStream? _ssl;
    private WireConnection? _conn;

    private string Endpoint => $"{cfg.Host}:{cfg.Port}";

    /// <summary>Only an <see cref="McpException"/> message reaches the model, and ControlTools
    /// builds that solely from <see cref="WireException"/> — so every transport failure must leave
    /// here as a WireException naming the endpoint and the likely cause, or the operator sees the
    /// SDK's bare "An error occurred invoking 'x'".</summary>
    public async Task<JsonElement> CallAsync(string cmd, object? args, CancellationToken ct = default)
    {
        await _sync.WaitAsync(ct);
        try
        {
            try
            {
                var conn = await EnsureConnectedAsync(ct);
                return await conn.CallAsync(cmd, args, ct);
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
                Reset();
                if (!IsRetryable(cmd))
                    throw new WireException("disconnected",
                        $"connection to APS Control at {Endpoint} dropped while sending '{cmd}' " +
                        $"({ex.Message}); outcome unknown and NOT retried because the command " +
                        "mutates state — verify with recipe_status / tail_log before re-issuing", ex);
                try
                {
                    var conn = await EnsureConnectedAsync(ct);
                    return await conn.CallAsync(cmd, args, ct);
                }
                catch (Exception ex2) when (ex2 is IOException or SocketException or ObjectDisposedException)
                {
                    Reset();
                    throw new WireException("disconnected",
                        $"connection to APS Control at {Endpoint} dropped twice while sending " +
                        $"'{cmd}' ({ex2.Message}) — is APS.SystemControl.exe still running?", ex2);
                }
            }
        }
        finally { _sync.Release(); }
    }

    private async Task<WireConnection> EnsureConnectedAsync(CancellationToken ct)
    {
        if (_conn is not null) return _conn;
        // WireException("unreachable" | "tls") with the operator-facing texts comes from APS.Wire.
        _ssl = await WireTls.ConnectAsync(cfg.Host, cfg.Port, cfg.CertSha256,
            m => Console.Error.WriteLine("aps-mcp: " + m), ct);
        var conn = new WireConnection(_ssl);
        JsonElement auth;
        try
        {
            auth = await conn.AuthenticateAsync(cfg.ApiKey, ct);   // WireException on bad key
        }
        catch (IOException ex)
        {
            Reset();
            throw new WireException("disconnected",
                $"connection to APS Control at {Endpoint} dropped during auth ({ex.Message})", ex);
        }
        catch (WireException)
        {
            Reset();                                       // bad apiKey: server closes the socket
            throw;
        }
        await Console.Error.WriteLineAsync(
            $"aps-mcp: connected to {Endpoint} tier={auth.GetProperty("tier").GetString()}");
        _conn = conn;
        return conn;
    }

    private void Reset()
    {
        try { _ssl?.Dispose(); } catch { }
        _ssl = null; _conn = null;
    }

    public ValueTask DisposeAsync()
    {
        Reset();
        _sync.Dispose();
        return ValueTask.CompletedTask;
    }
}
