using System.Globalization;
using System.Net;
using System.Net.Sockets;
using APS.Hardware.Demo;

namespace CoreLogicTests;

public class LanPowerSupplyTests
{
    private static LanPowerSupply Connected(FakeLanPsu srv, string mode)
    {
        var psu = new LanPowerSupply("PSU", "127.0.0.1", srv.Port, mode, "test");
        Assert.True(psu.Connect());
        return psu;
    }

    [Fact]
    public void Connect_ValidatesIdentityAndMode()
    {
        using var srv = FakeLanPsu.Start(mode: "PDC");
        var psu = new LanPowerSupply("PSU1", "127.0.0.1", srv.Port, "PDC", "Pulsed DC (M1)");
        Assert.True(psu.Connect());
        psu.Dispose();
    }

    [Fact]
    public void Connect_ReadsDeviceSetpoints()
    {
        using var srv = FakeLanPsu.Start(mode: "PDC");
        var psu = new LanPowerSupply("PSU", "127.0.0.1", srv.Port, "PDC", "test");
        Assert.False(psu.TryGetSetpoint("Frequency", out _));
        Assert.True(psu.Connect());
        Assert.True(psu.TryGetSetpoint("Power", out float power));
        Assert.True(psu.TryGetSetpoint("Frequency", out float freq));
        Assert.True(psu.TryGetSetpoint("DutyCycle", out float duty));
        Assert.Equal((0f, 50f, 50f), (power, freq, duty));
        psu.Disconnect();
        Assert.False(psu.TryGetSetpoint("Frequency", out _));
        psu.Dispose();
    }

    [Fact]
    public void Connect_Rf_ReadsOnlyPower()
    {
        using var srv = FakeLanPsu.Start(mode: "RF");
        var psu = Connected(srv, "RF");
        Assert.True(psu.TryGetSetpoint("Power", out _));
        Assert.False(psu.TryGetSetpoint("Frequency", out _));
        psu.Dispose();
    }

    [Fact]
    public void ModeMismatch_ConnectFails()
    {
        using var srv = FakeLanPsu.Start(mode: "PDC");
        var psu = new LanPowerSupply("PSU2", "127.0.0.1", srv.Port, "RF", "RF (M2)");
        Assert.False(psu.Connect());
        Assert.Contains("mode", psu.LastCommFault, StringComparison.OrdinalIgnoreCase);
        psu.Dispose();
    }

    [Fact]
    public void WrongIdentity_ConnectFails()
    {
        using var srv = FakeLanPsu.Start(mode: "PDC", idn: "OTHER,DEVICE,0,1");
        var psu = new LanPowerSupply("PSU", "127.0.0.1", srv.Port, "PDC", "x");
        Assert.False(psu.Connect());
        Assert.Contains("identity", psu.LastCommFault, StringComparison.OrdinalIgnoreCase);
        psu.Dispose();
    }

    [Theory]
    [InlineData("pdc")]
    [InlineData("Rf")]
    [InlineData("dc")]
    public void Mode_IsCaseInsensitive(string mode)
    {
        var psu = new LanPowerSupply("PSU", "127.0.0.1", 1, mode, "x");
        psu.Dispose();
    }

    [Fact]
    public void UnknownMode_ConstructorThrows()
    {
        Assert.Throws<ArgumentException>(() => new LanPowerSupply("PSU", "127.0.0.1", 1, "DC2", "x"));
    }

    [Fact]
    public void SetpointNames_FollowMode()
    {
        var dc = new LanPowerSupply("D", "127.0.0.1", 1, "DC", "x");
        var pdc = new LanPowerSupply("A", "127.0.0.1", 1, "PDC", "x");
        var rf = new LanPowerSupply("B", "127.0.0.1", 1, "RF", "x");
        Assert.Equal(["Power", "Current"], dc.SetpointParameterNames);
        Assert.Equal(["Power", "Current", "Frequency", "DutyCycle"], pdc.SetpointParameterNames);
        Assert.Equal(["Power"], rf.SetpointParameterNames);
        Assert.True(pdc.SupportsPowerSetpoint);
        dc.Dispose();
        pdc.Dispose();
        rf.Dispose();
    }

    [Fact]
    public void CurrentUnit_IsMilliamps()
    {
        var psu = new LanPowerSupply("A", "127.0.0.1", 1, "PDC", "x");
        Assert.Equal("mA", psu.CurrentUnit);
        psu.Dispose();
    }

    [Fact]
    public void Connect_Dc_ReadsPowerAndCurrentOnly()
    {
        using var srv = FakeLanPsu.Start(mode: "DC");
        var psu = Connected(srv, "DC");
        Assert.True(psu.TryGetSetpoint("Power", out _));
        Assert.True(psu.TryGetSetpoint("Current", out float current));
        Assert.Equal(0f, current);
        Assert.False(psu.TryGetSetpoint("Frequency", out _));
        Assert.DoesNotContain(srv.Received, l => l is "FREQ?" or "DUTY?");
        psu.Dispose();
    }

