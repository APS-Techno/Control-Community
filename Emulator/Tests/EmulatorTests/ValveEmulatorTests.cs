using APS.Emulator.Devices.S7;
using EmulatorCore.Interfaces;
using EmulatorCore.Models;
using EmulatorTests.Helpers;

namespace EmulatorTests;

public class ValveEmulatorTests
{
    private sealed class CapturingS7Server : IS7ProtocolServer
    {
        public List<MbBitRegistration> MbBits { get; } = [];
        public List<DbRegion> DbRegions { get; } = [];
        public string Name => "test";
        public string PlcId => "";
        public bool IsRunning => false;
        public void Start() { }
        public void Stop() { }
        public void Flush() { }
        public void Dispose() { }
        public void RegisterDbRegion(DbRegion region) => DbRegions.Add(region);
        public void RegisterMbBit(MbBitRegistration bit) => MbBits.Add(bit);
        public void RegisterEbBit(EbBitRegistration bit) { }
    }

    [Fact]
    public void Foreline_opens_on_V15_6_DB_write_and_reflects_on_M2_6()
    {
        // FL: command V15.6 (positive), status served on M2.6.
        var server = new CapturingS7Server();
        var fl = new ValveEmulator("FL", 2, 6, "Fore line", host: "main",
            cmdDbByte: 15, cmdDbBit: 6, cmdInvert: false);
        fl.RegisterProtocol(server);
        var mb = server.MbBits.Single();
        var db = server.DbRegions.Single(r => r.Address == 15);

        db.OnWrite([1 << 6]);                 // V15.6 = 1
        Assert.True(fl.IsOpen);
        Assert.True(mb.OnRead());

        db.OnWrite([0]);                      // V15.6 = 0
        Assert.False(fl.IsOpen);
        Assert.False(mb.OnRead());
    }

    [Fact]
    public void PrePump_closes_on_V5_3_DB_write_inverted_logic()
    {
        // PP: V5.3 = 1 force-closes; clearing it opens (inverted).
        var server = new CapturingS7Server();
        var pp = new ValveEmulator("PP", 2, 4, "Pre-pump", host: "main",
            cmdDbByte: 5, cmdDbBit: 3, cmdInvert: true);
        pp.RegisterProtocol(server);
        var mb = server.MbBits.Single();
        var db = server.DbRegions.Single(r => r.Address == 5);

        db.OnWrite([0]);                      // V5.3 = 0 → released → open
        Assert.True(pp.IsOpen);
        Assert.True(mb.OnRead());

        db.OnWrite([1 << 3]);                 // V5.3 = 1 → force-close
        Assert.False(pp.IsOpen);
        Assert.False(mb.OnRead());
    }

    [Fact]
    public void Command_mode_valve_ignores_direct_M_bit_writes()
    {
        // The M-bit is firmware-owned in command mode: a stray M-write must not
        // move the valve (only the DB command bit does).
        var server = new CapturingS7Server();
        var fl = new ValveEmulator("FL", 2, 6, "Fore line", host: "main",
            cmdDbByte: 15, cmdDbBit: 6, cmdInvert: false);
        fl.RegisterProtocol(server);
        var mb = server.MbBits.Single();

        mb.OnWrite(true);                     // direct M2.6 write — must be inert
        Assert.False(fl.IsOpen);
    }

    [Fact]
    public void Command_DB_region_is_marked_SkipFlush()
    {
        var server = new CapturingS7Server();
        var fl = new ValveEmulator("FL", 2, 6, "Fore line", host: "main",
            cmdDbByte: 15, cmdDbBit: 6);
        fl.RegisterProtocol(server);

        Assert.True(server.DbRegions.Single().SkipFlush);
    }

    [Fact]
    public void Delayed_open_reflects_commanded_bit_on_immediate_readback()
    {
        // Real S7-200: writing the MB command bit reads back set immediately,
        // even though the ladder's T38 timer delays the physical valve open.
        // The emulator must mirror this — otherwise PlcVerifiedWrites loops
        // 5 attempts and fails, and Operations.Valve's 500 ms post-write
        // probe reports "closed" on A1 (which has delay=2).
        var server = new CapturingS7Server();
        var valve = new ValveEmulator("A1", 2, 1, "Air 1", openDelay: 2.0f);
        valve.RegisterProtocol(server);
        var bit = server.MbBits.Single();

        bit.OnWrite(true);

        Assert.True(bit.OnRead());
        Assert.False(valve.IsOpen);
    }

    [Fact]
    public void Delayed_open_completes_after_timer_elapses()
    {
        var server = new CapturingS7Server();
        var valve = new ValveEmulator("A1", 2, 1, "Air 1", openDelay: 2.0f);
        valve.RegisterProtocol(server);
        var bit = server.MbBits.Single();
        var state = TestHelpers.CreateTestState();

        bit.OnWrite(true);
        Assert.False(valve.IsOpen);

        valve.Update(2.0f, state);

        Assert.True(valve.IsOpen);
        Assert.True(bit.OnRead());
    }


    [Fact]
    public void Valve_DefaultState_IsClosed()
    {
        var valve = new ValveEmulator("V1", 0, 0, "Test valve");
        Assert.False(valve.IsOpen);
    }

    [Fact]
    public void Valve_SetOpen_IsOpen()
    {
        var valve = new ValveEmulator("V1", 0, 0, "Test valve");
        valve.SetState(true);
        Assert.True(valve.IsOpen);
    }

    [Fact]
    public void Valve_GetStatus_ReturnsExpectedFields()
    {
        var valve = new ValveEmulator("V1", 0, 0, "Test valve");
        valve.SetState(true);

        var status = valve.GetStatus();
        Assert.Equal("V1", status[0]);
        Assert.Equal(true, status[1]);
        Assert.Equal("Test valve", status[2]);
    }

    [Fact]
    public void Valve_Update_DoesNothing()
    {
        var valve = new ValveEmulator("V1", 0, 0, "Test valve");
        var state = TestHelpers.CreateTestState();

        valve.Update(1.0f, state);
        Assert.False(valve.IsOpen);
    }
}
