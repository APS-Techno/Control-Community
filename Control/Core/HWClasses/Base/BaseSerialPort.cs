using System.IO.Ports;
using System.Net.Sockets;

namespace APS.SystemControl.Core.HWClasses.Base;

public enum FramingMode
{
    None,

    /// <summary>Modbus RTU framing — CRC16 appended on send and verified on receive via <see cref="ModbusHelper"/>.</summary>
    ModbusRTU,

    /// <summary>
    /// Additive checksum — a two-byte big-endian 16-bit arithmetic sum
    /// over payload bytes [2..end] is appended on send (mirrors the Delphi
    /// <c>TPortControl.CheckSum</c>). The reply is NOT checksum-validated, matching
    /// the Delphi driver which checks only the frame prefix.
    /// </summary>
    AdditiveSum16,
}

public record SerialPortConfig
{
    public required string PortName { get; init; }
    public int BaudRate { get; init; } = 9600;
    public int DataBits { get; init; } = 8;
    public Parity Parity { get; init; } = Parity.None;
    public StopBits StopBits { get; init; } = StopBits.One;
    public FramingMode Framing { get; init; } = FramingMode.None;
    public int ReadTimeoutMs { get; init; } = 1000;
    public int WriteTimeoutMs { get; init; } = 1000;

    /// <summary>
    /// Maximum time to wait for exclusive access to the port before giving up
    /// with a <see cref="TimeoutException"/>.
    ///
    /// Read/Write timeouts only bound the I/O once the port lock is HELD. If a
    /// blocking read fails to honour its timeout — the known virtual-COM stall
    /// class that also wedges <c>SerialPort.Close()</c>, see SerialWedgeBreaker —
    /// an unbounded lock wait parks every later caller behind it indefinitely.
    /// Bounding acquisition turns that permanent wedge into a failed operation
    /// the caller can handle (e.g. a FAILURE_PROTOCOL step stepping over it).
    /// </summary>
    public int LockTimeoutMs { get; init; } = 5000;

    /// <summary>
    /// DTR line state asserted on open. Some USB-serial adapters require DTR
    /// asserted to power their line driver.
    /// </summary>
    public bool DtrEnable { get; init; }

    /// <summary>
    /// RTS line state asserted on open. Defaults to <c>false</c>. For an RS-485
    /// transceiver whose transmit/receive direction is wired to RTS (no hardware
    /// auto-direction), set this; leave <c>false</c> for adapters that handle
    /// direction themselves (the common case — do NOT force it blindly, as a
    /// statically-asserted RTS breaks an auto-direction adapter).
    /// </summary>
    public bool RtsEnable { get; init; }

    public Handshake Handshake { get; init; } = Handshake.None;
}

/// <summary>
/// Thread-safe wrapper around <see cref="System.IO.Ports.SerialPort"/> with
/// support for binary framing (Modbus RTU / additive checksum) and text protocols.
/// Each hardware device owns its own <see cref="BaseSerialPort"/> instance —
/// port sharing is not supported.
/// </summary>
public sealed class BaseSerialPort : IDisposable
{
    private readonly SerialPortConfig _config;
    private readonly SerialPort? _port;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private bool _disposed;

    private readonly bool _isTcp;
    private TcpClient? _tcpClient;
    private NetworkStream? _tcpStream;

    /// <summary>
    /// Test seam: the DTR/RTS/handshake line state actually applied to the
    /// underlying serial port, or <c>null</c> for a TCP instance.
    /// </summary>
    internal (bool DtrEnable, bool RtsEnable, Handshake Handshake)? SerialLineConfig
        => _isTcp ? null : (_port!.DtrEnable, _port.RtsEnable, _port.Handshake);

    /// <summary>
    /// The physical port is NOT opened until <see cref="Open"/> is called.
    /// If <see cref="SerialPortConfig.PortName"/> starts with "tcp://", a TCP connection
    /// is used instead of a physical serial port (for emulator testing).
    /// </summary>
    public BaseSerialPort(SerialPortConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config;
        _isTcp = config.PortName.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase);

