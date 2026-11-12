using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.HWClasses.Base;

namespace CoreLogicTests;

public class BaseControllerProbeTests
{
    private sealed class FakeCtrl(object[] measurement)
        : BaseController(new BaseDeviceParams("GAUGE_A", 0, "gauge"))
    {
        public override object[] Measure() => measurement;
    }

    [Fact]
    public void Probe_alive_when_measure_returns_values()
        => Assert.Equal(HealthLevel.Alive, ((ISelfTestable)new FakeCtrl([1.2e-3f])).Probe().Level);

    [Fact]
    public void Probe_unresponsive_when_measure_empty()
        => Assert.Equal(HealthLevel.Unresponsive, ((ISelfTestable)new FakeCtrl([])).Probe().Level);
}
