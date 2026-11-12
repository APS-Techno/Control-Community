using System.Net.Sockets;
using EmulatorCore.Models;
using S7Server;
using Sharp7;

namespace EmulatorTests;

public class S7ProtocolServerTests : IDisposable
{
    private const int TestPort = 10102;
    private const string TestAddress = "127.0.0.1";
    private readonly S7ProtocolServer _server;

    public S7ProtocolServerTests()
    {
        _server = new S7ProtocolServer(TestAddress, TestPort, "TestS7");
    }

    public void Dispose()
    {
        _server.Dispose();
    }

    private static S7Client CreateClient()
    {
        var client = new S7Client();
        client.PLCPort = TestPort;
        return client;
    }

    [Fact]
    public async Task Server_AcceptsTcpConnection()
    {
        _server.Start();
        Assert.True(_server.IsRunning);

        using var client = new TcpClient();
        await client.ConnectAsync(TestAddress, TestPort);
        Assert.True(client.Connected);
    }

    [Fact]
    public void Sharp7Client_CanReadDbFloat()
    {
        float testValue = 2.5f;
        byte[] regionData = new byte[4];
        // S7 uses big-endian floats
        var bits = BitConverter.SingleToInt32Bits(testValue);
        regionData[0] = (byte)(bits >> 24);
        regionData[1] = (byte)(bits >> 16);
        regionData[2] = (byte)(bits >> 8);
        regionData[3] = (byte)bits;

        _server.RegisterDbRegion(new DbRegion
        {
            DeviceId = "test-device",
            DbNumber = 1,
            Address = 100,
            Length = 4,
            OnRead = () => regionData,
            OnWrite = _ => { },
        });

        _server.Start();
        _server.Flush(); // Push data into buffer

        var client = CreateClient();
        int result = client.ConnectTo(TestAddress, 0, 0);
        Assert.Equal(0, result); // 0 = success

        try
        {
            byte[] buffer = new byte[4];
            result = client.DBRead(1, 100, 4, buffer);
            Assert.Equal(0, result);

            int raw = (buffer[0] << 24) | (buffer[1] << 16) | (buffer[2] << 8) | buffer[3];
            float readValue = BitConverter.Int32BitsToSingle(raw);
            Assert.Equal(testValue, readValue, precision: 5);
        }
        finally
        {
            client.Disconnect();
        }
    }

    [Fact]
    public void MbBitSharing_TwoDevices_SameByte_DifferentBits()
    {
        // Two valves share MB address 2, using bits 0 and 3
        bool valve1State = true;
        bool valve2State = false;
        bool valve1Written = false;
        bool valve2Written = false;

        _server.RegisterMbBit(new MbBitRegistration
        {
            DeviceId = "valve1",
            Address = 2,
            BitIndex = 0,
            OnRead = () => valve1State,
            OnWrite = v => { valve1State = v; valve1Written = true; },
        });

        _server.RegisterMbBit(new MbBitRegistration
        {
            DeviceId = "valve2",
            Address = 2,
            BitIndex = 3,
            OnRead = () => valve2State,
            OnWrite = v => { valve2State = v; valve2Written = true; },
        });

        _server.Start();
        _server.Flush(); // Rebuild MB buffer from device states

        var client = CreateClient();
        int result = client.ConnectTo(TestAddress, 0, 0);
        Assert.Equal(0, result);

        try
        {
            // Read MB byte 2 - should have bit 0 set (valve1=true) and bit 3 clear (valve2=false)
            byte[] buffer = new byte[1];
            result = client.MBRead(2, 1, buffer);
            Assert.Equal(0, result);
            Assert.Equal(0x01, buffer[0]); // only bit 0 set

            // Now write back with bit 3 also set (valve2=true), keeping bit 0
            buffer[0] = 0x09; // bits 0 and 3
            result = client.MBWrite(2, 1, buffer);
            Assert.Equal(0, result);

            Assert.True(valve1Written);
            Assert.True(valve2Written);
            Assert.True(valve1State);  // bit 0 still true
            Assert.True(valve2State);  // bit 3 now true

            buffer[0] = 0;
            result = client.MBRead(2, 1, buffer);
            Assert.Equal(0, result);
            Assert.Equal(0x09, buffer[0]); // bits 0 and 3
        }
        finally
        {
            client.Disconnect();
        }
    }

    [Fact]
    public void Sharp7Client_CanWriteMbBit()
    {
        bool bitCallbackFired = false;
        bool writtenValue = false;

        _server.RegisterMbBit(new MbBitRegistration
        {
            DeviceId = "test-valve",
            Address = 5,
            BitIndex = 2,
            OnRead = () => false,
            OnWrite = v => { writtenValue = v; bitCallbackFired = true; },
        });

        _server.Start();
        _server.Flush();

        var client = CreateClient();
        int result = client.ConnectTo(TestAddress, 0, 0);
        Assert.Equal(0, result);

        try
        {
            byte[] buffer = [0x04]; // bit 2 = 0b00000100
            result = client.MBWrite(5, 1, buffer);
            Assert.Equal(0, result);

            Assert.True(bitCallbackFired, "MB bit write callback should have fired");
            Assert.True(writtenValue, "Written bit value should be true");
        }
        finally
        {
            client.Disconnect();
        }
    }

