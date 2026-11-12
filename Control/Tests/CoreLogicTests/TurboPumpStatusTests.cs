using System.Xml.Linq;
using APS.Hardware.S7;
using APS.SystemControl.XML;

namespace APS.SystemControl.Tests.CoreLogic;

public class TurboPumpStatusTests
{
    // S7200TurboPump.ReadStatus() reads ONE byte, the configured status byte,
    // and decodes bits 1..5 of it:
    //   bit1 = power-on / running
    //   bit2 = low-speed running
    //   bit3 = at-speed (normal)
    //   bit4 = at-rotation
    //   bit5 = alarm
    //
    // Status[0] Accelerating = bit1 && bit2 && bit4
    // Status[1] Normal        = bit1 && bit3
    // Status[2] Rotating      = bit1 && bit4
    // Status[3] Alarm         = bit5

    [Theory]
    [InlineData(0b00000000, false, false, false, false)] // all off
    [InlineData(0b00010110, true,  false, true,  false)] // bits 1,2,4 → accel + rotating
    [InlineData(0b00001010, false, true,  false, false)] // bits 1,3   → normal only
    [InlineData(0b00010010, false, false, true,  false)] // bits 1,4   → rotating only
    [InlineData(0b00100000, false, false, false, true )] // bit  5     → alarm only
    [InlineData(0b00111110, true,  true,  true,  true )] // bits 1..5  → everything
    public void ReadStatus_DecodesStatusByteBitsCorrectly(
        int rawStatusByte, bool expectAccel, bool expectNormal,
        bool expectRotating, bool expectAlarm)
    {
        // Use a distinctive non-17 status address so this asserts ReadStatus reads
        // the CONFIGURED address; a regression to a hardcoded 17 makes the fake
        // return 0 here and the test fails.
        const int statusAddr = 33;
        var fake = new FakePlcController();
        fake.WriteByte((int)Mode.DB, statusAddr, (byte)rawStatusByte);
        var pump = new S7200TurboPump(fake, "TP", address: statusAddr,
            writeAddr: 30, writeIndex: 0, description: "Turbo");

        bool[] status = pump.Status;

        Assert.Equal(expectAccel,    status[0]);
        Assert.Equal(expectNormal,   status[1]);
        Assert.Equal(expectRotating, status[2]);
        Assert.Equal(expectAlarm,    status[3]);
    }

    /// <summary>
    /// ReadStatus must read ONE byte: a 2-byte ReadInt packs big-endian (VB17 &lt;&lt; 8 | VB18),
    /// putting the decoded bits 1..5 on VB18 — the auto-load / auto-unload flags
    /// (V18.5 = ZDSL_OUT) — so a load-lock transfer raised a phantom turbo alarm. The adjacent
    /// byte must have no influence on turbo status.
    /// </summary>
    [Fact]
    public void ReadStatus_IgnoresTheAdjacentByte()
    {
        const int statusAddr = 17;
        var fake = new FakePlcController();
        fake.WriteByte((int)Mode.DB, statusAddr, 0x00);      // VB17: pump silent
        fake.WriteByte((int)Mode.DB, statusAddr + 1, 0xFF);  // VB18: every load/unload flag high

        var pump = new S7200TurboPump(fake, "TP", address: statusAddr,
            writeAddr: 30, writeIndex: 0, description: "Turbo");

        Assert.All(pump.Status, bit => Assert.False(bit));
        Assert.False(pump.IsRunning);
    }

    /// <summary>
    /// Guards the read width itself: the driver must issue a single-BYTE read,
    /// not a 2-byte word read. Seeding only the fake's int store leaves the byte
    /// store at zero, so any regression back to ReadInt shows up as a status
    /// pattern appearing out of nowhere.
    /// </summary>
    [Fact]
    public void ReadStatus_UsesAByteRead_NotAWordRead()
    {
        const int statusAddr = 17;
        var fake = new FakePlcController();
        fake.IntByDbAddress[statusAddr] = 0b00111110;   // would light every bit under ReadInt

        var pump = new S7200TurboPump(fake, "TP", address: statusAddr,
            writeAddr: 30, writeIndex: 0, description: "Turbo");

        Assert.All(pump.Status, bit => Assert.False(bit));
        Assert.True(fake.ByteReads > 0, "ReadStatus must read the status register as a byte");
    }

    [Fact]
    public void StatusBitNames_MatchReadStatusOutputOrder()
    {
        var pump = new S7200TurboPump(new FakePlcController(), "TP",
            address: 0, writeAddr: 30, writeIndex: 0, description: "Turbo");
        Assert.Equal(["accel", "normal", "rotating", "alarm"], pump.StatusBitNames);
    }

    [Fact]
    public void StatusBitNames_HasFourEntries_MatchingReadStatusArrayLength()
    {
        var pump = new S7200TurboPump(new FakePlcController(), "TP",
            address: 0, writeAddr: 30, writeIndex: 0, description: "Turbo");
        var pumpStatus = pump.Status;
        Assert.Equal(pump.StatusBitNames.Count, pumpStatus.Length);
    }

