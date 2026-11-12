namespace Protocols.Shared;

public enum FramingMode { None, ModbusRTU, AdditiveSum16, CrLfTerminated, CrTerminated, LfTerminated }

public static class FrameParser
{
    public static bool IsFrameComplete(List<byte> buffer, FramingMode mode)
        => IsFrameComplete(buffer, mode, out _);

    /// <summary>
    /// Reports whether <paramref name="buffer"/> currently holds one complete frame.
    /// For <see cref="FramingMode.AdditiveSum16"/> the buffer is also resynchronised:
    /// bytes that cannot belong to a frame are removed from its front and counted in
    /// <paramref name="discarded"/> so the caller can log the desync.
    /// </summary>
    public static bool IsFrameComplete(List<byte> buffer, FramingMode mode, out int discarded)
    {
        discarded = 0;
        if (buffer.Count == 0) return false;
        return mode switch
        {
            FramingMode.CrLfTerminated => buffer.Count >= 2 && buffer[^2] == '\r' && buffer[^1] == '\n',
            FramingMode.CrTerminated => buffer[^1] == '\r' || buffer[^1] == '\n',
            FramingMode.LfTerminated => buffer[^1] == '\n' || buffer[^1] == '\r',
            FramingMode.AdditiveSum16 => IsAdditiveSum16FrameComplete(buffer, out discarded),
            FramingMode.ModbusRTU => IsModbusFrameComplete(buffer),
            FramingMode.None => true,
            _ => false,
        };
    }

    /// <summary>
    /// Removes and returns the first complete frame in <paramref name="buffer"/>, or null
    /// when none is complete yet. Unlike <see cref="IsFrameComplete(List{byte}, FramingMode, out int)"/>
    /// this leaves bytes that belong to the NEXT frame in the buffer, which matters after an
    /// AdditiveSum16 resync where a short frame and the head of the following command can be
    /// buffered together. Protocol servers should use this rather than copying the buffer.
    /// </summary>
    public static byte[]? TakeFrame(List<byte> buffer, FramingMode mode, out int discarded)
    {
        if (!IsFrameComplete(buffer, mode, out discarded)) return null;

        int len = mode == FramingMode.AdditiveSum16 ? buffer[0] : buffer.Count;
        byte[] frame = buffer.GetRange(0, len).ToArray();
        buffer.RemoveRange(0, len);
        return frame;
    }

    // Wire frame (Delphi unit_DC4001.pas templates + TPortControl.CheckSum, mirrored by
    // Control's BaseSerialPort.AppendAdditiveSum16):
    //   [0]        total frame length L (0x16 = 22 for the C8913 command, 0x09 = 9 for the
    //              C8009 read-settings request)
    //   [1]        0xFF - L
    //   [2..L-3]   command id + payload
    //   [L-2..L-1] 16-bit additive sum of bytes [2..L-3], big-endian
    //
    // Like the real supply, hunt for the header on every byte: residue that is not a whole
    // frame (a truncated command, a 9-byte read-settings request, one stray byte) must not
    // stall the emulated supply for the rest of the session.
    private const int MinAdditiveSum16Frame = 4;   // header pair + checksum, nothing else
    private const int MaxAdditiveSum16Frame = 64;

    private static bool IsAdditiveSum16FrameComplete(List<byte> buffer, out int discarded)
    {
        discarded = 0;
        while (buffer.Count > 0)
        {
            int len = buffer[0];
            bool headerOk = len >= MinAdditiveSum16Frame && len <= MaxAdditiveSum16Frame
                            && buffer.Count >= 2 && buffer[1] == (byte)(0xFF - len);

            if (buffer.Count == 1)
            {
                // A lone byte may be a length byte awaiting its complement — keep it only
                // if it could ever start a frame.
                if (len >= MinAdditiveSum16Frame && len <= MaxAdditiveSum16Frame) return false;
                buffer.RemoveAt(0);
                discarded++;
                return false;
            }

            if (!headerOk)
            {
                buffer.RemoveAt(0);
                discarded++;
                continue;
            }

            if (buffer.Count < len) return false;    // header valid, body still arriving

            if (ChecksumMatches(buffer, len)) return true;

            // Valid-looking header but the trailer does not add up: this is residue of an
            // earlier truncated frame whose length byte swallowed the start of the real one.
            // Slide one byte and hunt again.
            buffer.RemoveAt(0);
            discarded++;
        }
        return false;
    }

    private static bool ChecksumMatches(List<byte> buffer, int len)
    {
        ushort sum = 0;
        for (int i = 2; i < len - 2; i++) sum += buffer[i];
        return buffer[len - 2] == (byte)(sum >> 8) && buffer[len - 1] == (byte)(sum & 0xFF);
    }

    private static bool IsModbusFrameComplete(List<byte> buffer)
    {
        if (buffer.Count < 4) return false;
        byte fn = buffer[1];
        int expectedLen = fn switch
        {
            0x03 => 8,
            0x06 => 8,
            _ => buffer.Count,
        };
        return buffer.Count >= expectedLen;
    }
}
