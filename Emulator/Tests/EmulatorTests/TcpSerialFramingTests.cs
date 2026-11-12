using System.Net.Sockets;
using System.Text;
using Protocols.Shared;
using TcpSerialServer;

namespace EmulatorTests;

public class TcpSerialFramingTests
{
    [Fact]
    public async Task CrTerminated_FrameCompletesOnCr()
    {
        const int port = 10030;
        using var server = new TcpSerialProtocolServer("CrTest", "127.0.0.1", port, FramingMode.CrTerminated);

        string? received = null;
        server.RegisterHandler(cmd =>
        {
            received = Encoding.ASCII.GetString(cmd);
            return Encoding.ASCII.GetBytes("OK\r");
        });

        server.Start();
        await Task.Delay(50);

        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port);
        var stream = client.GetStream();

        await stream.WriteAsync(Encoding.ASCII.GetBytes("HELLO\r"));
        await Task.Delay(100);

        byte[] buf = new byte[64];
        int read = await stream.ReadAsync(buf);
        string response = Encoding.ASCII.GetString(buf, 0, read);

        Assert.Equal("HELLO\r", received);
        Assert.Equal("OK\r", response);

        server.Stop();
    }

    [Fact]
    public async Task LfTerminated_FrameCompletesOnLf()
    {
        const int port = 10031;
        using var server = new TcpSerialProtocolServer("LfTest", "127.0.0.1", port, FramingMode.LfTerminated);

        string? received = null;
        server.RegisterHandler(cmd =>
        {
            received = Encoding.ASCII.GetString(cmd);
            return Encoding.ASCII.GetBytes("OK\n");
        });

        server.Start();
        await Task.Delay(50);

        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port);
        var stream = client.GetStream();

        await stream.WriteAsync(Encoding.ASCII.GetBytes("HELLO\n"));
        await Task.Delay(100);

        byte[] buf = new byte[64];
        int read = await stream.ReadAsync(buf);
        string response = Encoding.ASCII.GetString(buf, 0, read);

        Assert.Equal("HELLO\n", received);
        Assert.Equal("OK\n", response);

        server.Stop();
    }

    [Fact]
    public async Task AdditiveSum16_FrameCompletesAt22Bytes()
    {
        const int port = 10032;
        using var server = new TcpSerialProtocolServer("SumTest", "127.0.0.1", port, FramingMode.AdditiveSum16);

        byte[]? received = null;
        server.RegisterHandler(cmd =>
        {
            received = cmd;
            return [0xAA, 0xBB];
        });

        server.Start();
        await Task.Delay(50);

        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port);
        var stream = client.GetStream();

        // Send exactly 22 bytes (20-byte payload + 2-byte additive sum)
        byte[] frame = new byte[22];
        for (int i = 0; i < 22; i++) frame[i] = (byte)(i + 1);
        await stream.WriteAsync(frame);
        await Task.Delay(100);

        byte[] buf = new byte[64];
        int read = await stream.ReadAsync(buf);

        Assert.NotNull(received);
        Assert.Equal(22, received!.Length);
        Assert.Equal(2, read); // 0xAA, 0xBB

        server.Stop();
    }

    [Fact]
    public async Task ModbusRTU_FC03_FrameCompletesAt8Bytes()
    {
        const int port = 10033;
        using var server = new TcpSerialProtocolServer("ModbusTest", "127.0.0.1", port, FramingMode.ModbusRTU);

        byte[]? received = null;
        server.RegisterHandler(cmd =>
        {
            received = cmd;
            return [0x01, 0x03, 0x02, 0x00, 0x64, 0xB8, 0x44]; // Modbus response
        });

        server.Start();
        await Task.Delay(50);

        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port);
        var stream = client.GetStream();

        // Modbus RTU FC03 request: addr=0x01, fn=0x03, reg=0x0000, count=0x0001, CRC=2 bytes = 8 bytes total
        byte[] frame = [0x01, 0x03, 0x00, 0x00, 0x00, 0x01, 0x84, 0x0A];
        await stream.WriteAsync(frame);
        await Task.Delay(100);

        byte[] buf = new byte[64];
        int read = await stream.ReadAsync(buf);

        Assert.NotNull(received);
        Assert.Equal(8, received!.Length);
        Assert.Equal(0x01, received[0]); // addr
        Assert.Equal(0x03, received[1]); // function code
        Assert.True(read > 0);

        server.Stop();
    }
}
