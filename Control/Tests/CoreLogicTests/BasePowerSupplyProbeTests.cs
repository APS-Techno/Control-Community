using System.Diagnostics;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core;
using APS.SystemControl.Core.HWClasses.Base;

namespace CoreLogicTests;

public class BasePowerSupplyProbeTests
{
    private sealed class FakePsu(bool responds) : BasePowerSupply(new BaseDeviceParams("PSU", 0, "psu"))
    {
        public int ReadFeedbackCalls { get; private set; }
        public override void ReadFeedback() { ReadFeedbackCalls++; IsResponding = responds; }
    }

    [Fact]
    public void Probe_alive_when_supply_responds()
    {
        var psu = new FakePsu(responds: true);
        var r = ((ISelfTestable)psu).Probe();
        Assert.Equal(HealthLevel.Alive, r.Level);
        Assert.True(r.Ok);
        Assert.Equal(1, psu.ReadFeedbackCalls);
    }

    [Fact]
    public void Probe_unresponsive_when_silent()
    {
        var r = ((ISelfTestable)new FakePsu(responds: false)).Probe();
        Assert.Equal(HealthLevel.Unresponsive, r.Level);
        Assert.False(r.Ok);
    }
}
