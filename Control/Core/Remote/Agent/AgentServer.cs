using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace APS.SystemControl.Core.Remote.Agent;

/// <summary>TLS TCP server hosting the agent wire protocol.
/// Stop-order discipline (cancel -> dispose endpoints -> bounded wait) to avoid exit hangs.</summary>
internal sealed class AgentServer(AgentServerConfig cfg, AgentDispatcher dispatcher, X509Certificate2 cert,
    IAgentStreamExtension? stream = null)
    : IDisposable
{
    private const int MaxClients = 4;
    private const int AuthTimeoutMs = 10_000;

    /// <summary>Bounded-line-reader cap: a single wire line (recipe upload included)
    /// comfortably fits well under this; anything past it is treated as abuse, not a
    /// legitimate request, so the read aborts rather than growing an unbounded string.</summary>
    private const int MaxLineChars = 4_000_000;

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;
    private readonly SemaphoreSlim _clientCap = new(MaxClients, MaxClients);
    private readonly List<TcpClient> _clients = [];
    private readonly object _clientsSync = new();

    /// <summary>Authenticated connections, keyed by the TcpClient serving them. Guarded by
    /// <see cref="_clientsSync"/> alongside <see cref="_clients"/>: a client appears here only
    /// between a successful auth and its disconnect, so it is strictly a subset.</summary>
    private readonly Dictionary<TcpClient, AgentSession> _sessions = [];

    /// <summary>Raised on a background thread whenever an agent authenticates or disconnects.
    /// Subscribers must marshal to their own thread and must not throw — the raise is guarded,
    /// but a slow handler stalls the client that triggered it.</summary>
    public event Action? SessionsChanged;

    public int BoundPort =>
        _listener?.LocalEndpoint is IPEndPoint ep ? ep.Port : cfg.Port;

    public bool IsListening => _acceptLoop is not null;

    /// <summary>Snapshot of the currently authenticated agents. Safe to call from any thread.</summary>
    public IReadOnlyList<AgentSession> Sessions
    {
        get { lock (_clientsSync) return [.. _sessions.Values]; }
    }

    private void RaiseSessionsChanged()
    {
        // A faulting UI handler must never tear down the client loop that raised it.
        try { SessionsChanged?.Invoke(); }
        catch (Exception ex) { Trace.TraceWarning($"AgentServer SessionsChanged handler threw: {ex.Message}"); }
    }

    public void Start()
    {
        if (_acceptLoop is not null) return;
        _listener = new TcpListener(IPAddress.Parse(cfg.Bind), cfg.Port);
        _listener.Start();
        _cts = new CancellationTokenSource();
        _acceptLoop = Task.Run(() => AcceptLoop(_cts.Token));
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }               // faults parked AcceptTcpClientAsync
        lock (_clientsSync)
        {
            foreach (var c in _clients) { try { c.Dispose(); } catch { } }  // faults parked reads
            _clients.Clear();
            _sessions.Clear();
        }
        try { _acceptLoop?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _acceptLoop = null;
        RaiseSessionsChanged();   // after _acceptLoop = null, so subscribers see "not listening"
        var cts = _cts;
        _cts = null;
        try { cts?.Dispose(); } catch { }
    }

    public void Dispose()
    {
        Stop();
        _clientCap.Dispose();
    }

    private async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener!.AcceptTcpClientAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // Log and back off before re-accepting to avoid a hot-spin on a transient
                // socket error.
                Trace.TraceWarning($"AgentServer accept error: {ex.Message}");
                try { await Task.Delay(250, ct); }
                catch (OperationCanceledException) { break; }
                continue;
            }
            catch { break; } // ct was cancelled; fall through to loop condition
            if (!_clientCap.Wait(0))
            {
                try { client.Dispose(); } catch { }        // over capacity: refuse silently
                continue;
            }
            lock (_clientsSync) _clients.Add(client);
            _ = Task.Run(() => HandleClientAsync(client, ct), CancellationToken.None);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            // Linked auth-timeout CTS created BEFORE the handshake and passed into it, so a raw
            // TCP connection that never completes (or never starts) the TLS handshake cannot hold
            // the _clientCap slot forever; 4 idle connections would lock out every client.
            using var authTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            authTimeout.CancelAfter(AuthTimeoutMs);

            using var ssl = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
            await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = cert,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            }, authTimeout.Token);
            using var reader = new StreamReader(ssl);
            var writer = new StreamWriter(ssl) { AutoFlush = true };
            var lines = new BoundedLineReader(reader, MaxLineChars);

            // One writer lock per connection: request replies and telemetry pushes share the stream.
            using var writeLock = new SemaphoreSlim(1, 1);
            async Task WriteLine(string s, CancellationToken t)
            {
                await writeLock.WaitAsync(t);
                try { await writer.WriteLineAsync(s.AsMemory(), t); }
                finally { writeLock.Release(); }
            }

            string? first = await lines.ReadLineAsync(authTimeout.Token);
            if (first is null) return;
            string authResp = dispatcher.HandleAuth(first, cfg, out var tier, out string keyName, out string? recipeFolder);
            await WriteLine(authResp, authTimeout.Token);
            if (tier == AgentTier.None) return;            // auth failed: reply sent, close

            lock (_clientsSync) _sessions[client] = new AgentSession(keyName, tier);
            RaiseSessionsChanged();

            using var session = stream?.CreateSession(cfg.BusSamplerMs, WriteLine, ct);
            string? line;
            while (!ct.IsCancellationRequested && (line = await lines.ReadLineAsync(ct)) is not null)
            {
                if (session is not null && AgentProtocol.TryParseRequest(line, out var req) && session.Handles(req.Cmd))
                    await WriteLine(await session.HandleAsync(req, tier, ct), ct);
                else
                    await WriteLine(dispatcher.Handle(line, tier, keyName, recipeFolder), ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (AuthenticationException) { }                // plain-TCP / bad-TLS prober
        catch (IOException) { }                            // client vanished mid-stream
        catch (Exception ex)
        {
            Trace.TraceWarning($"AgentServer client error: {ex.Message}");
        }
        finally
        {
            bool wasAuthenticated;
            lock (_clientsSync)
            {
                _clients.Remove(client);
                wasAuthenticated = _sessions.Remove(client);
            }
            try { client.Dispose(); } catch { }
            _clientCap.Release();
            // Only notify for connections that actually reached a granted tier — a TLS
            // prober or failed auth never showed up in the indicator to begin with.
            if (wasAuthenticated) RaiseSessionsChanged();
        }
    }

    /// <summary>Bounded replacement for <c>StreamReader.ReadLineAsync</c>: plain
    /// ReadLineAsync has no length limit, so a client that never sends '\n' can grow an
    /// unbounded in-memory string (line-length DoS, pre- or post-auth). This reads
    /// chunks via <see cref="StreamReader.ReadAsync(char[], CancellationToken)"/>,
    /// accumulating characters until '\n'; any chunk bytes read past the newline are
    /// retained for the next call, so lines split across TCP segments still parse
    /// correctly. One instance is created per connection (state carries across the
    /// first-line auth read and every subsequent command-line read on the same
    /// StreamReader). Once the accumulated line exceeds <paramref name="maxChars"/> it
    /// returns null immediately without draining anything further — the caller treats
    /// that exactly like EOF and closes the connection.</summary>
    private sealed class BoundedLineReader(StreamReader reader, int maxChars)
    {
        private readonly char[] _chunk = new char[8192];
        private int _chunkPos;
        private int _chunkLen;

        public async Task<string?> ReadLineAsync(CancellationToken ct)
        {
            var sb = new StringBuilder();
            while (true)
            {
                if (_chunkPos >= _chunkLen)
                {
                    _chunkLen = await reader.ReadAsync(_chunk, ct);
                    _chunkPos = 0;
                    if (_chunkLen == 0) return null;   // EOF, mid-line or clean: same as ReadLineAsync
                }
                char c = _chunk[_chunkPos++];
                if (c == '\n')
                {
                    if (sb.Length > 0 && sb[^1] == '\r') sb.Length--;   // CRLF, like ReadLineAsync
                    return sb.ToString();
                }
                if (sb.Length >= maxChars)
                    return null;   // over cap: abort without draining further; connection closes
                sb.Append(c);
            }
        }
    }
}
