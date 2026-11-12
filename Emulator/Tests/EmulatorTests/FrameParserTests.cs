using Protocols.Shared;

namespace EmulatorTests;

public class FrameParserTests
{
    [Fact]
    public void CrLfTerminated_CompletesOnCrLf()
    {
        var buf = new List<byte>("HELLO\r\n"u8.ToArray());
        Assert.True(FrameParser.IsFrameComplete(buf, FramingMode.CrLfTerminated));
    }

    [Fact]
    public void CrLfTerminated_IncompleteWithoutLf()
    {
        var buf = new List<byte>("HELLO\r"u8.ToArray());
        Assert.False(FrameParser.IsFrameComplete(buf, FramingMode.CrLfTerminated));
    }

    [Fact]
    public void CrTerminated_CompletesOnCr()
    {
        var buf = new List<byte>("HELLO\r"u8.ToArray());
        Assert.True(FrameParser.IsFrameComplete(buf, FramingMode.CrTerminated));
    }

    [Fact]
    public void LfTerminated_CompletesOnLf()
    {
        var buf = new List<byte>("HELLO\n"u8.ToArray());
        Assert.True(FrameParser.IsFrameComplete(buf, FramingMode.LfTerminated));
    }

    // Wire frame: [len][0xFF-len] ... [sumHi][sumLo], sum = 16-bit additive sum of
    // bytes [2..len-3] (Delphi TPortControl.CheckSum / BaseSerialPort.AppendAdditiveSum16).
    // C8913 command = 22 bytes (0x16/0xE9), C8009 read-settings = 9 bytes (0x09/0xF6).

    private static byte[] WithAdditiveSum(byte[] payload)
    {
        ushort sum = 0;
        for (int i = 2; i < payload.Length; i++) sum += payload[i];
        return [.. payload, (byte)(sum >> 8), (byte)(sum & 0xFF)];
    }

    /// <summary>The exact C8913 frame a Control driver sends: Uset=800, Iset=50, Pset=800, ctrl=0x89.</summary>
    private static byte[] SumFrameCommand() => WithAdditiveSum(
    [
        0x16, 0xE9, 0x89, 0x13, 0xFF,
        0x00, 0x00, 0x48, 0x44,
        0x00, 0x00, 0x48, 0x42,
        0x00, 0x00, 0x48, 0x44,
        0x14, 0x0A, 0x89,
    ]);

    private static byte[] SumFrameReadSettings() => WithAdditiveSum([0x09, 0xF6, 0x80, 0x09, 0xFF, 0x01, 0x01]);

    /// <summary>Feeds bytes one at a time the way VirtualSerialPortAdapter does; returns the
    /// buffer contents at the moment the parser first reports a complete frame, or null.</summary>
    private static byte[]? FeedUntilComplete(List<byte> buf, IEnumerable<byte> bytes)
    {
        foreach (byte b in bytes)
        {
            buf.Add(b);
            if (FrameParser.IsFrameComplete(buf, FramingMode.AdditiveSum16))
                return [.. buf];
        }
        return null;
    }

    [Fact]
    public void AdditiveSum16_CompletesAt22Bytes()
    {
        var buf = new List<byte>();
        byte[]? frame = FeedUntilComplete(buf, SumFrameCommand());
        Assert.Equal(SumFrameCommand(), frame);
    }

    [Fact]
    public void AdditiveSum16_IncompleteAt21Bytes()
    {
        // A 21-byte (1-byte XOR) frame must NOT be treated as complete.
        var buf = new List<byte>();
        Assert.Null(FeedUntilComplete(buf, SumFrameCommand().Take(21)));
    }

    [Fact]
    public void AdditiveSum16_ReadSettingsFrame_CompletesAt9Bytes()
    {
        // Byte 0 carries the frame length, so the short C8009 request must not be
        // left in the buffer to poison every later 22-byte command.
        var buf = new List<byte>();
        byte[]? frame = FeedUntilComplete(buf, SumFrameReadSettings());
        Assert.Equal(SumFrameReadSettings(), frame);
    }

    [Fact]
    public void AdditiveSum16_TwentyTwoZeroBytes_IsNotAFrame()
    {
        // A real frame needs the length/complement header.
        var buf = new List<byte>();
        Assert.Null(FeedUntilComplete(buf, new byte[22]));
    }

