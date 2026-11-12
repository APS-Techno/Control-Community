using System.Net.Sockets;

namespace APS.SystemControl.Core.HWClasses.Base;

/// <summary>
/// Thread-safe TCP client wrapper for Ethernet-connected SCPI instruments
/// (e.g. a power analyzer at TCP 5025).
/// Provides text-based send/receive using <c>\n</c> line termination,
/// with automatic reconnection on connection loss.
/// </summary>
/// <remarks>
/// Each instrument owns its own <see cref="BaseSocketPort"/> instance.
/// </remarks>
public sealed class BaseSocketPort : IDisposable
{
    private TcpClient? _client;
    private NetworkStream? _stream;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private bool _disposed;

    public string Host { get; }

    public int Port { get; }

    /// <summary>Timeout in milliseconds for connect, send, and receive operations.</summary>
    public int TimeoutMs { get; set; } = 5000;

    public bool IsConnected => _client?.Connected ?? false;

    /// <summary>
    /// The connection is NOT opened until <see cref="Connect"/> is called.
    /// </summary>
    public BaseSocketPort(string host, int port)
    {
        ArgumentNullException.ThrowIfNull(host);
        Host = host;
        Port = port;
    }

    public bool Connect()
    {
        // Serialise lifetime transitions on the SAME gate the send/receive path
        // uses, so a manual Reconnect cannot tear the connection fields while a
        // poll exchange is mid-flight. The internal reconnect (SendCommandCoreAsync
        // catch) calls ConnectCore directly because it already holds the gate.
        // (audit M-4 — mirrors BaseSerialPort.Open/Close.)
        _semaphore.Wait();
        try { return ConnectCore(); }
        finally { _semaphore.Release(); }
    }

    private bool ConnectCore()
    {
        if (_client?.Connected == true)
            return true;

        try
        {
            CleanupConnection();

            _client = new TcpClient();
            _client.SendTimeout = TimeoutMs;
            _client.ReceiveTimeout = TimeoutMs;

            var connectTask = _client.ConnectAsync(Host, Port);
            if (!connectTask.Wait(TimeoutMs))
            {
                CleanupConnection();
                return false;
            }

            _stream = _client.GetStream();
            _stream.ReadTimeout = TimeoutMs;
            _stream.WriteTimeout = TimeoutMs;

            _reader = new StreamReader(_stream, leaveOpen: true);
            _writer = new StreamWriter(_stream, leaveOpen: true)
            {
                AutoFlush = true,
                NewLine = "\n",
            };

            return true;
        }
        catch
        {
            CleanupConnection();
            return false;
        }
    }

    public void Disconnect()
    {
        // Take the gate so we never tear the connection fields under an in-flight
        // send/receive (audit M-4).
        _semaphore.Wait();
        try { CleanupConnection(); }
        finally { _semaphore.Release(); }
    }

    /// <summary>
    /// Sends a SCPI command string terminated with <c>\n</c> and reads a
    /// single response line. Auto-reconnects once if the connection is lost.
    /// </summary>
    /// <param name="command">The SCPI command to send (newline is appended automatically).</param>
    /// <returns>The response line with trailing newline characters stripped.</returns>
    /// <exception cref="IOException">Thrown when communication fails after reconnect attempt.</exception>
    public async Task<string> SendCommandAsync(string command)
    {
        ArgumentNullException.ThrowIfNull(command);

        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            return await SendCommandCoreAsync(command, allowReconnect: true).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public string SendCommand(string command)
        => SendCommandAsync(command).GetAwaiter().GetResult();

    /// <summary>
    /// Sends a SCPI command string terminated with <c>\n</c> without reading
    /// a response. Auto-reconnects once if the connection is lost.
    /// </summary>
    /// <param name="command">The SCPI command to send (newline is appended automatically).</param>
    public async Task SendAsync(string command)
    {
        ArgumentNullException.ThrowIfNull(command);

        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            await SendCoreAsync(command, allowReconnect: true).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Send(string command)
        => SendAsync(command).GetAwaiter().GetResult();

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        CleanupConnection();
        _semaphore.Dispose();
    }

    private async Task<string> SendCommandCoreAsync(string command, bool allowReconnect)
    {
        try
        {
            EnsureConnected();

            // Bound the exchange: NetworkStream.ReadTimeout only applies to
            // SYNCHRONOUS reads, so the bare ReadLineAsync below could block
            // forever on a half-open peer while holding _semaphore, wedging the
            // port. A CTS turns a silent peer into an OperationCanceledException that releases
            // the gate and triggers the reconnect. (audit M-5)
            using var cts = new CancellationTokenSource(TimeoutMs);
            await _writer!.WriteLineAsync(command.AsMemory(), cts.Token).ConfigureAwait(false);
            string? response = await _reader!.ReadLineAsync(cts.Token).ConfigureAwait(false);

            return response?.TrimEnd('\r', '\n') ?? string.Empty;
        }
        catch (Exception) when (allowReconnect)
        {
            // Connection lost — attempt one reconnect (we hold _semaphore, so call
            // the non-locking ConnectCore, not Connect).
            if (!ConnectCore())
                throw new IOException(
                    $"Lost connection to {Host}:{Port} and reconnect failed.");

            return await SendCommandCoreAsync(command, allowReconnect: false).ConfigureAwait(false);
        }
    }

    private async Task SendCoreAsync(string command, bool allowReconnect)
    {
        try
        {
            EnsureConnected();

            using var cts = new CancellationTokenSource(TimeoutMs);
            await _writer!.WriteLineAsync(command.AsMemory(), cts.Token).ConfigureAwait(false);
        }
        catch (Exception) when (allowReconnect)
        {
            // Connection lost — attempt one reconnect (we hold _semaphore, so call
            // the non-locking ConnectCore, not Connect).
            if (!ConnectCore())
                throw new IOException(
                    $"Lost connection to {Host}:{Port} and reconnect failed.");

            await SendCoreAsync(command, allowReconnect: false).ConfigureAwait(false);
        }
    }

    private void EnsureConnected()
    {
        if (_client?.Connected != true || _writer == null || _reader == null)
            throw new InvalidOperationException(
                $"Not connected to {Host}:{Port}. Call Connect() first.");
    }

    private void CleanupConnection()
    {
        try { _reader?.Dispose(); } catch { }
        try { _writer?.Dispose(); } catch { }
        try { _stream?.Dispose(); } catch { }
        try { _client?.Dispose(); } catch { }

        _reader = null;
        _writer = null;
        _stream = null;
        _client = null;
    }
}
