using APS.ProcessPanel.Contracts;
using APS.SystemControl;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.HWClasses.Base;
using Xunit;

namespace CoreLogicTests;

/// <summary>
/// Core change-gates every non-sensor poll value (ShouldForwardPolledValue),
/// so a supply or throttle controller whose readback has not changed since
/// its first message — which went out before the Process panel and its
/// telemetry collector existed — is never re-sent. The seeder primes a fresh
/// collector from the devices' cached readbacks so the rows are live from
/// the first tick instead of after the first change.
/// </summary>
public class TelemetrySeederTests
{
    private sealed class FakePsu(string id, object[] readback) : BasePowerSupply(new BaseDeviceParams(id, 0, ""))
    {
        private readonly object[] _readback = readback;
        public override object[] GetReadback() => _readback;
    }

    private sealed class FakeController(string id, object[] measurement) : BaseController(new BaseDeviceParams(id, 0, ""))
    {
        private readonly object[] _m = measurement;
        public override object[] Measure() => _m;
    }

    private static ReadingState Ok(string _, double __) => ReadingState.Ok;

    [Fact]
    public void Seeds_connected_supplies_and_controllers_from_their_cached_readbacks()
    {
        var psu  = new FakePsu("PSU-A", [412f, 730f, 300f, true]) { Connected = true };
        var bias = new FakePsu("BIAS-1",
            [-80f, 0.125f, 0f, 0f, 0f, 0f, 0f, 0f, false, false, false, false, true]) { Connected = true };
        var gaugeA = new FakeController("GAUGE_A#1", ["Data", 2.5f, 11.5f, 1.5f]) { Connected = true };
        gaugeA.Poll();

        var c = new ProcessTelemetryCollector();
        c.RegisterMany(["PSU-A_V", "PSU-A_I", "BIAS-1_V1", "BIAS-1_I1", "GAUGE_A#1_P", "GAUGE_A#1_Pos"]);

        int pushed = TelemetrySeeder.Seed([psu, bias, gaugeA], c, Ok);

        var snap = c.Snapshot().ToDictionary(r => r.Id);
        Assert.Equal(412.0,  snap["PSU-A_V"].Value);
        Assert.Equal(730.0,  snap["PSU-A_I"].Value);
        Assert.Equal(-80.0,  snap["BIAS-1_V1"].Value);
        Assert.Equal(ReadingState.Ok, snap["BIAS-1_I1"].State);
        Assert.Equal(2.5,    snap["GAUGE_A#1_P"].Value);
        Assert.Equal(11.5,   snap["GAUGE_A#1_Pos"].Value);
        Assert.True(pushed >= 6);
    }

    [Fact]
    public void Disconnected_devices_are_skipped()
    {
        var psu = new FakePsu("RF301", [1f, 2f, 3f, true]) { Connected = false };
        var c = new ProcessTelemetryCollector();
        c.Register("RF301_W");

        TelemetrySeeder.Seed([psu], c, Ok);

        Assert.Equal(ReadingState.Off, c.Snapshot().Single().State);
        Assert.Null(c.Snapshot().Single().Value);
    }

    [Fact]
    public void Controller_with_no_measurement_yet_is_skipped_without_throwing()
    {
        var gaugeA = new FakeController("GAUGE_A#1", []) { Connected = true };   // never polled → LastMeasurement empty
        var c = new ProcessTelemetryCollector();
        c.Register("GAUGE_A#1_P");

        TelemetrySeeder.Seed([gaugeA], c, Ok);

        Assert.Equal(ReadingState.Off, c.Snapshot().Single().State);
    }
}
