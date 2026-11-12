using System.Net;
using System.Net.Sockets;
using APS.Hardware.Demo;

namespace CoreLogicTests;

public class LanRotaryDriveTests
{
    private static LanRotaryDrive Connected(FakeLanDrive srv)
    {
        var drive = new LanRotaryDrive("Holder", "127.0.0.1", srv.Port, "test");
        Assert.True(drive.Connect());
        return drive;
    }

    [Fact]
    public void MoveToFlange_SendsPos()
    {
        using var srv = new FakeLanDrive();
        using var drive = Connected(srv);
        drive.MoveToFlange(3);
        Assert.Contains("POS 3", srv.Received);
        Assert.Equal(3f, drive.TargetPosition);
        Assert.True(drive.IsMoving);
    }

    [Fact]
    public void Home_SendsPosHome()
    {
        using var srv = new FakeLanDrive();
        using var drive = Connected(srv);
        drive.Home();
        Assert.Contains("POS Home", srv.Received);
    }

    [Theory]
    [InlineData("MoveToFlange", 4, "POS 4")]
    [InlineData("MoveTo", 2, "POS 2")]
    [InlineData("Home", null, "POS Home")]
    public void ProcessCommand_ManualMoves_ReachTheWire(string command, object? arg, string expected)
    {
        using var srv = new FakeLanDrive();
        using var drive = Connected(srv);
        drive.ProcessCommand(command, arg is null ? [] : [arg]);
        Assert.Contains(expected, srv.Received);
    }

    [Fact]
    public void Busy_MapsToIsMoving()
    {
        using var srv = new FakeLanDrive();
        using var drive = Connected(srv);
        Assert.True(drive.FlangePositionKnown);

        srv.Position = "3";
        srv.Busy = "1";
        drive.ReadPosition();
        Assert.True(drive.IsMoving);
        Assert.Equal(3f, drive.CurrentPosition);

        srv.Busy = "0";
        drive.ReadPosition();
        Assert.False(drive.IsMoving);

        srv.Position = "Home";
        drive.ReadPosition();
        Assert.Equal(0f, drive.CurrentPosition);
    }

    [Fact]
    public void DeviceRejection_Throws()
    {
        using var srv = new FakeLanDrive { RejectPos = true };
        using var drive = Connected(srv);
        Assert.Throws<ArgumentException>(() => drive.MoveToFlange(9));
        Assert.False(drive.IsMoving);
    }

    private sealed class FakeLanDrive : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly List<TcpClient> _clients = [];
        private readonly List<string> _received = [];
        private readonly Queue<string> _errors = new();

        public volatile string Position = "Home";
        public volatile string Busy = "0";
        public volatile bool RejectPos;

        public FakeLanDrive()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(AcceptLoop);
        }

        public int Port { get; }

        public string[] Received { get { lock (_received) return [.. _received]; } }

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
                case "*IDN?": return "APS,LANDRIVE-100,0,1.0";
                case "POS?": return Position;
                case "BUSY?": return Busy;
                case "SYST:ERR?": return _errors.Count > 0 ? _errors.Dequeue() : "0,\"No error\"";
            }
            if (line.StartsWith("POS ", StringComparison.Ordinal))
            {
                if (RejectPos)
                    _errors.Enqueue("-222,\"Invalid position\"");
                return null;
            }
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