    [Fact]
    public void DbWriteThenRead_WithoutFlush_ReturnsWrittenBytes()
    {
        // A command region (SkipFlush) that the controller writes and reads back.
        byte[] backing = new byte[4];
        _server.RegisterDbRegion(new DbRegion
        {
            DeviceId = "cmd-device",
            DbNumber = 1,
            Address = 200,
            Length = 4,
            OnRead = () => backing,
            OnWrite = data => Array.Copy(data, backing, Math.Min(data.Length, backing.Length)),
            SkipFlush = true,
        });

        _server.Start();
        _server.Flush(); // publish the initial snapshot

        var client = CreateClient();
        Assert.Equal(0, client.ConnectTo(TestAddress, 0, 0));
        try
        {
            byte[] write = [0xDE, 0xAD, 0xBE, 0xEF];
            Assert.Equal(0, client.DBWrite(1, 200, 4, write));

            // Read back WITHOUT an intervening Flush — must reflect the write at once.
            byte[] read = new byte[4];
            Assert.Equal(0, client.DBRead(1, 200, 4, read));
            Assert.Equal(write, read);
        }
        finally { client.Disconnect(); }
    }

    [Fact]
    public void DbRead_BeforeFirstFlush_ReturnsZeros()
    {
        _server.RegisterDbRegion(new DbRegion
        {
            DeviceId = "test-device",
            DbNumber = 1,
            Address = 100,
            Length = 4,
            OnRead = () => [0x11, 0x22, 0x33, 0x44],
            OnWrite = _ => { },
        });

        _server.Start(); // NOTE: no Flush() — snapshot not yet published

        var client = CreateClient();
        Assert.Equal(0, client.ConnectTo(TestAddress, 0, 0));
        try
        {
            byte[] read = new byte[4];
            Assert.Equal(0, client.DBRead(1, 100, 4, read));
            Assert.Equal(new byte[4], read); // No snapshot published yet → server returns a zero-filled result by convention.
        }
        finally { client.Disconnect(); }
    }

    [Fact]
    public void DbRead_AfterFlush_ReturnsDeviceState()
    {
        _server.RegisterDbRegion(new DbRegion
        {
            DeviceId = "test-device",
            DbNumber = 1,
            Address = 100,
            Length = 4,
            OnRead = () => [0x11, 0x22, 0x33, 0x44],
            OnWrite = _ => { },
        });

        _server.Start();
        _server.Flush();

        var client = CreateClient();
        Assert.Equal(0, client.ConnectTo(TestAddress, 0, 0));
        try
        {
            byte[] read = new byte[4];
            Assert.Equal(0, client.DBRead(1, 100, 4, read));
            Assert.Equal(new byte[] { 0x11, 0x22, 0x33, 0x44 }, read);
        }
        finally { client.Disconnect(); }
    }

    [Fact]
    public async Task ConcurrentReadsDuringFlush_NeverTear()
    {
        const float testValue = 2.5f;
        byte[] regionData = new byte[4];
        var bits = BitConverter.SingleToInt32Bits(testValue);
        regionData[0] = (byte)(bits >> 24);
        regionData[1] = (byte)(bits >> 16);
        regionData[2] = (byte)(bits >> 8);
        regionData[3] = (byte)bits;

        _server.RegisterDbRegion(new DbRegion
        {
            DeviceId = "const-device",
            DbNumber = 1,
            Address = 100,
            Length = 4,
            OnRead = () => regionData,
            OnWrite = _ => { },
        });

        _server.Start();
        _server.Flush();

        // Hammer Flush() on several background threads for the duration of the reads,
        // widening the window for a read to interleave with a snapshot republish.
        using var stop = new CancellationTokenSource();
        var flushers = Enumerable.Range(0, 3)
            .Select(_ => Task.Run(() =>
            {
                while (!stop.IsCancellationRequested)
                    _server.Flush();
            }))
            .ToArray();

        var client = CreateClient();
        Assert.Equal(0, client.ConnectTo(TestAddress, 0, 0));
        try
        {
            for (int i = 0; i < 200; i++)
            {
                byte[] buffer = new byte[4];
                Assert.Equal(0, client.DBRead(1, 100, 4, buffer));
                int raw = (buffer[0] << 24) | (buffer[1] << 16) | (buffer[2] << 8) | buffer[3];
                // Each read must be a complete, untorn copy of the constant value.
                Assert.Equal(testValue, BitConverter.Int32BitsToSingle(raw), precision: 5);
            }
        }
        finally
        {
            client.Disconnect();
            stop.Cancel();
            // Awaiting surfaces any flusher fault; WaitAsync fails the test if they hang.
            await Task.WhenAll(flushers).WaitAsync(TimeSpan.FromSeconds(2));
        }
    }
}
