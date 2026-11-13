using System.Linq;
using APS.ProcessPanel.Contracts;
using APS.Controls.Generic;
using Xunit;

namespace CoreLogicTests;

public class ClusterPanelMapperTests
{
    private static ClusterRobotInfo Robot(double? pres = 5e-7, bool turbo = true, bool scroll = false, byte face = 1)
        => new(RobotStateCode.Transferring, face, 64, 200, true, false, pres, turbo, scroll);

    private static ClusterChamberInfo Proc(string key, SystemStateCode st = SystemStateCode.Processing)
        => new(key, 60, st, 1e-3, true,
               Temperature: 25,
               Flows: new[] { new ClusterReadingInfo($"{key}_Ar", "Ar", 12) },
               Powers: new[] { new ClusterReadingInfo($"{key}_RF", "RF", 240) },
               Valves: new[] { new ClusterValveInfo(key + "_V", "gas", true) },
               DrivePosition: 100, TurboOn: true, ScrollOn: false);

    [Fact]
    public void Readings_emit_pm_tmp_ar_rf_for_process_chamber()
    {
        var snap = new ClusterSnapshot(new[] { Proc("C1") }, Robot());
        var ids = ClusterPanelMapper.ToReadings(snap).Select(r => r.Id).ToArray();
        Assert.Contains("C1·pm", ids);
        Assert.Contains("C1·tmp", ids);
        Assert.Contains("C1·Ar", ids);
        Assert.Contains("C1·RF", ids);
    }

    [Fact]
    public void Readings_emit_secondary_flow_and_power_devices()
    {
        var c3 = new ClusterChamberInfo("C3", 60, SystemStateCode.Processing, 1e-3, true,
            Temperature: 25,
            Flows: new[]
            {
                new ClusterReadingInfo("C3_Ar", "Ar", 5),
                new ClusterReadingInfo("C3_N2", "N2", 2),
            },
            Powers: new[]
            {
                new ClusterReadingInfo("C3_RF",   "RF",   110),
                new ClusterReadingInfo("C3_RF_B", "RF B", 90),
            });
        var ids = ClusterPanelMapper.ToReadings(new ClusterSnapshot(new[] { c3 }, Robot()))
            .Select(r => r.Id).ToArray();
        Assert.Contains("C3·Ar", ids);
        Assert.Contains("C3·N2", ids);
        Assert.Contains("C3·RF", ids);
        Assert.Contains("C3·RF B", ids);
    }

    [Theory]
    [InlineData("C3_N2",   "C3", "N2")]
    [InlineData("C1_RF_B", "C1", "RF B")]
    [InlineData("C3_Ar",   "C3", "Ar")]
    [InlineData("orphan",  "C3", "orphan")]   // prefix mismatch → raw key
    public void ReadingInfo_For_derives_short_label(string key, string chamber, string expected)
        => Assert.Equal(expected, ClusterReadingInfo.For(key, chamber, 1.0).Label);

    [Fact]
    public void Readings_emit_only_pm_for_loadlock_and_tm()
    {
        var ll = new ClusterChamberInfo("LL", 0, SystemStateCode.Idle, 2e-7, false);
        var snap = new ClusterSnapshot(new[] { ll }, Robot());
        var ids = ClusterPanelMapper.ToReadings(snap).Select(r => r.Id).ToArray();
        Assert.Contains("LL·pm", ids);
        Assert.DoesNotContain("LL·Ar", ids);
        Assert.Contains("TM·pm", ids);
    }

    [Fact]
    public void Reading_state_maps_from_system_state()
    {
        var snap = new ClusterSnapshot(
            new[] { Proc("C1", SystemStateCode.Processing), Proc("C2", SystemStateCode.Stopped) }, Robot());
        var rs = ClusterPanelMapper.ToReadings(snap);
        Assert.Equal(ReadingState.Ok,    rs.First(r => r.Id == "C1·pm").State);
        Assert.Equal(ReadingState.Alarm, rs.First(r => r.Id == "C2·pm").State);
    }

    [Fact]
    public void Null_value_is_preserved()
    {
        var c = new ClusterChamberInfo("C1", 60, SystemStateCode.Idle, null, false);
        var snap = new ClusterSnapshot(new[] { c }, Robot());
        Assert.Null(ClusterPanelMapper.ToReadings(snap).First(r => r.Id == "C1·pm").Value);
    }

