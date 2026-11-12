using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.HWClasses.Base;
using APS.SystemControl.Tests.CoreLogic;
using Xunit;

namespace CoreLogicTests;

public class PumpIsRunningTests
{
    private sealed class TestPump : BasePump
    {
        public bool[] StatusToReturn = [];
        public int Writes;
        public TestPump() : base(new BaseDeviceParams("P", 0, "P")) { }
        protected override bool[] ReadStatus() => StatusToReturn;
        protected override void WriteHW(bool newstate) => Writes++;
    }

    [Fact]
    public void IsRunning_TrueWhenFirstStatusBitSet()
    {
        var p = new TestPump { StatusToReturn = [true] };
        Assert.True(p.IsRunning);
    }

    [Fact]
    public void IsRunning_FalseWhenBitClearOrEmpty()
    {
        Assert.False(new TestPump { StatusToReturn = [false] }.IsRunning);
        Assert.False(new TestPump { StatusToReturn = [] }.IsRunning);
    }

    [Fact]
    public void Power_On_SkipsWrite_WhenAlreadyRunning()
    {
        var p = new TestPump { Connected = true, StatusToReturn = [true] };
        p.Power(true);
        Assert.Equal(0, p.Writes);   // idempotent: no redundant write
    }

    [Fact]
    public void Power_On_Writes_WhenNotRunning()
    {
        var p = new TestPump { Connected = true, StatusToReturn = [false] };
        p.Power(true);
        Assert.Equal(1, p.Writes);
    }

    [Fact]
    public void Power_Off_AlwaysWrites_EvenWhenRunning()
    {
        var p = new TestPump { Connected = true, StatusToReturn = [true] };
        p.Power(false);
        Assert.Equal(1, p.Writes);   // off is never suppressed
    }

    // Double on the PRODUCTION path: PlcPump (not BasePump) is what every
    // real pump inherits from, so the idempotency guard must hold here too.
    private sealed class TestPlcPump : PlcPump
    {
        public bool[] StatusToReturn = [];
        public int Writes;
        public TestPlcPump(IPLCController plc)
            : base(plc, new BaseDeviceParams("P", 0, "P")) { Connected = true; }
        protected override bool[] ReadStatus() => StatusToReturn;
        protected override void WriteHW(bool newstate) => Writes++;
    }

    [Fact]
    public void PlcPump_PowerOn_SkipsWrite_WhenAlreadyRunning()
    {
        var p = new TestPlcPump(new FakePlcController()) { StatusToReturn = [true] };
        p.Power(true);
        Assert.Equal(0, p.Writes);
    }

    [Fact]
    public void PlcPump_PowerOn_Writes_WhenNotRunning()
    {
        var p = new TestPlcPump(new FakePlcController()) { StatusToReturn = [false] };
        p.Power(true);
        Assert.Equal(1, p.Writes);
    }

    [Fact]
    public void PlcPump_PowerOff_AlwaysWrites_EvenWhenRunning()
    {
        var p = new TestPlcPump(new FakePlcController()) { StatusToReturn = [true] };
        p.Power(false);
        Assert.Equal(1, p.Writes);
    }
}
