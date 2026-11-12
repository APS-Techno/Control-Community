namespace APS.SystemControl.BaseHWClasses;

public interface IPLCController
{
    bool Connected { get; }

    #region Connect
    abstract void Connect();
    abstract void Connect(int Address);
    abstract void Connect(string Address);
    abstract void Connect(string Address, int Rack, int Slot);
    abstract void Disconnect();
    #endregion

    #region[Read/Write]
    abstract void WriteFloat(int mode, int address, float value);
    abstract float ReadFloat(int mode, int address);

    abstract void WriteInt(int mode, int address, short value);
    abstract int ReadInt(int mode, int address);

    abstract void WriteBool(int mode, int address, int index, bool value);
    abstract bool ReadBool(int mode, int address, int index);

    abstract void WriteString(int mode, int address, string value);
    abstract string ReadString(int mode, int address);

    /// <summary>
    /// Bulk-reads multiple bool addresses in a single PLC round-trip.
    /// Each request is (mode, address, byteIndex). Returns bool[] in input
    /// order. Implementations may dedupe unique (mode, address) pairs and
    /// decode bits client-side. Mirrors Delphi <c>TPLC7200.CheckValves</c>'
    /// <c>FClient.ReadMultiVars</c> path.
    /// </summary>
    abstract bool[] ReadBoolsBulk(IReadOnlyList<(int mode, int address, int byteIndex)> requests);

    abstract byte ReadByte(int mode, int address);

    abstract void WriteByte(int mode, int address, byte value);

    /// <summary>
    /// Writes a contiguous byte block in ONE PLC round-trip. Multi-byte device
    /// words (e.g. the robot's 4-byte command word) MUST use this rather than
    /// per-byte writes: the per-byte path releases the PLC lock between bytes,
    /// so a concurrent writer can interleave and the ladder latches a hybrid
    /// word (code audit 2026-08-29, C42).
    /// </summary>
    void WriteBytes(int mode, int address, byte[] data)
    {
        // Default per-byte fallback so simple implementations (test fakes)
        // keep compiling — real controllers MUST override with a true block
        // write, or the atomicity guarantee above does not hold.
        for (int i = 0; i < data.Length; i++)
            WriteByte(mode, address + i, data[i]);
    }

    /// <summary>
    /// Reads a contiguous byte block in ONE PLC round-trip, so all bytes come
    /// from the same wire moment instead of pairing values from different
    /// ladder scans (code audit 2026-08-29, C41).
    /// </summary>
    byte[] ReadBytes(int mode, int address, int count)
    {
        // Default per-byte fallback — real controllers MUST override so the
        // block genuinely comes from one wire moment.
        byte[] buffer = new byte[count];
        for (int i = 0; i < count; i++)
            buffer[i] = ReadByte(mode, address + i);
        return buffer;
    }
    #endregion
}
