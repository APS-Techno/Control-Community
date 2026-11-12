using System.Net.Sockets;
using System.Text;
using Protocols.Shared;
using TcpSerialServer;

namespace EmulatorTests;

public class TcpSerialServerTests : IDisposable
{
    private readonly TcpSerialProtocolServer _server;
    private const int TestPort = 10022;

    public TcpSerialServerTests()
    {
        _server = new TcpSerialProtocolServer("SerialTest", "127.0.0.1", TestPort, FramingMode.CrLfTerminated);
    }

    public void Dispose() => _server.Dispose();

    [Fact]
    public async Task Server_RoutesCommandToHandler()
    {
        string? receivedCmd = null;
        _server.RegisterHandler(cmd =>
        {
            receivedCmd = Encoding.ASCII.GetString(cmd).TrimEnd('\r', '\n');
            return Encoding.ASCII.GetBytes("xxx2.50\r");
        });

        _server.Start();
        await Task.Delay(50);

        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", TestPort);
        var stream = client.GetStream();

        byte[] cmd = Encoding.ASCII.GetBytes("R5\r\n");
        await stream.WriteAsync(cmd);
        await Task.Delay(100);

        byte[] buf = new byte[64];
        int read = await stream.ReadAsync(buf);
        string response = Encoding.ASCII.GetString(buf, 0, read);

        Assert.Equal("R5", receivedCmd);
        Assert.Equal("xxx2.50\r", response);

        _server.Stop();
    }
}