    [Fact]
    public void Current_SendsCurrInMilliamps()
    {
        using var srv = FakeLanPsu.Start(mode: "PDC");
        var psu = Connected(srv, "PDC");
        psu.ApplySetpoint("Current", 50f);
        Assert.Contains("CURR 50", srv.Received);
        Assert.Equal(50f, psu.SetCurrent);
        Assert.True(psu.TryGetSetpoint("Current", out float current));
        Assert.Equal(50f, current);
        psu.Dispose();
    }

    [Fact]
    public void Current_InDcMode_SendsCurr()
    {
        using var srv = FakeLanPsu.Start(mode: "DC");
        var psu = Connected(srv, "DC");
        psu.ApplySetpoint("Current", 300f);
        Assert.Contains("CURR 300", srv.Received);
        psu.Dispose();
    }

    [Fact]
    public void Dc_FrequencyRejected()
    {
        using var srv = FakeLanPsu.Start(mode: "DC");
        var psu = Connected(srv, "DC");
        Assert.Throws<ArgumentException>(() => psu.ApplySetpoint("Frequency", 100));
        Assert.DoesNotContain(srv.Received, l => l.StartsWith("FREQ "));
        psu.Dispose();
    }

    [Fact]
    public void Rf_CurrentRejectedBeforeSend()
    {
        using var srv = FakeLanPsu.Start(mode: "RF");
        var psu = Connected(srv, "RF");
        Assert.Throws<ArgumentException>(() => psu.ApplySetpoint("Current", 50));
        Assert.DoesNotContain(srv.Received, l => l.StartsWith("CURR"));
        psu.Dispose();
    }

