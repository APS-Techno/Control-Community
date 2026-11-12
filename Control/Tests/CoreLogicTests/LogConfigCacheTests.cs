using APS.Controls.Core;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.HWClasses.Base;
using APS.SystemControl.Core.Logging;
using Xunit;

namespace APS.SystemControl.Tests.CoreLogic;

/// <summary>
/// LogConfig.CollectValues runs on the coordinator thread (via LogDataRow) every
/// data-logging cycle. It must read the in-memory LastX caches, NOT the wire, or it
/// becomes a second concurrent PLC/serial reader contending with the bus pollers.
/// </summary>
public class LogConfigCacheTests
{
    private sealed class TestSensor : BaseSensor
    {
        public float Hw;
        public int ReadHwCalls;
        public TestSensor() : base(new BaseDeviceParams("pm", 0, "")) { Connected = true; }
        protected override float ReadHW() { ReadHwCalls++; return Hw; }
    }

    [Fact]
    public void CollectValues_reads_sensor_cache_not_live()
    {
        var sensor = new TestSensor { Hw = 0.02f };
        sensor.Poll();                       // bus poller: LastValue = 0.02, ReadHwCalls = 1
        int callsAfterPoll = sensor.ReadHwCalls;
        sensor.Hw = 999f;                    // change the LIVE reading; the cache stays 0.02

        var hw = new APS.SystemControl.Core.CoreClass.HwElementCollection();
        hw.Item[DeviceType.Sensor].Add("pm", sensor);

        var values = new LogConfig().CollectValues(hw, new HashSet<string>());

        Assert.Equal(0.02, values["S_pm"], 3);            // cached value, not the live 999
        Assert.Equal(callsAfterPoll, sensor.ReadHwCalls); // CollectValues did not touch the wire
    }
}