    [Fact]
    public void Valves_emit_slit_perchamber_and_pumps()
    {
        var snap = new ClusterSnapshot(new[] { Proc("C1") }, Robot(turbo: true, scroll: true));
        var v = ClusterPanelMapper.ToValves(snap).ToDictionary(x => x.Id, x => x.IsOpen);
        Assert.True(v["SV_C1"]);
        Assert.True(v["C1_V"]);
        Assert.True(v["C1 TP"]);
        Assert.False(v["C1 SP"]);
        Assert.True(v["TM TP"]);
        Assert.True(v["TM SP"]);
    }

    [Fact]
    public void Tm_pm_is_alarm_when_robot_error_active()
    {
        var errored = new ClusterRobotInfo(RobotStateCode.Error, 1, 64, 200, false, true, 5e-7, true, false);
        var snap = new ClusterSnapshot(new[] { Proc("C1") }, errored);
        var tm = ClusterPanelMapper.ToReadings(snap).First(r => r.Id == "TM·pm");
        Assert.Equal(ReadingState.Alarm, tm.State);
    }

    [Fact]
    public void Tm_pm_is_off_when_robot_ok()
    {
        var snap = new ClusterSnapshot(new[] { Proc("C1") }, Robot());
        var tm = ClusterPanelMapper.ToReadings(snap).First(r => r.Id == "TM·pm");
        Assert.Equal(ReadingState.Off, tm.State);
    }

    [Fact]
    public void Idle_chamber_stays_alive_while_recipe_active()
    {
        var snap = new ClusterSnapshot(new[] { Proc("C1", SystemStateCode.Idle) }, Robot());
        var rs = ClusterPanelMapper.ToReadings(snap, recipeActive: true);
        Assert.Equal(ReadingState.Ok, rs.First(r => r.Id == "C1·pm").State);
        Assert.Equal(ReadingState.Ok, rs.First(r => r.Id == "C1·RF").State);
    }

    [Fact]
    public void Idle_chamber_is_off_when_no_recipe()
    {
        var snap = new ClusterSnapshot(new[] { Proc("C1", SystemStateCode.Idle) }, Robot());
        var rs = ClusterPanelMapper.ToReadings(snap, recipeActive: false);
        Assert.Equal(ReadingState.Off, rs.First(r => r.Id == "C1·pm").State);
    }

    [Fact]
    public void Stopped_and_maintenance_win_over_recipe_active()
    {
        var snap = new ClusterSnapshot(
            new[] { Proc("C1", SystemStateCode.Stopped),
                    Proc("C2", SystemStateCode.Maintenance) }, Robot());
        var rs = ClusterPanelMapper.ToReadings(snap, recipeActive: true);
        Assert.Equal(ReadingState.Alarm,   rs.First(r => r.Id == "C1·pm").State);
        Assert.Equal(ReadingState.Warning, rs.First(r => r.Id == "C2·pm").State);
    }

    [Fact]
    public void Tm_pm_is_alive_while_recipe_active()
    {
        var snap = new ClusterSnapshot(new[] { Proc("C1") }, Robot());
        var tm = ClusterPanelMapper.ToReadings(snap, recipeActive: true)
            .First(r => r.Id == "TM·pm");
        Assert.Equal(ReadingState.Ok, tm.State);
    }

    [Fact]
    public void Maintenance_state_maps_to_warning()
    {
        var snap = new ClusterSnapshot(new[] { Proc("C1", SystemStateCode.Maintenance) }, Robot());
        Assert.Equal(ReadingState.Warning,
            ClusterPanelMapper.ToReadings(snap).First(r => r.Id == "C1·pm").State);
    }

    [Fact]
    public void Header_counts_processing_and_idle()
    {
        var snap = new ClusterSnapshot(
            new[] { Proc("C1", SystemStateCode.Processing),
                    Proc("C2", SystemStateCode.Processing),
                    Proc("C3", SystemStateCode.Idle) }, Robot());
        var s = ClusterPanelMapper.ToHeaderSummary(snap);
        Assert.Contains("2 processing", s);
        Assert.Contains("1 idle", s);
    }
}
