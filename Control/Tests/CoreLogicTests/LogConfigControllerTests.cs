using APS.Controls.Core;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.HWClasses.Base;
using APS.SystemControl.Core.Logging;
using Xunit;

namespace APS.SystemControl.Tests.CoreLogic;

/// <summary>
/// The GAUGE_A measures the process pressure itself and regulates it through the throttle valve;
/// during a deposition it is the in-range gauge (the ion gauge "pm" is off scale under process
/// gas). Although registered as a <see cref="DeviceType.Controller"/>, a controller implementing
/// <see cref="IPressureController"/> gets a pressure and a valve-position column, fed from the
/// poll cache.
/// </summary>
public class LogConfigControllerTests
{
    private sealed class TestPressureController : BaseController, IPressureController
    {
        public TestPressureController(string id, bool connected = true)
            : base(new BaseDeviceParams(id, 0, "GAUGE_A Vacuum Controller"))
            => Connected = connected;

        public float PressureTorr { get; set; }
        public float ValvePositionPercent { get; set; }

        // A wire read here would make LogConfig a second bus reader (see
        // LogConfigCacheTests); the properties above are the poll cache.
        public override object[] Measure()
        {
            MeasureCalls++;
            return [];
        }

        public int MeasureCalls;
    }

    private sealed class PlainController(string id) : BaseController(new BaseDeviceParams(id, 0, ""))
    {
        public new bool Connected { get => base.Connected; set => base.Connected = value; }
    }

    private static APS.SystemControl.Core.CoreClass.HwElementCollection With(params BaseController[] controllers)
    {
        var hw = new APS.SystemControl.Core.CoreClass.HwElementCollection();
        foreach (var c in controllers)
            hw.Item[DeviceType.Controller].Add(c.Params.ID, c);
        return hw;
    }

    [Fact]
    public void BuildColumns_includes_GAUGE_A_pressure_and_valve_position()
    {
        var hw = With(new TestPressureController("GAUGE_A#1"));

        var columns = new LogConfig().BuildColumns(hw, new HashSet<string>());

        Assert.Equal("Torr", columns["S_GAUGE_A#1"]);
        Assert.Equal("%", columns["TV_GAUGE_A#1"]);
    }

    [Fact]
    public void CollectValues_reads_controller_cache_not_the_wire()
    {
        var gaugeA = new TestPressureController("GAUGE_A#1")
        {
            PressureTorr = 0.0015f,      // 1.5 mTorr process pressure
            ValvePositionPercent = 42.5f,
        };
        var hw = With(gaugeA);

        var values = new LogConfig().CollectValues(hw, new HashSet<string>());

        Assert.Equal(0.0015, values["S_GAUGE_A#1"], 6);
        Assert.Equal(42.5, values["TV_GAUGE_A#1"], 3);
        Assert.Equal(0, gaugeA.MeasureCalls);
    }

    [Fact]
    public void Disconnected_controller_gets_no_column()
    {
        var hw = With(new TestPressureController("GAUGE_A#1", connected: false));

        var columns = new LogConfig().BuildColumns(hw, new HashSet<string>());

        Assert.DoesNotContain("S_GAUGE_A#1", columns.Keys);
        Assert.DoesNotContain("TV_GAUGE_A#1", columns.Keys);
    }

    [Fact]
    public void Controller_without_pressure_readings_gets_no_column()
    {
        var plain = new PlainController("CTRL1") { Connected = true };
        var hw = With(plain);

        var columns = new LogConfig().BuildColumns(hw, new HashSet<string>());
        var values = new LogConfig().CollectValues(hw, new HashSet<string>());

        Assert.DoesNotContain(columns.Keys, k => k.Contains("CTRL1"));
        Assert.DoesNotContain(values.Keys, k => k.Contains("CTRL1"));
    }

    [Fact]
    public void Columns_and_values_use_the_same_keys()
    {
        // A header with permanently empty cells (or a value with no header,
        // silently dropped by DataLogger.LogRow) is the failure this guards.
        var hw = With(new TestPressureController("GAUGE_A#1"));

        var columns = new LogConfig().BuildColumns(hw, new HashSet<string>());
        var values = new LogConfig().CollectValues(hw, new HashSet<string>());

        Assert.Equal(columns.Keys.OrderBy(k => k), values.Keys.OrderBy(k => k));
    }
}
