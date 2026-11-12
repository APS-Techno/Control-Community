using APS.SystemControl.BaseHWClasses;
using APS.Hardware.S7;
using Xunit;

namespace APS.SystemControl.Tests.CoreLogic;

public class S7200ValveWriteFailedTests
{
    [Fact]
    public void WriteHW_RaisesWriteFailed_WhenVerifyExhausted()
    {
        var fake = new FakePlcController();
        // FakePlcController._dropCount is keyed on (mode, address) and
        // shared between float and bool writes — drop 99 bool writes at MB:30.
        fake.DropNextWrites(((int)Mode.MB, 30), count: 99);

        var valve = new S7200Valve(fake, "V1", address: 30, channel: 1, description: "N2 inlet");
        valve.Connected = true;

        bool? observed = null;
        valve.WriteFailed += target => observed = target;

        valve.Open();   // ProcessCommand(ValveState.Open) → WriteHW(true)
        Assert.True(observed);
    }

    [Fact]
    public void WriteHW_DoesNotRaiseWriteFailed_OnHappyPath()
    {
        var fake = new FakePlcController();
        var valve = new S7200Valve(fake, "V1", address: 30, channel: 1, description: "N2 inlet");
        valve.Connected = true;

        bool fired = false;
        valve.WriteFailed += _ => fired = true;

        valve.Open();
        Assert.False(fired);
        Assert.True(fake.ReadBool((int)Mode.MB, 30, 1));
    }

    [Fact]
    public void WriteHW_RaisesWriteFailed_OnClose_WhenVerifyExhausted()
    {
        var fake = new FakePlcController();
        // Pre-set valve to OPEN so a Close() actually requests a state change.
        fake.WriteBool((int)Mode.MB, 30, 1, true);
        fake.DropNextWrites(((int)Mode.MB, 30), count: 99);

        var valve = new S7200Valve(fake, "V1", address: 30, channel: 1, description: "N2 inlet");
        valve.Connected = true;

        bool? observed = null;
        valve.WriteFailed += target => observed = target;

        valve.Close();   // ProcessCommand(ValveState.Closed) → WriteHW(false)
        Assert.False(observed);   // target was false (CLOSE)
        Assert.NotNull(observed); // event did fire (vs. null = never fired)
    }

    // H-8: a verified-write failure must clear LastWriteVerified so the recipe
    // valve op returns null (abort) instead of a read-back the engine reads as OK.
    [Fact]
    public void LastWriteVerified_False_AfterFailedWrite()
    {
        var fake = new FakePlcController();
        fake.DropNextWrites(((int)Mode.MB, 30), count: 99);
        var valve = new S7200Valve(fake, "V1", address: 30, channel: 1, description: "N2 inlet");
        valve.Connected = true;

        valve.Open();

        Assert.False(valve.LastWriteVerified);
    }

    [Fact]
    public void LastWriteVerified_True_OnHappyPath()
    {
        var fake = new FakePlcController();
        var valve = new S7200Valve(fake, "V1", address: 30, channel: 1, description: "N2 inlet");
        valve.Connected = true;

        valve.Open();

        Assert.True(valve.LastWriteVerified);
    }
}
