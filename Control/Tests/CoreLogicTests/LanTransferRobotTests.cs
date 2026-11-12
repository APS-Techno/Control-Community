using System.Net;
using System.Net.Sockets;
using APS.Hardware.Demo;
using APS.SystemControl.Core.HWClasses.Base;

namespace CoreLogicTests;

public class LanTransferRobotTests
{
    private static LanTransferRobot Connected(FakeLanRobot srv)
    {
        var robot = new LanTransferRobot("ROBOT", "127.0.0.1", srv.Port, "test")
        {
            Faces = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase) { ["LL"] = 1, ["C1"] = 2 },
        };
        Assert.True(robot.Connect());
        return robot;
    }

    [Fact]
    public void Transfer_SendsFaceNames()
    {
        using var srv = new FakeLanRobot();
        var robot = Connected(srv);
        robot.Transfer(1, 2);
        Assert.Contains("XFER LL,C1", srv.Received);
        Assert.NotEqual(0, robot.LastIssuedCmdId);
        robot.Dispose();
    }

    [Fact]
    public void Transfer_ParkIndexSendsPark()
    {
        using var srv = new FakeLanRobot();
        var robot = Connected(srv);
        robot.Transfer(0, 2);
        Assert.Contains("XFER PARK,C1", srv.Received);
        robot.Dispose();
    }

    [Fact]
    public void Transfer_UnknownIndex_Throws()
    {
        using var srv = new FakeLanRobot();
        var robot = Connected(srv);
        Assert.Throws<ArgumentException>(() => robot.Transfer(1, 9));
        Assert.DoesNotContain(srv.Received, l => l.StartsWith("XFER"));
        robot.Dispose();
    }

    [Fact]
    public void StatTransferring_MapsToRobotStateTransferring()
    {
        using var srv = new FakeLanRobot { Stat = "TRANSFERRING", Loc = "C1" };
        var robot = Connected(srv);
        robot.Poll();
        Assert.Equal(RobotState.Transferring, robot.State);
        Assert.Equal((byte)2, robot.CurrentFace);
        srv.Stat = "IDLE";
        robot.Poll();
        Assert.Equal(RobotState.Idle, robot.State);
        Assert.True(robot.Homed);
        srv.Stat = "ERROR";
        robot.Poll();
        Assert.Equal(RobotState.Error, robot.State);
        Assert.NotEqual(RobotErrorCode.Ok, robot.ErrorCode);
        robot.Dispose();
    }

    [Fact]
    public void PollFault_KeepsLastState()
    {
        using var srv = new FakeLanRobot { Stat = "TRANSFERRING", Loc = "C1" };
        var robot = Connected(srv);
        robot.Poll();
        srv.Stat = "garbled";
        robot.Poll();
        Assert.Equal(RobotState.Transferring, robot.State);
        Assert.NotNull(robot.LastCommFault);
        robot.Dispose();
    }

    [Fact]
    public void Abort_SendsAbor()
    {
        using var srv = new FakeLanRobot();
        var robot = Connected(srv);
        robot.Abort();
        Assert.Contains("ABOR", srv.Received);
        robot.Dispose();
    }

    [Fact]
    public void Home_SendsHome()
    {
        using var srv = new FakeLanRobot();
        var robot = Connected(srv);
        robot.Home();
        Assert.Contains("HOME", srv.Received);
        robot.Dispose();
    }

    [Fact]
    public void Poll_EchoesIssuedCmdId()
    {
        using var srv = new FakeLanRobot();
        var robot = Connected(srv);
        robot.Home();
        robot.Poll();
        Assert.Equal(robot.LastIssuedCmdId, robot.LastCmdId);
        robot.Dispose();
    }

    [Fact]
    public void DeviceRejection_Throws()
    {
        using var srv = new FakeLanRobot { RejectXfer = true };
        var robot = Connected(srv);
        Assert.Throws<InvalidOperationException>(() => robot.Transfer(1, 2));
        robot.Dispose();
    }

    [Fact]
    public void WrongIdentity_ConnectFails()
    {
        using var srv = new FakeLanRobot { Idn = "OTHER,DEVICE,0,1" };
        var robot = new LanTransferRobot("ROBOT", "127.0.0.1", srv.Port, "x");
        Assert.False(robot.Connect());
        Assert.Contains("identity", robot.LastCommFault, StringComparison.OrdinalIgnoreCase);
        robot.Dispose();
    }

    private sealed class FakeLanRobot : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly List<TcpClient> _clients = [];
        private readonly List<string> _received = [];
        private readonly Queue<string> _errors = new();

        public int Port { get; }
        public string Idn { get; init; } = "APS,LANROBOT-100,0,1.0";
        public volatile string Stat = "IDLE";
        public volatile string Loc = "LL";
        public bool RejectXfer { get; init; }
        public string[] Received { get { lock (_received) return [.. _received]; } }

        public FakeLanRobot()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(AcceptLoop);
        }

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
                    if (reply != null) await writer.WriteLineAsync(reply);
                }
            }
            catch (Exception) { }
        }

        private string? Handle(string line)
        {
            switch (line)
            {
                case "*IDN?": return Idn;
                case "STAT?": return Stat;
                case "LOC?": return Loc;
                case "SYST:ERR?": return _errors.Count > 0 ? _errors.Dequeue() : "0,\"No error\"";
                case "HOME":
                case "ABOR":
                    return null;
            }
            if (line.StartsWith("XFER ", StringComparison.Ordinal) && RejectXfer)
                _errors.Enqueue("-222,\"Data out of range\"");
            else if (!line.StartsWith("XFER ", StringComparison.Ordinal))
                _errors.Enqueue("-113,\"Undefined header\"");
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
