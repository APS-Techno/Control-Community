using APS.Hardware.S7;
using APS.SystemControl.Core.HWClasses.Base;

namespace CoreLogicTests;

public class S7200PlcProbeTests
{
    private sealed class FakePlc(bool ok, string serial) : S7200PLC
    {
        protected override bool TryReadCpuSerial(out string s) { s = serial; return ok; }
    }

    [Fact]
    public void Probe_alive_with_identity_when_cpu_responds()
    {
        var r = ((ISelfTestable)new FakePlc(true, "S C-XYZ")).Probe();
        Assert.Equal(HealthLevel.Alive, r.Level);
        Assert.Equal("S C-XYZ", r.Identity);
    }

    [Fact]
    public void Probe_unresponsive_when_cpu_silent()
        => Assert.Equal(HealthLevel.Unresponsive, ((ISelfTestable)new FakePlc(false, "")).Probe().Level);
}
