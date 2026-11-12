using System;
using System.Collections.Generic;
using APS.SystemControl.BaseHWClasses;
using APS.Hardware.S7;
using Xunit;

namespace APS.SystemControl.Tests.CoreLogic;

/// <summary>
/// Phantom-success PLC writes:
///   C22 — a non-zero Sharp7 result code on a write must throw PlcCommException,
///         otherwise a rejected or dropped write is indistinguishable from success.
///   C23 — S7200GasValve writes must use the write+readback loop; an unverified,
///         dropped Ar/N2 inlet command logs as recipe step success.
///   C24 — the CLEARING write of a momentary pulse must be retried, or a transient
///         comm fault leaves the V-memory command flag latched high.
/// </summary>
public class PlcWriteVerificationTests
{
    private sealed class FakePlc : IPLCController
    {
        public readonly List<(int mode, int address, int index, bool value)> BoolWrites = new();
        public Func<int, bool>? WriteBoolBehavior;   // arg: 1-based call number; return ignored, throw to fail
        public Func<int, int, int, bool>? ReadBoolBehavior;

        public bool Connected => true;
        public void Connect() { }
        public void Connect(int Address) { }
        public void Connect(string Address) { }
        public void Connect(string Address, int Rack, int Slot) { }
        public void Disconnect() { }
        public void WriteFloat(int mode, int address, float value) { }
        public float ReadFloat(int mode, int address) => 0f;
        public void WriteInt(int mode, int address, short value) { }
        public int ReadInt(int mode, int address) => 0;
        public void WriteBool(int mode, int address, int index, bool value)
        {
            BoolWrites.Add((mode, address, index, value));
            WriteBoolBehavior?.Invoke(BoolWrites.Count);
        }
        public bool ReadBool(int mode, int address, int index) =>
            ReadBoolBehavior?.Invoke(mode, address, index) ?? false;
        public void WriteString(int mode, int address, string value) { }
        public string ReadString(int mode, int address) => "";
        public bool[] ReadBoolsBulk(IReadOnlyList<(int mode, int address, int byteIndex)> requests) =>
            new bool[requests.Count];
        public byte ReadByte(int mode, int address) => 0;
        public void WriteByte(int mode, int address, byte value) { }
        public void WriteBytes(int mode, int address, byte[] data) { }
        public byte[] ReadBytes(int mode, int address, int count) => new byte[count];
    }

    [Fact]
    public void Write_result_code_zero_does_not_throw()
        => S7200PLC.ThrowOnCommError(0, "write test");

    [Fact]
    public void Write_result_code_nonzero_throws_PlcCommException()
    {
        var ex = Assert.Throws<PlcCommException>(() => S7200PLC.ThrowOnCommError(0x40, "write mode=1 addr=2"));
        Assert.Contains("write mode=1 addr=2", ex.Message);
    }

    [Fact]
    public void GasValve_verified_write_sets_the_bit_and_keeps_LastWriteVerified()
    {
        var plc = new FakePlc();
        bool state = false;
        plc.ReadBoolBehavior = (_, _, _) => state;
        plc.WriteBoolBehavior = n => state = plc.BoolWrites[n - 1].value;

        var v = new S7200GasValve(plc, "V1", 10, 2, "N2 inlet");
        v.Open();

        Assert.True(v.LastWriteVerified);
        Assert.Contains(plc.BoolWrites, w => w is { mode: (int)Mode.MB, address: 10, index: 2, value: true });
    }

    [Fact]
    public void GasValve_write_that_never_reads_back_clears_LastWriteVerified()
    {
        var plc = new FakePlc();
        plc.ReadBoolBehavior = (_, _, _) => false;

        var v = new S7200GasValve(plc, "V1", 10, 2, "N2 inlet");
        v.Open();

        Assert.False(v.LastWriteVerified);
    }

    [Fact]
    public void Pulse_retries_the_clearing_write_after_a_comm_fault()
    {
        var plc = new FakePlc();
        plc.WriteBoolBehavior = n => n == 2
            ? throw new PlcCommException(0x40, "transient fault on clear")
            : true;

        PlcPulse.Pulse(plc, (int)Mode.DB, 5, 3, pulseMs: 1);

        // set(true), clear-attempt (throws), clear-retry (succeeds)
        Assert.Equal(3, plc.BoolWrites.Count);
        Assert.True(plc.BoolWrites[0].value);
        Assert.False(plc.BoolWrites[1].value);
        Assert.False(plc.BoolWrites[2].value);
    }

    [Fact]
    public void Pulse_propagates_when_the_clear_fails_twice()
    {
        var plc = new FakePlc();
        plc.WriteBoolBehavior = n => n >= 2
            ? throw new PlcCommException(0x40, "persistent fault on clear")
            : true;

        Assert.Throws<PlcCommException>(() => PlcPulse.Pulse(plc, (int)Mode.DB, 5, 3, pulseMs: 1));
        Assert.Equal(3, plc.BoolWrites.Count);   // set + two clear attempts
    }

    [Fact]
    public void Pulse_happy_path_sets_then_clears()
    {
        var plc = new FakePlc();

        PlcPulse.Pulse(plc, (int)Mode.DB, 5, 3, pulseMs: 1);

        Assert.Equal(2, plc.BoolWrites.Count);
        Assert.Equal((int)Mode.DB, plc.BoolWrites[0].mode);
        Assert.Equal((5, 3, true), (plc.BoolWrites[0].address, plc.BoolWrites[0].index, plc.BoolWrites[0].value));
        Assert.Equal((5, 3, false), (plc.BoolWrites[1].address, plc.BoolWrites[1].index, plc.BoolWrites[1].value));
    }
}
