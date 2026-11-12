using APS.Hardware.S7;
using APS.SystemControl.BaseHWClasses;
using Xunit;

namespace APS.SystemControl.Tests.CoreLogic;

public class S7200ThrottleValveTests
{
    private sealed class FakePLC : IPLCController
    {
        public List<(int mode, int addr, float value)> FloatWrites { get; } = [];
        public float NextReadFloat { get; set; }

        public bool Connected { get; set; } = true;
        public void Connect() { Connected = true; }
        public void Connect(int address) { Connected = true; }
        public void Connect(string address) { Connected = true; }
        public void Connect(string address, int rack, int slot) { Connected = true; }
        public void Disconnect() { Connected = false; }

        public void WriteFloat(int mode, int address, float value)
            => FloatWrites.Add((mode, address, value));

        // Verified write reads back what we just wrote — simulate success.
        public float ReadFloat(int mode, int address)
        {
            if (FloatWrites.Count > 0 && FloatWrites[^1].addr == address)
                return FloatWrites[^1].value;
            return NextReadFloat;
        }

        // Unused — throw so accidental use blows up.
        public bool ReadBool(int mode, int address, int byteIndex) => throw new NotImplementedException();
        public void WriteBool(int mode, int address, int index, bool value) => throw new NotImplementedException();
        public int ReadInt(int mode, int address) => throw new NotImplementedException();
        public void WriteInt(int mode, int address, short value) => throw new NotImplementedException();
        public string ReadString(int mode, int address) => throw new NotImplementedException();
        public void WriteString(int mode, int address, string value) => throw new NotImplementedException();
        public bool[] ReadBoolsBulk(IReadOnlyList<(int mode, int address, int byteIndex)> requests) => throw new NotImplementedException();
        public byte ReadByte(int mode, int address) => throw new NotImplementedException();
        public void WriteByte(int mode, int address, byte value) => throw new NotImplementedException();
    }

    private const int Addr = 200;

    private static (S7200ThrottleValve tv, FakePLC plc) NewTV()
    {
        var plc = new FakePLC();
        var tv = new S7200ThrottleValve(plc, "TV1", Addr, "Test throttle");
        return (tv, plc);
    }

    [Fact]
    public void Open_writes_zero_to_setpoint_register()
    {
        var (tv, plc) = NewTV();
        tv.Command(["Open"]);
        Assert.Contains(plc.FloatWrites, w => w.addr == Addr && w.value == 0f);
    }

    [Fact]
    public void Close_writes_sentinel_to_setpoint_register()
    {
        var (tv, plc) = NewTV();
        tv.Command(["Close"]);
        Assert.Contains(plc.FloatWrites, w => w.addr == Addr && w.value == 9999f);
    }

    [Fact]
    public void SetPressure_writes_target_pressure_in_torr()
    {
        var (tv, plc) = NewTV();
        tv.Command(["SetPressure", 0.005f]); // 5 mTorr
        Assert.Contains(plc.FloatWrites, w => w.addr == Addr && MathF.Abs(w.value - 0.005f) < 1e-6f);
    }

    [Fact]
    public void SetPressure_rejects_NaN()
    {
        var (tv, _) = NewTV();
        Assert.Throws<ArgumentOutOfRangeException>(() => tv.Command(["SetPressure", float.NaN]));
    }

    [Fact]
    public void SetPressure_rejects_negative()
    {
        var (tv, _) = NewTV();
        Assert.Throws<ArgumentOutOfRangeException>(() => tv.Command(["SetPressure", -1f]));
    }

    [Fact]
    public void Stop_after_SetPressure_rewrites_last_setpoint()
    {
        var (tv, plc) = NewTV();
        tv.Command(["SetPressure", 0.005f]);
        plc.FloatWrites.Clear();
        tv.Command(["Stop"]);
        Assert.Contains(plc.FloatWrites, w => w.addr == Addr && MathF.Abs(w.value - 0.005f) < 1e-6f);
    }

    [Fact]
    public void Stop_before_any_setpoint_is_noop()
    {
        var (tv, plc) = NewTV();
        tv.Command(["Stop"]);
        Assert.Empty(plc.FloatWrites);
    }

    [Fact]
    public void SetPoint_verb_is_silently_ignored()
    {
        var (tv, plc) = NewTV();
        tv.Command(["SetPoint", 3]);
        Assert.Empty(plc.FloatWrites);
    }

    [Fact]
    public void SetPointValues_verb_is_silently_ignored()
    {
        var (tv, plc) = NewTV();
        tv.Command(["SetPointValues", 1.0f, 2.0f, 3.0f, 4.0f, 5.0f]);
        Assert.Empty(plc.FloatWrites);
    }

    [Fact]
    public void Z1_verb_is_silently_ignored()
    {
        var (tv, plc) = NewTV();
        tv.Command(["Z1"]);
        Assert.Empty(plc.FloatWrites);
    }

    [Fact]
    public void Measure_reads_position_from_address_plus_four()
    {
        var (tv, plc) = NewTV();
        plc.NextReadFloat = 73.5f;
        // ReadFloat at Addr+4 (no prior write at that address) returns NextReadFloat.
        object[] data = tv.Measure();
        Assert.NotEmpty(data);
        // First element is the position by convention used in our binding layer.
        Assert.Equal(73.5f, Convert.ToSingle(data[0]));
    }
}
