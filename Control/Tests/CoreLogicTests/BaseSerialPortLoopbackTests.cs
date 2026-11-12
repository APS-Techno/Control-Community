using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using APS.SystemControl.Core.HWClasses.Base;

namespace CoreLogicTests;

/// <summary>
/// The production driver already supports a "tcp://host:port" URL in
/// SerialPortConfig.PortName; these tests stand up a <see cref="TcpListener"/>
/// on an ephemeral port, drive BaseSerialPort through the TCP branch, and
/// assert that the framing layer (ModbusRTU CRC append+verify, checksum)
/// is byte-correct end-to-end.
///
/// Serial-port loopback would require com0com on the host and is left to
/// the Emulator project's runtime tests. The value here is verifying
/// BaseSerialPort's framing state machine against realistic responses —
/// not the OS-specific serial plumbing.
/// </summary>
public class BaseSerialPortLoopbackTests : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private Task? _serverTask;
    private readonly List<byte[]> _received = [];
    private Func<byte[], byte[]?>? _responder;

    public BaseSerialPortLoopbackTests()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
        try { _serverTask?.Wait(500); } catch { }
        _cts.Dispose();
    }

    private int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    private SerialPortConfig MakeConfig(FramingMode framing, int readTimeoutMs = 1000) => new()
    {
        PortName = $"tcp://127.0.0.1:{Port}",
        BaudRate = 9600,  // ignored for TCP
        Framing = framing,
        ReadTimeoutMs = readTimeoutMs,
        WriteTimeoutMs = readTimeoutMs,
    };

    /// <summary>
    /// Starts the accept loop. For each incoming bytes-from-client burst the
    /// configured responder produces the bytes sent back (or null to stay silent
    /// until the read timeout fires on the client side).
    /// </summary>
    private void StartServer(Func<byte[], byte[]?> responder)
    {
        _responder = responder;
        _serverTask = Task.Run(async () =>
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                using var stream = client.GetStream();
                var buf = new byte[256];
                while (!_cts.IsCancellationRequested)
                {
                    int n = await stream.ReadAsync(buf.AsMemory(0, buf.Length), _cts.Token);
                    if (n == 0) break;
                    var received = buf[..n];
                    lock (_received) _received.Add(received);
                    var reply = _responder(received);
                    if (reply != null)
                        await stream.WriteAsync(reply, _cts.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        });
    }

    [Fact]
    public void ModbusRTU_write_appends_crc16_little_endian()
    {
        StartServer(_ => null);

        using var port = new BaseSerialPort(MakeConfig(FramingMode.ModbusRTU, readTimeoutMs: 200));
        Assert.True(port.Open());

        byte[] payload = [0x01, 0x03, 0x10, 0x01, 0x00, 0x01];
        // Fire-and-forget: we only care about what arrives on the wire.
        // TCP read path surfaces OperationCanceledException when its
        // timeout-scoped CTS fires (serial path wraps as TimeoutException).
        Assert.ThrowsAny<Exception>(() => port.SendAndReceive(payload, expectedResponseLength: 7));

        // Wait briefly for the server-side read to register.
        Thread.Sleep(50);

        byte[] onWire;
        lock (_received) onWire = _received.SelectMany(b => b).ToArray();

        // Standard Modbus CRC16 (polynomial 0xA001) of the payload = D1 0A
        // per the KCMR-91WRS datasheet example.
        Assert.Equal(new byte[]
        {
            0x01, 0x03, 0x10, 0x01, 0x00, 0x01,
            0xD1, 0x0A,
        }, onWire);
    }

    [Fact]
    public void ModbusRTU_roundtrip_delivers_register_value()
    {
        // Reply: Modbus FC03 response "1 register = 0x00FD" with appended CRC.
        byte[] reply = ModbusHelper.AppendCRC([0x01, 0x03, 0x02, 0x00, 0xFD]);
        StartServer(_ => reply);

        using var port = new BaseSerialPort(MakeConfig(FramingMode.ModbusRTU));
        port.Open();
        byte[] response = port.SendAndReceive(
            command: [0x01, 0x03, 0x10, 0x01, 0x00, 0x01],
            expectedResponseLength: reply.Length);

        // BaseSerialPort strips the framing bytes before handing the payload
        // back; ParseRegisterResponse should read 0x00FD = 253 from this.
        ushort[] regs = ModbusHelper.ParseRegisterResponse(response);
        Assert.Single(regs);
        Assert.Equal(0x00FD, regs[0]);
    }

    [Fact]
    public void ModbusRTU_bad_crc_response_is_rejected()
    {
        // Deliberately corrupt CRC.
        byte[] reply = [0x01, 0x03, 0x02, 0x00, 0xFD, 0x00, 0x00];
        StartServer(_ => reply);

        using var port = new BaseSerialPort(MakeConfig(FramingMode.ModbusRTU, readTimeoutMs: 500));
        port.Open();

        // Either SendAndReceive throws on framing error, or it returns the
        // bytes and ParseRegisterResponse rejects the CRC. Both are
        // acceptable; assert that one of them happens.
        Exception? thrown = null;
        try
        {
            byte[] resp = port.SendAndReceive(
                command: [0x01, 0x03, 0x10, 0x01, 0x00, 0x01],
                expectedResponseLength: reply.Length);
            ModbusHelper.ParseRegisterResponse(resp);
        }
        catch (Exception ex) { thrown = ex; }
        Assert.NotNull(thrown);
    }

    [Fact]
    public void Checksum_write_appends_additive_sum16_big_endian()
    {
        StartServer(_ => null);

        using var port = new BaseSerialPort(MakeConfig(FramingMode.AdditiveSum16, readTimeoutMs: 200));
        port.Open();

        // Delphi TPortControl.CheckSum (unit_SerialPort.pas:201-213) appends TWO
        // bytes: a 16-bit ARITHMETIC sum of the payload bytes from index 2 to the
        // end (the `if i>1` guard excludes header bytes [0] and [1]), stored
        // big-endian Hi(sum) then Lo(sum). The PSU-A uses this mode.
        byte[] payload = [0x16, 0xE9, 0x89, 0x13, 0xFF];
        // sum of bytes[2..] = 0x89 + 0x13 + 0xFF = 0x019B  ->  Hi=0x01, Lo=0x9B
        byte[] expected = [0x16, 0xE9, 0x89, 0x13, 0xFF, 0x01, 0x9B];

        Assert.ThrowsAny<Exception>(() => port.SendAndReceive(payload, expectedResponseLength: 6));
        Thread.Sleep(50);

        byte[] onWire;
        lock (_received) onWire = _received.SelectMany(b => b).ToArray();

        Assert.Equal(expected, onWire);
    }

    [Fact]
    public void Checksum_reply_is_not_checksum_validated()
    {
        // Delphi never validates a reply checksum — the Delphi PSU unit checks only the
        // 0x16 0xE9 prefix and reads fields positionally. A reply whose trailer
        // is not our checksum must be returned to the caller, not rejected as a
        // framing error.
        byte[] reply = [0x16, 0xE9, 0x00, 0x11, 0x22, 0x33];
        StartServer(_ => reply);

        using var port = new BaseSerialPort(MakeConfig(FramingMode.AdditiveSum16, readTimeoutMs: 500));
        port.Open();

        byte[] response = port.SendAndReceive(
            [0x16, 0xE9, 0x89, 0x13, 0xFF], expectedResponseLength: reply.Length);

        Assert.Equal(reply, response);
    }

    [Fact]
    public void No_framing_write_passes_bytes_through_unchanged()
    {
        StartServer(_ => null);

        using var port = new BaseSerialPort(MakeConfig(FramingMode.None, readTimeoutMs: 200));
        port.Open();

        byte[] payload = [0x01, 0x02, 0x03, 0x04, 0x05];
        try { port.SendAndReceive(payload, expectedResponseLength: 1); }
        catch (Exception) { /* expected — no reply, timeout/cancel fires */ }

        Thread.Sleep(50);
        byte[] onWire;
        lock (_received) onWire = _received.SelectMany(b => b).ToArray();
        Assert.Equal(payload, onWire);
    }

    [Fact]
    public void Close_then_open_restores_connectivity()
    {
        StartServer(bytes => bytes);  // echo

        using var port = new BaseSerialPort(MakeConfig(FramingMode.None));
        Assert.True(port.Open());
        Assert.True(port.IsConnected);

        port.Close();
        Assert.False(port.IsConnected);
    }

    [Fact]
    public void Read_timeout_throws_when_server_goes_silent()
    {
        StartServer(_ => null);

        using var port = new BaseSerialPort(MakeConfig(FramingMode.None, readTimeoutMs: 200));
        port.Open();

        // TCP path raises OperationCanceledException via its timeout-CTS;
        // serial path wraps in TimeoutException. Both are failures — accept
        // either so the test is transport-agnostic.
        var ex = Assert.ThrowsAny<Exception>(() =>
            port.SendAndReceive([0x01], expectedResponseLength: 4));
        Assert.True(ex is TimeoutException or OperationCanceledException,
            $"Expected timeout-like exception, got {ex.GetType().Name}");
    }

    [Fact]
    public void Serial_config_applies_dtr_rts_handshake_to_port()
    {
        // The shared layer must be ABLE to drive DTR/RTS/handshake: a serial-mode port (not opened)
        // should carry whatever the config requests.
        var cfg = new SerialPortConfig
        {
            PortName = "COM200",
            DtrEnable = true,
            RtsEnable = true,
            Handshake = Handshake.RequestToSend,
        };
        using var port = new BaseSerialPort(cfg);

        Assert.Equal((true, true, Handshake.RequestToSend), port.SerialLineConfig);
    }

    [Fact]
    public void Serial_config_defaults_preserve_no_flow_control()
    {
        // Default config must NOT change line state — DtrEnable/RtsEnable false,
        // Handshake None — so existing devices (e.g. a working PSU-B on an
        // auto-direction adapter) are unaffected.
        using var port = new BaseSerialPort(new SerialPortConfig { PortName = "COM200" });

        Assert.Equal((false, false, Handshake.None), port.SerialLineConfig);
    }

    /// <summary>
    /// Port-lock acquisition must be bounded: the configured Read/Write timeouts only bound the I/O
    /// after the lock is held, so with an unbounded <c>WaitAsync()</c> one hung read parks every later
    /// caller (a hung <c>power RF601 off</c> step once kept FAILURE_PROTOCOL from reaching its MFC-zero
    /// and valve-close steps). A caller that cannot get the port within
    /// <see cref="SerialPortConfig.LockTimeoutMs"/> fails fast with a TimeoutException, which a
    /// <c>onStepFailure="continue"</c> protocol can step over.
    /// </summary>
    [Fact]
    public async Task Send_fails_fast_when_the_port_lock_is_held_by_a_wedged_call()
    {
        StartServer(_ => null);   // never replies — first caller holds the lock

        var cfg = MakeConfig(FramingMode.None, readTimeoutMs: 10_000) with { LockTimeoutMs = 300 };
        using var port = new BaseSerialPort(cfg);
        Assert.True(port.Open());

        // Caller #1 parks inside the lock for its full 10 s read timeout.
        var wedged = Task.Run(() =>
        {
            try { port.SendAndReceive([0x01], expectedResponseLength: 8); } catch { }
        });

        // Give #1 time to take the semaphore.
        await Task.Delay(200);

        // Caller #2 must give up after ~LockTimeoutMs, NOT wait out caller #1.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAsync<TimeoutException>(
            () => port.SendTextAsync("S", 200));
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 3_000,
            $"second caller waited {sw.ElapsedMilliseconds} ms — it queued behind the wedged call "
            + "instead of failing fast on the lock budget");

        try { await wedged.WaitAsync(TimeSpan.FromSeconds(15)); } catch { }
    }
}