    [Fact]
    public void ApplySetpoint_SendsInvariantCommand()
    {
        using var srv = FakeLanPsu.Start(mode: "PDC");
        var psu = Connected(srv, "PDC");
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            psu.ApplySetpoint("Frequency", 100.5f);
        }
        finally { CultureInfo.CurrentCulture = saved; }
        Assert.Contains("FREQ 100.5", srv.Received);
        psu.Dispose();
    }

    [Fact]
    public void ApplySetpoint_PowerUpdatesSetPower()
    {
        using var srv = FakeLanPsu.Start(mode: "PDC");
        var psu = Connected(srv, "PDC");
        psu.ApplySetpoint("Power", 250f);
        Assert.Equal(250f, psu.SetPower);
        psu.Dispose();
    }

    [Fact]
    public void Rf_FrequencyRejected()
    {
        using var srv = FakeLanPsu.Start(mode: "RF");
        var psu = Connected(srv, "RF");
        Assert.Throws<ArgumentException>(() => psu.ApplySetpoint("Frequency", 100));
        Assert.DoesNotContain(srv.Received, l => l.StartsWith("FREQ"));
        psu.Dispose();
    }

    [Fact]
    public void UnknownName_ThrowsBeforeSending()
    {
        using var srv = FakeLanPsu.Start(mode: "PDC");
        var psu = Connected(srv, "PDC");
        int before = srv.Received.Length;
        Assert.Throws<ArgumentException>(() => psu.ApplySetpoint("Voltage", 1));
        Assert.Equal(before, srv.Received.Length);
        psu.Dispose();
    }

    [Fact]
    public void OutOfRange_ApplySetpointThrows()
    {
        using var srv = FakeLanPsu.Start(mode: "PDC", errorAfter: "POW 5000", error: "-222,\"Data out of range\"");
        var psu = Connected(srv, "PDC");
        var ex = Assert.Throws<ArgumentException>(() => psu.ApplySetpoint("Power", 5000));
        Assert.Contains("out of range", ex.Message);
        psu.Dispose();
    }

    [Fact]
    public void HungPeer_OutputOffDoesNotThrow()
    {
        using var srv = FakeLanPsu.Start(mode: "PDC");
        var psu = Connected(srv, "PDC");
        psu.TimeoutMs = 300;
        srv.Silent = true;
        psu.OutputOff();
        Assert.False(psu.IsResponding);
        psu.Dispose();
    }

    [Fact]
    public void ServerGone_OutputOffDoesNotThrow()
    {
        var srv = FakeLanPsu.Start(mode: "PDC");
        var psu = Connected(srv, "PDC");
        srv.Dispose();
        psu.OutputOff();
        Assert.False(psu.IsResponding);
        psu.Dispose();
    }

    [Fact]
    public void ServerGone_ApplySetpointDoesNotThrow()
    {
        var srv = FakeLanPsu.Start(mode: "PDC");
        var psu = Connected(srv, "PDC");
        srv.Dispose();
        psu.ApplySetpoint("Power", 0);
        Assert.False(psu.IsResponding);
        psu.Dispose();
    }

    [Fact]
    public void GetReadback_ReportsRespondingFlagThatDropsWhenTheServerGoes()
    {
        var srv = FakeLanPsu.Start(mode: "PDC");
        var psu = Connected(srv, "PDC");
        psu.ReadFeedback();
        Assert.Equal(true, psu.GetReadback()[4]);

        srv.Dispose();
        psu.ReadFeedback();
        Assert.Equal(false, psu.GetReadback()[4]);
        psu.Dispose();
    }

    [Fact]
    public void OutputOnOff_AndReadFeedback()
    {
        using var srv = FakeLanPsu.Start(mode: "PDC");
        var psu = Connected(srv, "PDC");
        psu.ApplySetpoint("Power", 400f);
        psu.OutputOn();
        psu.ReadFeedback();
        Assert.True(psu.IsResponding);
        Assert.True(psu.IsOutputOn);
        Assert.Equal(400f, psu.Voltage);
        Assert.Equal(1000f, psu.Current);
        Assert.Equal(400f, psu.Power);
        psu.OutputOff();
        psu.ReadFeedback();
        Assert.False(psu.IsOutputOn);
        Assert.Equal(0f, psu.Power);
        psu.Dispose();
    }

    [Fact]
    public void ServerGone_MarksUnresponsive()
    {
        var srv = FakeLanPsu.Start(mode: "PDC");
        var psu = Connected(srv, "PDC");
        srv.Dispose();
        psu.ReadFeedback();
        Assert.False(psu.IsResponding);
        Assert.NotNull(psu.LastCommFault);
        psu.Dispose();
    }

    private sealed class FakeLanPsu : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly List<TcpClient> _clients = [];
        private readonly List<string> _received = [];
        private readonly Queue<string> _errors = new();
        private readonly string _mode, _idn, _errorAfter, _error;
        private bool _on;
        private float _pow, _curr, _freq = 50, _duty = 50;

        public volatile bool Silent;

        public int Port { get; }

        public string[] Received { get { lock (_received) return [.. _received]; } }

        private FakeLanPsu(string mode, string idn, string errorAfter, string error)
        {
            (_mode, _idn, _errorAfter, _error) = (mode, idn, errorAfter, error);
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(AcceptLoop);
        }

        public static FakeLanPsu Start(string mode, string idn = "APS,LANPSU-1000,0,1.0",
            string errorAfter = "", string error = "") => new(mode, idn, errorAfter, error);

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
            if (line == _errorAfter) { _errors.Enqueue(_error); return null; }
            string meas(float v) => (_on ? v : 0f).ToString("R", CultureInfo.InvariantCulture);
            string num(float v) => v.ToString("R", CultureInfo.InvariantCulture);
            switch (line)
            {
                case "*IDN?": return _idn;
                case "SYST:MODE?": return _mode;
                case "OUTP?": return _on ? "1" : "0";
                case "POW?": return num(_pow);
                case "CURR?": return num(_curr);
                case "FREQ?": return num(_freq);
                case "DUTY?": return num(_duty);
                case "MEAS:VOLT?": return meas(400f);
                case "MEAS:CURR?": return meas(1000f * _pow / 400f);
                case "MEAS:POW?": return meas(_pow);
                case "SYST:ERR?": return _errors.Count > 0 ? _errors.Dequeue() : "0,\"No error\"";
                case "OUTP 1": _on = true; return null;
                case "OUTP 0": _on = false; return null;
            }
            int sp = line.IndexOf(' ');
            if (sp < 0 || !float.TryParse(line[(sp + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
            {
                _errors.Enqueue("-113,\"Undefined header\"");
                return null;
            }
            switch (line[..sp])
            {
                case "POW" when v is >= 0 and <= 1000: _pow = v; break;
                case "CURR" when _mode == "RF": _errors.Enqueue("-221,\"Settings conflict\""); break;
                case "FREQ" when _mode != "PDC": _errors.Enqueue("-221,\"Settings conflict\""); break;
                case "DUTY" when _mode != "PDC": _errors.Enqueue("-221,\"Settings conflict\""); break;
                case "CURR" when v is >= 0 and <= 1000: _curr = v; break;
                case "FREQ" when v is >= 1 and <= 350: _freq = v; break;
                case "DUTY" when v is >= 5 and <= 95: _duty = v; break;
                case "POW" or "CURR" or "FREQ" or "DUTY": _errors.Enqueue("-222,\"Data out of range\""); break;
                default: _errors.Enqueue("-113,\"Undefined header\""); break;
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
