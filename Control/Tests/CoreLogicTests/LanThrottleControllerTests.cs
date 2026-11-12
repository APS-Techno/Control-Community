using System.Globalization;
using System.Net;
using System.Net.Sockets;
using APS.Hardware.Demo;

namespace CoreLogicTests;

public class LanThrottleControllerTests
{
    private static LanThrottleController Connected(FakeLanThrottle srv)
    {
        var ctrl = new LanThrottleController("TC1", "127.0.0.1", srv.Port, "test");
        Assert.True(ctrl.Connect());
        return ctrl;
    }

    [Fact]
    public void SetPressure_SendsTarg()
    {
        using var srv = FakeLanThrottle.Start();
        var ctrl = Connected(srv);
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            ctrl.Command(["SetPressure", 0.003f]);
        }
        finally { CultureInfo.CurrentCulture = saved; }
        Assert.Contains("TARG 0.003", srv.Received);
        ctrl.Dispose();
    }

    [Fact]
    public void SetPressure_AtFloor_SendsTargNotPark()
    {
        using var srv = FakeLanThrottle.Start();
        var ctrl = Connected(srv);
        ctrl.Command(["SetPressure", 1e-5f]);
        Assert.Contains("TARG 1E-05", srv.Received);
        Assert.DoesNotContain("PARK", srv.Received);
        ctrl.Dispose();
    }

    [Fact]
    public void SetPressure_Zero_SendsPark()
    {
        using var srv = FakeLanThrottle.Start();
        var ctrl = Connected(srv);
        ctrl.Command(["SetPressure", 0f]);
        Assert.Contains("PARK", srv.Received);
        ctrl.Dispose();
    }

    [Fact]
    public void SetPressure_BelowFloor_Throws()
    {
        using var srv = FakeLanThrottle.Start();
        var ctrl = Connected(srv);
        Assert.Throws<ArgumentException>(() => ctrl.Command(["SetPressure", 1e-6f]));
        ctrl.Dispose();
    }

    [Fact]
    public void SetPressure_Negative_ThrowsBeforeSend()
    {
        using var srv = FakeLanThrottle.Start();
        var ctrl = Connected(srv);
        Assert.Throws<ArgumentOutOfRangeException>(() => ctrl.Command(["SetPressure", -1f]));
        Assert.DoesNotContain(srv.Received, l => l.StartsWith("TARG ") || l == "PARK");
        ctrl.Dispose();
    }

    [Fact]
    public void Open_SendsPark()
    {
        using var srv = FakeLanThrottle.Start();
        var ctrl = Connected(srv);
        ctrl.Command(["Open"]);
        Assert.Contains("PARK", srv.Received);
        ctrl.Dispose();
    }

    [Fact]
    public void Close_SendsTargAtCloseSentinel()
    {
        using var srv = FakeLanThrottle.Start();
        var ctrl = Connected(srv);
        ctrl.Command(["Close"]);
        Assert.Contains("TARG 9999", srv.Received);
        ctrl.Dispose();
    }

    [Fact]
    public void Stop_ResendsLastTarget_AndIsNoOpBeforeAny()
    {
        using var srv = FakeLanThrottle.Start();
        var ctrl = Connected(srv);
        ctrl.Command(["Stop"]);
        Assert.DoesNotContain(srv.Received, l => l.StartsWith("TARG ") || l == "PARK");
        ctrl.Command(["SetPressure", 0.003f]);
        ctrl.Command(["Stop"]);
        Assert.Equal(2, srv.Received.Count(l => l == "TARG 0.003"));
        ctrl.Dispose();
    }

    [Fact]
    public void Floor_ReadOnConnect()
    {
        using var srv = FakeLanThrottle.Start();
        var ctrl = new LanThrottleController("TC1", "127.0.0.1", srv.Port, "test");
        Assert.Equal(0.0, ctrl.GaugeFloorTorr);
        Assert.True(ctrl.Connect());
        Assert.Equal(1e-5, ctrl.GaugeFloorTorr);
        ctrl.Dispose();
    }

    [Fact]
    public void Measure_ReportsMilliTorrAndFillsPollCache()
    {
        using var srv = FakeLanThrottle.Start();
        srv.Pressure = 0.003f;
        srv.Position = 42.5f;
        var ctrl = Connected(srv);
        ctrl.Command(["SetPressure", 0.003f]);

        object[] reading = ctrl.Measure();
        Assert.Equal("Data", reading[0]);
        Assert.InRange((float)reading[1], 2.999f, 3.001f);
        Assert.Equal(42.5f, (float)reading[2]);
        Assert.InRange((float)reading[3], 2.999f, 3.001f);

        ctrl.Poll();
        Assert.Equal(0.003f, ctrl.PressureTorr);
        Assert.Equal(42.5f, ctrl.ValvePositionPercent);
        Assert.Equal(4, ctrl.LastMeasurement.Length);
        ctrl.Dispose();
    }

    [Fact]
    public void WrongIdentity_ConnectFails()
    {
        using var srv = FakeLanThrottle.Start(idn: "OTHER,DEVICE,0,1");
        var ctrl = new LanThrottleController("TC1", "127.0.0.1", srv.Port, "x");
        Assert.False(ctrl.Connect());
        Assert.False(ctrl.Connected);
        Assert.Contains("identity", ctrl.LastCommFault, StringComparison.OrdinalIgnoreCase);
        ctrl.Dispose();
    }

    [Fact]
    public void SilentServer_MarksNotResponding_StaysPolled_AndRecovers()
    {
        using var srv = FakeLanThrottle.Start();
        var ctrl = Connected(srv);
        ctrl.TimeoutMs = 300;
        srv.Silent = true;
        Assert.Empty(ctrl.Measure());
        Assert.False(ctrl.IsResponding);
        Assert.True(ctrl.Connected);
        srv.Silent = false;
        Assert.NotEmpty(ctrl.Measure());
        Assert.True(ctrl.IsResponding);
        ctrl.Dispose();
    }

    [Theory]
    [InlineData("Open")]
    [InlineData("SetPressure")]
    public void ServerGone_CommandThrowsIOException(string verb)
    {
        var srv = FakeLanThrottle.Start();
        var ctrl = Connected(srv);
        ctrl.TimeoutMs = 300;
        srv.Dispose();
        object[] args = verb == "Open" ? ["Open"] : ["SetPressure", 0.003f];
        Assert.ThrowsAny<IOException>(() => ctrl.Command(args));
        ctrl.Dispose();
    }

    internal sealed class FakeLanThrottle : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly List<TcpClient> _clients = [];
        private readonly List<string> _received = [];
        private readonly Queue<string> _errors = new();
        private readonly string _idn;
        private float _target;

        public volatile bool Silent;
        public float Pressure;
        public float Position = 100f;

        public int Port { get; }

        public string[] Received { get { lock (_received) return [.. _received]; } }

        private FakeLanThrottle(string idn)
        {
            _idn = idn;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(AcceptLoop);
        }

        public static FakeLanThrottle Start(string idn = "APS,LANTHROT-100,0,1.0") => new(idn);

        private async Task AcceptLoop()
        {
            try
            {
                while (true)
                {
                    var client = await _listener.AcceptTcpClientAsync();
                    lock (_clients) _clients.Add(client);
                    _ = Task.Run(() => Serve(client));
                }
            }
            catch (Exception) { }
        }

        private async Task Serve(TcpClient client)
        {
            try
            {
                var stream = client.GetStream();
                using var reader = new StreamReader(stream);
                using var writer = new StreamWriter(stream) { NewLine = "\n", AutoFlush = true };
                while (await reader.ReadLineAsync() is { } line)
                {
                    lock (_received) _received.Add(line);
                    string? reply;
                    lock (_errors) reply = Handle(line);
                    if (reply != null && !Silent) await writer.WriteLineAsync(reply);
                }
            }
            catch (Exception) { }
        }

        private string? Handle(string line)
        {
            string num(float v) => v.ToString("R", CultureInfo.InvariantCulture);
            switch (line)
            {
                case "*IDN?": return _idn;
                case "FLOOR?": return "1e-05";
                case "PRES?": return num(Pressure);
                case "POS?": return num(Position);
                case "TARG?": return num(_target);
                case "SYST:ERR?": return _errors.Count > 0 ? _errors.Dequeue() : "0,\"No error\"";
                case "PARK": _target = 0f; return null;
            }
            if (line.StartsWith("TARG ", StringComparison.Ordinal) &&
                float.TryParse(line[5..], NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
            {
                if (v > 0f && v < 1e-5f) _errors.Enqueue("-222,\"Data out of range\"");
                else _target = v;
            }
            else
            {
                _errors.Enqueue("-113,\"Undefined header\"");
            }
            return null;
        }

        public void Dispose()
        {
            _listener.Stop();
            lock (_clients)
                foreach (var c in _clients) c.Dispose();
        }
    }
}