    // IsRunning = normal (status[1]) || rotating (status[2]); accel/alarm do not count.
    [Theory]
    [InlineData(0b00000000, false)] // all off
    [InlineData(0b00001010, true )] // bits 1,3 → normal
    [InlineData(0b00010010, true )] // bits 1,4 → rotating
    [InlineData(0b00010110, true )] // bits 1,2,4 → accel + rotating (rotating wins)
    [InlineData(0b00100000, false)] // bit 5 → alarm only, not running
    public void IsRunning_TrueWhenNormalOrRotating(int rawStatusByte, bool expected)
    {
        const int statusAddr = 33;
        var fake = new FakePlcController();
        fake.WriteByte((int)Mode.DB, statusAddr, (byte)rawStatusByte);
        var pump = new S7200TurboPump(fake, "TP", address: statusAddr,
            writeAddr: 30, writeIndex: 0, description: "Turbo");

        Assert.Equal(expected, pump.IsRunning);
    }

    /// <summary>
    /// The raw status byte is surfaced for field diagnostics, so an operator
    /// spin-up can be traced bit-by-bit against the pump's actual behaviour.
    /// </summary>
    [Fact]
    public void LastStatusByte_ExposesTheRawRegisterAsRead()
    {
        const int statusAddr = 17;
        var fake = new FakePlcController();
        fake.WriteByte((int)Mode.DB, statusAddr, 0b00011010);
        var pump = new S7200TurboPump(fake, "TP", address: statusAddr,
            writeAddr: 30, writeIndex: 0, description: "Turbo");

        _ = pump.Status;

        Assert.Equal((byte)0b00011010, pump.LastStatusByte);
    }

    /// <summary>
    /// <see cref="BasePump.LastRunning"/> must agree with <see cref="BasePump.IsRunning"/> for the
    /// same status bits. For the turbo <c>LastStatus[0]</c> is the ACCEL composite
    /// (bit1 &amp;&amp; bit2 &amp;&amp; bit4), true only DURING spin-up and false once at speed.
    /// </summary>
    [Theory]
    [InlineData(0b00000000, false)] // all off
    [InlineData(0b00011010, true )] // bits 1,3,4 → at speed: normal + rotating, accel CLEAR
    [InlineData(0b00010110, true )] // bits 1,2,4 → spinning up: accel + rotating
    [InlineData(0b00001010, true )] // bits 1,3   → normal only
    [InlineData(0b00100000, false)] // bit  5     → alarm only
    public void LastRunning_AgreesWithIsRunning_ForTurboFeedback(int rawStatusByte, bool expected)
    {
        const int statusAddr = 17;
        var fake = new FakePlcController();
        fake.WriteByte((int)Mode.DB, statusAddr, (byte)rawStatusByte);
        var pump = new S7200TurboPump(fake, "TP", address: statusAddr,
            writeAddr: 30, writeIndex: 0, description: "Turbo") { Connected = true };

        pump.Poll();   // fills LastStatus exactly as the background poll does

        Assert.Equal(expected, pump.LastRunning);
        Assert.Equal(pump.IsRunning, pump.LastRunning);
    }

    [Fact]
    public void Pulse_overload_starts_and_stops_via_paired_V_flag_pulses()
    {
        var fake = new FakePlcController();
        // Status byte at DB17, start flag at (0, 2), stop flag at (0, 3).
        var pump = new S7200TurboPump(fake, "TP", address: 17,
            startAddr: 0, startBit: 2, stopAddr: 0, stopBit: 3, description: "Main pump")
        { Connected = true };

        fake.BoolWriteLog.Clear();
        pump.Power(true);   // start → V0.2 high then low
        Assert.Equal(
            new[] { ((int)Mode.DB, 0, 2, true), ((int)Mode.DB, 0, 2, false) },
            fake.BoolWriteLog.FindAll(w => w.mode == (int)Mode.DB));

        fake.BoolWriteLog.Clear();
        pump.Power(false);  // stop → V0.3 high then low
        Assert.Equal(
            new[] { ((int)Mode.DB, 0, 3, true), ((int)Mode.DB, 0, 3, false) },
            fake.BoolWriteLog.FindAll(w => w.mode == (int)Mode.DB));
    }

    [Fact]
    public void Pulse_overload_constructs_via_the_positional_loader()
    {
        // Mirrors the real HWConf TP row; chamber/gate/foreline are metadata and
        // must be filtered out so the ctor receives exactly its 7 positional args.
        var el = new XElement("pump", "TP");
        el.SetAttributeValue("address", "17");
        el.SetAttributeValue("startaddr", "0");
        el.SetAttributeValue("startbit", "2");
        el.SetAttributeValue("stopaddr", "0");
        el.SetAttributeValue("stopbit", "3");
        el.SetAttributeValue("chamber", "Main");
        el.SetAttributeValue("gate", "GVM");
        el.SetAttributeValue("foreline", "FL");
        el.SetAttributeValue("descr", "Main pump");

        object[] attrs = XMLHardware.Attributes(el);
        Assert.Equal(new object[] { "TP", 17, 0, 2, 0, 3, "Main pump" }, attrs);

        var fake = new FakePlcController();
        object[] fullArgs = [fake, .. attrs];
        var pump = (S7200TurboPump)Activator.CreateInstance(typeof(S7200TurboPump), fullArgs)!;
        Assert.NotNull(pump);
    }
}
