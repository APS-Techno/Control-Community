using APS.SystemControl.BaseHWClasses;

namespace APS.SystemControl.Tests.CoreLogic;

/// <summary>
/// In-memory IPLCController test double. Each register address can be
/// configured to: accept writes (default), drop the next N writes, or
/// echo a fixed wrong value on read. Captures call counts.
/// </summary>
public sealed class FakePlcController : IPLCController
{
    private readonly Dictionary<(int mode, int addr), float> _floats = new();
    private readonly Dictionary<(int mode, int addr, int idx), bool> _bools = new();
    private readonly Dictionary<(int mode, int addr), byte> _bytes = new();
    // Bool drops share the (mode,addr) key with float drops: S7200Valve
    // writes bools into the same address space.
    private readonly Dictionary<(int mode, int addr), int> _dropCount = new();
    private readonly Dictionary<(int mode, int addr), float> _fixedFloatReadback = new();

    public bool Connected { get; set; } = true;
    public int FloatWrites { get; private set; }
    public int BoolWrites { get; private set; }
    public int ByteWrites { get; private set; }
    public int FloatReads { get; private set; }
    public int BoolReads { get; private set; }
    public int ByteReads { get; private set; }
    public int BoolBulkReads { get; private set; }
    public int BoolBulkBitsRead { get; private set; }    // sum of bits returned across all bulk calls

    /// <summary>
    /// Forces the next <paramref name="count"/> writes (float OR bool) at the
    /// given (mode, address) pair to silently drop — i.e., increment the call
    /// counter but skip the backing-store update. Used to simulate dropped PLC
    /// writes for verify-with-retry tests. The shared key covers both
    /// <see cref="WriteFloat"/> and <see cref="WriteBool"/> at the same address.
    /// </summary>
    public void DropNextWrites((int mode, int addr) target, int count)
        => _dropCount[target] = count;

    /// <summary>
    /// Force <see cref="ReadFloat"/> at a given address to always return
    /// <paramref name="value"/> regardless of subsequent writes — used to
    /// simulate PLC ADC rounding drift in tolerance tests.
    /// </summary>
    public void FixedFloatReadback((int mode, int addr) target, float value)
        => _fixedFloatReadback[target] = value;

    // Number of upcoming reads (float or bool) that should throw
    // PlcCommException — simulates a non-zero Sharp7 result code so we can
    // assert the verified-write retry loop tolerates transient comm failures.
    private int _throwReadCount;

    public void ThrowCommOnNextReads(int count) => _throwReadCount = count;

    private void MaybeThrowComm()
    {
        if (_throwReadCount > 0)
        {
            _throwReadCount--;
            throw new PlcCommException(0x1, "test read");
        }
    }

    public void Connect() { }
    public void Connect(string address) { }
    public void Connect(int address) => throw new NotSupportedException();
    public void Connect(string address, int rack, int slot) => throw new NotSupportedException();
    public void Disconnect() { Connected = false; }

    public float ReadFloat(int mode, int address)
    {
        MaybeThrowComm();
        FloatReads++;
        if (_fixedFloatReadback.TryGetValue((mode, address), out var fixedVal))
            return fixedVal;
        return _floats.TryGetValue((mode, address), out var v) ? v : 0f;
    }

    public bool ReadBool(int mode, int address, int byteIndex)
    {
        MaybeThrowComm();
        BoolReads++;
        return _bools.TryGetValue((mode, address, byteIndex), out var v) && v;
    }

    /// <summary>
    /// DB-int values keyed by address. Lets turbo-pump tests verify that
    /// ReadStatus reads its CONFIGURED status address rather than a hardcoded 17.
    /// </summary>
    public Dictionary<int, int> IntByDbAddress { get; } = new();

    public int ReadInt(int mode, int address)
    {
        // DB = 0; address is the configured status register.
        if (mode == 0 && IntByDbAddress.TryGetValue(address, out int v)) return v;
        // Fall back to the BYTE store, composed big-endian exactly as the real
        // driver does (Sharp7.GetIntAt = b[0] << 8 | b[1]). Real PLC memory is a
        // single flat address space: a word read at N spans bytes N and N+1.
        // Modelling that here lets a test catch a driver reading a word where
        // it meant to read a byte.
        byte hi = _bytes.TryGetValue((mode, address), out var h) ? h : (byte)0;
        byte lo = _bytes.TryGetValue((mode, address + 1), out var l) ? l : (byte)0;
        return hi << 8 | lo;
    }
    public string ReadString(int mode, int address) => "";

    public void WriteFloat(int mode, int address, float value)
    {
        FloatWrites++;
        var key = (mode, address);
        if (_dropCount.TryGetValue(key, out var n) && n > 0)
        {
            _dropCount[key] = n - 1;
            return;   // simulate dropped write
        }
        _floats[key] = value;
    }

    /// <summary>Ordered log of every bool write (mode, addr, bit, value) — lets
    /// tests assert command pulse sequences (e.g. a flag driven high then low).</summary>
    public List<(int mode, int addr, int idx, bool value)> BoolWriteLog { get; } = new();

    public void WriteBool(int mode, int address, int index, bool value)
    {
        BoolWrites++;
        BoolWriteLog.Add((mode, address, index, value));
        var key = (mode, address, index);
        var dropKey = (mode, address);
        if (_dropCount.TryGetValue(dropKey, out var n) && n > 0)
        {
            _dropCount[dropKey] = n - 1;
            return;
        }
        _bools[key] = value;
    }

    public byte ReadByte(int mode, int address)
    {
        ByteReads++;
        return _bytes.TryGetValue((mode, address), out var v) ? v : (byte)0;
    }

    public void WriteByte(int mode, int address, byte value)
    {
        ByteWrites++;
        _bytes[(mode, address)] = value;
    }

    public void WriteInt(int mode, int address, short value) { }
    public void WriteString(int mode, int address, string value) { }

    public bool[] ReadBoolsBulk(IReadOnlyList<(int mode, int address, int byteIndex)> requests)
    {
        BoolBulkReads++;
        BoolBulkBitsRead += requests.Count;
        var result = new bool[requests.Count];
        for (int i = 0; i < requests.Count; i++)
        {
            var key = (requests[i].mode, requests[i].address, requests[i].byteIndex);
            result[i] = _bools.TryGetValue(key, out var v) && v;
        }
        return result;
    }
}