        if (!_isTcp)
        {
            _port = new SerialPort(
                config.PortName,
                config.BaudRate,
                config.Parity,
                config.DataBits,
                config.StopBits)
            {
                ReadTimeout  = config.ReadTimeoutMs,
                WriteTimeout = config.WriteTimeoutMs,
                NewLine      = "\r",
                DtrEnable    = config.DtrEnable,
                RtsEnable    = config.RtsEnable,
                Handshake    = config.Handshake,
            };
        }
    }

    /// <returns><c>true</c> on success; <c>false</c> if the port is already open or an error occurs.</returns>
    public bool Open()
    {
        if (_disposed) return false;
        // Serialise lifetime transitions on the SAME gate the send/receive
        // path uses, so an Open/Close can never overlap an in-flight transfer
        // (the reconnect path runs on the Core thread while a poll/ramp thread
        // may be mid-Read inside the semaphore).
        _semaphore.Wait();
        try { return OpenCore(); }
        finally { _semaphore.Release(); }
    }

    private bool OpenCore()
    {
        if (_isTcp)
        {
            if (_tcpClient?.Connected == true)
                return false;
            try
            {
                var uri = _config.PortName[6..]; // strip "tcp://"
                int colonIdx = uri.LastIndexOf(':');
                string host = uri[..colonIdx];
                int port = int.Parse(uri[(colonIdx + 1)..]);

                _tcpClient = new TcpClient();
                _tcpClient.Connect(host, port);
                _tcpStream = _tcpClient.GetStream();
                _tcpStream.ReadTimeout = _config.ReadTimeoutMs;
                _tcpStream.WriteTimeout = _config.WriteTimeoutMs;
                return true;
            }
            catch
            {
                return false;
            }
        }

        if (_port!.IsOpen)
            return false;

        try
        {
            _port.Open();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Close()
    {
        if (_disposed) return;
        _semaphore.Wait();
        try { CloseCore(); }
        finally { _semaphore.Release(); }
    }

    // Actual teardown without acquiring _semaphore — callers (Close/Dispose)
    // already hold it. Must never be called without the gate held.
    private void CloseCore()
    {
        if (_isTcp)
        {
            try { _tcpStream?.Close(); _tcpClient?.Close(); }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning($"BaseSerialPort close (TCP) failed: {ex.Message}");
            }
            _tcpStream = null;
            _tcpClient = null;
            return;
        }

        // A serial port whose peer has gone wedges SerialPort.Close() forever
        // (the internal event-loop thread's WaitCommEvent never returns, so Close's
        // join hangs). SerialWedgeBreaker bounds the close and forcibly breaks the
        // wait if it wedges, so teardown always completes and the OS reclaims the
        // COM port. Healthy ports take the fast clean-close path unchanged.
        SerialWedgeBreaker.CloseSafely(_port, _port!.PortName);
    }

    public bool IsConnected => _isTcp ? _tcpClient?.Connected == true : _port!.IsOpen;

    /// <summary>
    /// Thread-safe binary send/receive.  Applies configured framing before
    /// sending and verifies framing on the received bytes.
    /// </summary>
    /// <param name="command">Raw command bytes (framing is added automatically).</param>
    /// <param name="expectedResponseLength">
    /// Expected byte count of the response including any framing bytes.
    /// Pass <c>0</c> to use silence-detection (100 ms of no new data).
    /// </param>
    /// <param name="timeoutMs">
    /// Receive timeout in milliseconds.  Pass <c>-1</c> to use the value from
    /// <see cref="SerialPortConfig.ReadTimeoutMs"/>.
    /// </param>
    /// <exception cref="TimeoutException">Thrown when no data is received within the timeout.</exception>
    /// <exception cref="InvalidOperationException">Thrown when framing verification fails.</exception>
    public async Task<byte[]> SendAndReceiveAsync(
        byte[] command,
        int expectedResponseLength = 0,
        int timeoutMs = -1)
    {
        ArgumentNullException.ThrowIfNull(command);

        int timeout = timeoutMs < 0 ? _config.ReadTimeoutMs : timeoutMs;
        byte[] framedCommand = ApplyFraming(command);

        await AcquirePortAsync().ConfigureAwait(false);
        try
        {
            if (_isTcp && _tcpStream != null)
            {
                await _tcpStream.WriteAsync(framedCommand).ConfigureAwait(false);
                await _tcpStream.FlushAsync().ConfigureAwait(false);

                byte[] response = await ReadTcpResponseAsync(expectedResponseLength, timeout).ConfigureAwait(false);
                VerifyResponseFraming(response);
                return response;
            }

            _port!.DiscardInBuffer();
            _port.Write(framedCommand, 0, framedCommand.Length);

            byte[] serialResponse = await ReadResponseAsync(expectedResponseLength, timeout).ConfigureAwait(false);
            VerifyResponseFraming(serialResponse);
            return serialResponse;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public byte[] SendAndReceive(
        byte[] command,
        int expectedResponseLength = 0,
        int timeoutMs = -1)
        => SendAndReceiveAsync(command, expectedResponseLength, timeoutMs)
           .GetAwaiter().GetResult();

    /// <summary>
    /// Acquires exclusive port access, bounded by
    /// <see cref="SerialPortConfig.LockTimeoutMs"/>. Throws instead of queueing
    /// forever behind an operation whose blocking read is not returning, so a
    /// single wedged call cannot take the device — and the recipe engine driving
    /// it — down indefinitely.
    /// </summary>
    /// <exception cref="TimeoutException">The port lock was not acquired in time.</exception>
    private async Task AcquirePortAsync()
    {
        if (!await _semaphore.WaitAsync(_config.LockTimeoutMs).ConfigureAwait(false))
            throw new TimeoutException(
                $"Port {_config.PortName} busy: exclusive access not acquired within "
                + $"{_config.LockTimeoutMs} ms — a previous operation is not returning.");
    }

    /// <summary>
    /// Thread-safe text send/receive for line-oriented protocols (e.g. stepper
    /// drive commands).  Sends <paramref name="command"/> followed by a newline
    /// and reads back a response line.
    /// </summary>
    /// <param name="command">Text command (newline is appended automatically).</param>
    /// <param name="timeoutMs">
    /// Receive timeout in milliseconds.  Pass <c>-1</c> to use the value from
    /// <see cref="SerialPortConfig.ReadTimeoutMs"/>.
    /// </param>
    /// <returns>Response line with any trailing newline characters stripped.</returns>
    /// <exception cref="TimeoutException">Thrown when no data is received within the timeout.</exception>
    public async Task<string> SendTextAsync(string command, int timeoutMs = -1)
    {
        ArgumentNullException.ThrowIfNull(command);

        int timeout = timeoutMs < 0 ? _config.ReadTimeoutMs : timeoutMs;

        await AcquirePortAsync().ConfigureAwait(false);
        try
        {
            if (_isTcp && _tcpStream != null)
            {
                byte[] cmdBytes = System.Text.Encoding.ASCII.GetBytes(command + "\r\n");
                await _tcpStream.WriteAsync(cmdBytes).ConfigureAwait(false);
                await _tcpStream.FlushAsync().ConfigureAwait(false);

                using var cts = new CancellationTokenSource(timeout);
                string response = await ReadTcpLineAsync(cts.Token).ConfigureAwait(false);
                return response.TrimEnd('\r', '\n');
            }

            _port!.DiscardInBuffer();
            _port.WriteLine(command);

            // Read the response line directly on the calling thread, bounded by the
            // SerialPort's own ReadTimeout. We deliberately do NOT offload the blocking
            // ReadLine to Task.Run: every hardware read already runs on a thread that is
            // meant to block here (a per-bus poller's dedicated thread, DriveStateReader,
            // or the HW command thread), and wrapping ReadLine in a thread-pool task while
            // the caller blocks on the result (GetAwaiter().GetResult()) costs a *second*
            // pool thread per read. With one bus poller per serial device that starves the
            // pool — the offloaded read sits queued past its timeout CTS and the task comes
            // back *canceled* (a TaskCanceledException, which callers that catch only
            // TimeoutException miss) instead of timing out, surfacing as a spurious
            // "HW command dispatch error: A task was canceled". Relying on ReadTimeout keeps
            // the read on the owning thread and yields a clean TimeoutException on silence.
            _port.ReadTimeout = timeout;
            try
            {
                return _port.ReadLine().TrimEnd('\r', '\n');
            }
            catch (TimeoutException)
            {
                throw new TimeoutException(
                    $"No response from {_config.PortName} within {timeout} ms.");
            }
        }
        finally
        {
            if (!_isTcp)
                _port!.ReadTimeout = _config.ReadTimeoutMs;
            _semaphore.Release();
        }
    }

    public string SendText(string command, int timeoutMs = -1)
        => SendTextAsync(command, timeoutMs).GetAwaiter().GetResult();

    public void Dispose()
    {
        if (_disposed)
            return;

        // Tear down under the gate so we cannot dispose the transport out from
        // under an in-flight transfer. CloseCore() (not Close()) because we
        // already hold the semaphore — SemaphoreSlim is not reentrant.
        _semaphore.Wait();
        try
        {
            if (_disposed) return;
            _disposed = true;
            CloseCore();
            _port?.Dispose();
            _tcpStream?.Dispose();
            _tcpClient?.Dispose();
        }
        finally
        {
            _semaphore.Release();
        }
        _semaphore.Dispose();
    }

    private byte[] ApplyFraming(byte[] command)
    {
        return _config.Framing switch
        {
            FramingMode.ModbusRTU => ModbusHelper.AppendCRC(command),
            FramingMode.AdditiveSum16 => AppendAdditiveSum16(command),
            _                     => command,
        };
    }

    private void VerifyResponseFraming(byte[] response)
    {
        switch (_config.Framing)
        {
            case FramingMode.ModbusRTU:
                if (!ModbusHelper.VerifyCRC(response))
                    throw new InvalidOperationException(
                        $"Modbus CRC verification failed for response from {_config.PortName}.");
                break;

            case FramingMode.AdditiveSum16:
                // Delphi never validates a reply checksum — its serial unit
                // checks only the frame prefix and reads
                // fields positionally. The device's reply-trailer format also
                // differs from the request framing, so a checksum check here
                // would wrongly reject every genuine reply. Do not verify.
                break;

            case FramingMode.None:
            default:
                break;
        }
    }

    /// <summary>
    /// Returns a new array containing <paramref name="data"/> followed by a
    /// two-byte big-endian 16-bit additive checksum computed over bytes
    /// <c>[2..end]</c> — header bytes <c>[0]</c> and <c>[1]</c> are excluded.
    /// Mirrors the Delphi <c>TPortControl.CheckSum</c> (unit_SerialPort.pas):
    /// a 16-bit arithmetic sum stored
    /// <c>Hi(sum)</c> then <c>Lo(sum)</c>.
    /// </summary>
    private static byte[] AppendAdditiveSum16(byte[] data)
    {
        // 16-bit accumulation wraps exactly like the Delphi `word` variable.
        ushort sum = 0;
        for (int i = 2; i < data.Length; i++)
            sum += data[i];

        var result = new byte[data.Length + 2];
        Array.Copy(data, result, data.Length);
        result[data.Length]     = (byte)(sum >> 8);
        result[data.Length + 1] = (byte)(sum & 0xFF);
        return result;
    }

    private async Task<byte[]> ReadResponseAsync(int expectedLength, int timeoutMs)
    {
        using var cts = new CancellationTokenSource(timeoutMs);

        if (expectedLength > 0)
            return await ReadExactAsync(expectedLength, cts.Token).ConfigureAwait(false);
        else
            return await ReadUntilSilenceAsync(cts.Token).ConfigureAwait(false);
    }

    private async Task<byte[]> ReadExactAsync(int count, CancellationToken ct)
    {
        var port = _port ?? throw new InvalidOperationException("Serial port not initialised.");

        var buffer = new byte[count];
        int received = 0;

        while (received < count)
        {
            ct.ThrowIfCancellationRequested();

            int available = port.BytesToRead;
            if (available > 0)
            {
                int toRead  = Math.Min(available, count - received);
                int actual  = port.Read(buffer, received, toRead);
                received   += actual;
            }
            else
            {
                if (received == 0 && ct.IsCancellationRequested)
                    throw new TimeoutException(
                        $"No response from {_config.PortName} within the specified timeout.");

                await Task.Delay(1, ct).ConfigureAwait(false);
            }
        }

        return buffer;
    }

    private async Task<byte[]> ReadTcpResponseAsync(int expectedLength, int timeoutMs)
    {
        // Capture the stream into a local under the caller-held semaphore so a
        // concurrent Close() (which also takes the semaphore now) can't null the
        // field out from under the repeated dereferences below mid-read.
        NetworkStream stream = _tcpStream
            ?? throw new InvalidOperationException("TCP stream not connected.");
        using var cts = new CancellationTokenSource(timeoutMs);

        var buffer = new byte[1024];
        var result = new List<byte>();

        if (expectedLength > 0)
        {
            int received = 0;
            var exactBuf = new byte[expectedLength];
            while (received < expectedLength)
            {
                int read = await stream.ReadAsync(
                    exactBuf.AsMemory(received, expectedLength - received), cts.Token).ConfigureAwait(false);
                if (read == 0) break;
                received += read;
            }
            // A peer close (read == 0) before expectedLength bytes arrive must
            // not be returned as a silently-truncated, partly-zero frame — that
            // surfaces downstream as bad data (FramingMode.None) or a confusing
            // CRC error (ModbusRTU) instead of a connection loss.
            if (received < expectedLength)
                throw new TimeoutException(
                    $"Short read from {_config.PortName}: got {received} of {expectedLength} " +
                    "bytes before the peer closed or the timeout fired.");
            return exactBuf;
        }

        // No expected length — do a blocking read (TCP responses arrive as complete frames)
        stream.ReadTimeout = timeoutMs;
        int firstRead = await stream.ReadAsync(buffer, cts.Token).ConfigureAwait(false);
        if (firstRead == 0)
            throw new TimeoutException($"No response from {_config.PortName} within {timeoutMs} ms.");

        for (int i = 0; i < firstRead; i++)
            result.Add(buffer[i]);

        // Check if there's more data immediately available (multi-packet response)
        await Task.Delay(10, cts.Token).ConfigureAwait(false);
        while (stream.DataAvailable)
        {
            int read = await stream.ReadAsync(buffer, cts.Token).ConfigureAwait(false);
            if (read == 0) break;
            for (int i = 0; i < read; i++)
                result.Add(buffer[i]);
        }

        return [.. result];
    }

    private async Task<string> ReadTcpLineAsync(CancellationToken ct)
    {
        if (_tcpStream == null) throw new InvalidOperationException("TCP stream not connected.");

        var lineBytes = new List<byte>();
        var buf = new byte[1];

        while (!ct.IsCancellationRequested)
        {
            int read = await _tcpStream.ReadAsync(buf.AsMemory(0, 1), ct).ConfigureAwait(false);
            if (read == 0) break;
            lineBytes.Add(buf[0]);
            if (buf[0] == '\n' || buf[0] == '\r')
                break;
        }

        return System.Text.Encoding.ASCII.GetString([.. lineBytes]);
    }

    private async Task<byte[]> ReadUntilSilenceAsync(CancellationToken ct)
    {
        const int SilenceMs = 100;
        var port = _port ?? throw new InvalidOperationException("Serial port not initialised.");

        var result   = new List<byte>();
        var lastData = DateTime.UtcNow;
        bool gotAny  = false;

        while (!ct.IsCancellationRequested)
        {
            int available = port.BytesToRead;
            if (available > 0)
            {
                var buf = new byte[available];
                int actual = port.Read(buf, 0, available);
                for (int i = 0; i < actual; i++)
                    result.Add(buf[i]);

                lastData = DateTime.UtcNow;
                gotAny   = true;
            }
            else
            {
                if (gotAny && (DateTime.UtcNow - lastData).TotalMilliseconds >= SilenceMs)
                    return [.. result];

                await Task.Delay(5, ct).ConfigureAwait(false);
            }
        }

        if (!gotAny)
            throw new TimeoutException(
                $"No response from {_config.PortName} within the specified timeout.");

        return [.. result];
    }
}