    [Fact]
    public void AdditiveSum16_ResyncsAfterStrayPartialFrame()
    {
        // Field failure: a truncated command (here the first 10 bytes of one, a VALID header)
        // left in the buffer silenced the supply for the rest of the session. The parser
        // must discard the residue and deliver the next whole command intact.
        var buf = new List<byte>();
        byte[] stray = SumFrameCommand().Take(10).ToArray();
        byte[]? frame = FeedUntilComplete(buf, [.. stray, .. SumFrameCommand()]);
        Assert.Equal(SumFrameCommand(), frame);
    }

    [Fact]
    public void AdditiveSum16_ResyncsAfterStrayShortFrame()
    {
        // Residue whose length byte points past the real command start: the 9-byte
        // read-settings request cut to 5 bytes, then a whole command.
        var buf = new List<byte>();
        byte[] stray = SumFrameReadSettings().Take(5).ToArray();
        byte[]? frame = FeedUntilComplete(buf, [.. stray, .. SumFrameCommand()]);
        Assert.Equal(SumFrameCommand(), frame);
    }

    [Fact]
    public void AdditiveSum16_GarbageWithoutHeader_IsDiscardedNotAccumulated()
    {
        // Bytes that can never start a frame must not pile up: the buffer keeps at most the
        // trailing byte (a possible length byte awaiting its complement).
        var buf = new List<byte>();
        Assert.Null(FeedUntilComplete(buf, Enumerable.Repeat((byte)0x55, 100)));
        Assert.True(buf.Count <= 1, $"buffer kept {buf.Count} garbage bytes");
    }

    [Fact]
    public void AdditiveSum16_TakeFrame_ReturnsOneFrameAndKeepsTheRest()
    {
        // After a resync the buffer can already hold a whole short frame plus the head of
        // the next command. The adapter must receive exactly the frame, not the buffer.
        var buf = new List<byte>();
        byte[] stray = SumFrameCommand().Take(3).ToArray();          // valid 22-byte header, cut short
        byte[] next = SumFrameCommand();
        byte[] stream = [.. stray, .. SumFrameReadSettings(), .. next.Take(10)];

        byte[]? frame = null;
        int discardedTotal = 0;
        foreach (byte b in stream)
        {
            buf.Add(b);
            frame = FrameParser.TakeFrame(buf, FramingMode.AdditiveSum16, out int discarded);
            discardedTotal += discarded;
            if (frame != null) break;
        }

        Assert.Equal(SumFrameReadSettings(), frame);
        Assert.Equal(3, discardedTotal);
        // The 10 bytes of the following command stay queued for the next TakeFrame calls.
        byte[]? tail = null;
        foreach (byte b in next.Skip(10))
        {
            buf.Add(b);
            tail = FrameParser.TakeFrame(buf, FramingMode.AdditiveSum16, out _);
            if (tail != null) break;
        }
        Assert.Equal(SumFrameCommand(), tail);
        Assert.Empty(buf);
    }

    [Fact]
    public void AdditiveSum16_BadChecksum_IsRejectedAndSkipped()
    {
        // A frame with a corrupted trailer must not be delivered; the parser slides past it
        // and still finds the good frame that follows.
        byte[] bad = SumFrameCommand();
        bad[^1] ^= 0xFF;
        var buf = new List<byte>();
        byte[]? frame = FeedUntilComplete(buf, [.. bad, .. SumFrameCommand()]);
        Assert.Equal(SumFrameCommand(), frame);
    }

    [Fact]
    public void ModbusRTU_FC03_CompletesAt8Bytes()
    {
        var buf = new List<byte>([0x01, 0x03, 0x00, 0x00, 0x00, 0x01, 0x84, 0x0A]);
        Assert.True(FrameParser.IsFrameComplete(buf, FramingMode.ModbusRTU));
    }

    [Fact]
    public void ModbusRTU_FC06_CompletesAt8Bytes()
    {
        var buf = new List<byte>([0x01, 0x06, 0x00, 0x01, 0x00, 0x03, 0x00, 0x00]);
        Assert.True(FrameParser.IsFrameComplete(buf, FramingMode.ModbusRTU));
    }

    [Fact]
    public void ModbusRTU_IncompleteAt3Bytes()
    {
        var buf = new List<byte>([0x01, 0x03, 0x00]);
        Assert.False(FrameParser.IsFrameComplete(buf, FramingMode.ModbusRTU));
    }

    [Fact]
    public void EmptyBuffer_ReturnsFalse()
    {
        var buf = new List<byte>();
        Assert.False(FrameParser.IsFrameComplete(buf, FramingMode.CrLfTerminated));
    }

    [Fact]
    public void None_AlwaysComplete()
    {
        var buf = new List<byte>([0x42]);
        Assert.True(FrameParser.IsFrameComplete(buf, FramingMode.None));
    }
}
