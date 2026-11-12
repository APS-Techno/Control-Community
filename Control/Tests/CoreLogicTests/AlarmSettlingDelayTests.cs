using APS.SystemControl.Core;
using APS.SystemControl.Core.Classes;

namespace CoreLogicTests;

/// <summary>
/// The chamber leaves `processing` the instant the last gas valve closes, while still at process
/// pressure (~1.4 mTorr); it needs tens of seconds to pump below the idle rule's 1e-4 Torr threshold.
/// A settling delay (`delay="60"` in Alarms.xml) requires the condition to hold CONTINUOUSLY for that
/// long, so a transient pump-down is ignored while a genuine over-pressure still fires.
/// </summary>
public class AlarmSettlingDelayTests
{
    private const int CondMore = 1; // ConditionParser: "more" => 1

    private static (Alarms, FakeClock) MainAlarmsWithSettlingDelay(float delaySeconds = 60f)
    {
        var clock = new FakeClock();
        var a = new Alarms("Main") { Clock = clock.Now };
        // <alarm sensor="pm" cond="more" val="1e-4" state="idle" delay="60">
        a.Add("pm", 1e-4f, CondMore, SystemState.Idle, "Main chamber pressure too high!",
              delaySeconds: delaySeconds);
        return (a, clock);
    }

    private sealed class FakeClock
    {
        private DateTime _t = new(2026, 8, 30, 6, 0, 0, DateTimeKind.Utc);
        public Func<DateTime> Now => () => _t;
        public void Advance(double seconds) => _t = _t.AddSeconds(seconds);
    }

    [Fact]
    public void Does_not_fire_immediately_when_condition_first_becomes_true()
    {
        var (a, _) = MainAlarmsWithSettlingDelay();

        // Recipe just ended: state flipped to Idle while pm is still 1.4 mTorr.
        bool fired = a.Check(SystemState.Idle, "pm", 1.4e-3f);

        Assert.False(fired);
    }

    [Fact]
    public void Does_not_fire_when_pressure_settles_within_the_delay()
    {
        var (a, clock) = MainAlarmsWithSettlingDelay();

        a.Check(SystemState.Idle, "pm", 1.4e-3f);   // still high, timer starts
        clock.Advance(20);
        a.Check(SystemState.Idle, "pm", 3.0e-4f);   // pumping down, still above
        clock.Advance(15);
        bool fired = a.Check(SystemState.Idle, "pm", 5.0e-5f); // below threshold

        Assert.False(fired);
    }

    [Fact]
    public void Fires_when_the_condition_persists_past_the_delay()
    {
        var (a, clock) = MainAlarmsWithSettlingDelay();

        a.Check(SystemState.Idle, "pm", 1.4e-3f);
        clock.Advance(61);
        bool fired = a.Check(SystemState.Idle, "pm", 1.4e-3f); // genuine leak

        Assert.True(fired);
        Assert.Equal("Main chamber pressure too high!", a.AlarmMessage);
    }

    [Fact]
    public void Settling_timer_restarts_after_the_condition_clears()
    {
        var (a, clock) = MainAlarmsWithSettlingDelay();

        a.Check(SystemState.Idle, "pm", 1.4e-3f);
        clock.Advance(50);
        a.Check(SystemState.Idle, "pm", 5.0e-5f);   // cleared -> timer reset
        clock.Advance(50);
        bool fired = a.Check(SystemState.Idle, "pm", 1.4e-3f); // high again, fresh timer

        Assert.False(fired);
    }

    /// <summary>
    /// A rule with no delay must fire on the very first sample; every existing rule relies on this.
    /// </summary>
    [Fact]
    public void Rule_without_delay_still_fires_immediately()
    {
        var a = new Alarms("Main");
        a.Add("pm", 2e-1f, CondMore, SystemState.Processing, "Main chamber pressure too high!");

        bool fired = a.Check(SystemState.Processing, "pm", 5e-1f);

        Assert.True(fired);
    }
}

public class AlarmDescribeProjectionTests
{
    private static Alarms MainAlarms()
    {
        var a = new Alarms("Main");
        a.Add("pm", 1e-4f, 1, SystemState.Idle, "Main chamber pressure too high!",
              guardValve: "LL_A1", guardValveOpen: true, delaySeconds: 60f);
        a.Add("pl", 5e-1f, 1, SystemState.Processing, "Load lock pressure too high!");
        a.AddSensorTransition(SystemState.Startup, SystemState.Idle, "pm", -1, 1e-6f);
        a.AddValveTransition(SystemState.Idle, SystemState.Maintenance, "A1", true, ["GVM closed"]);
        a.AddGroupTransition(SystemState.Processing, SystemState.Idle, GroupKind.Gas, GroupCondition.AllOff);
        return a;
    }

    private static System.Text.Json.JsonElement Project(Alarms a)
    {
        var view = APS.SystemControl.Core.Remote.Agent.CoreAgentSystemAccess.DescribeAlarmChambers(
            new Dictionary<string, Alarms> { ["Main"] = a });
        string json = System.Text.Json.JsonSerializer.Serialize(view,
            APS.SystemControl.Core.Remote.Agent.AgentProtocol.Opt);
        return System.Text.Json.JsonDocument.Parse(json).RootElement[0];
    }

    [Fact]
    public void Transition_lists_are_exposed_read_only()
    {
        var a = MainAlarms();

        var sensor = Assert.Single(a.SensorTransitions);
        Assert.Equal("pm", sensor.Sensor);
        var valve = Assert.Single(a.ValveTransitions);
        Assert.Equal("A1", valve.ValveId);
        var group = Assert.Single(a.GroupTransitions);
        Assert.Equal(GroupCondition.AllOff, group.Condition);
    }

    [Fact]
    public void Rule_carries_delay_guard_and_unit()
    {
        var pm = Project(MainAlarms()).GetProperty("rules")[0];

        Assert.Equal("pm", pm.GetProperty("sensor").GetString());
        Assert.Equal(60.0, pm.GetProperty("delaySeconds").GetDouble());
        Assert.Equal("LL_A1 open", pm.GetProperty("unless").GetString());
        Assert.Equal("Torr", pm.GetProperty("unit").GetString());
        Assert.Equal("Idle", pm.GetProperty("state").GetString());
    }

    [Fact]
    public void Rule_without_delay_or_guard_reports_zero_and_null()
    {
        var pl = Project(MainAlarms()).GetProperty("rules")[1];

        Assert.Equal(0.0, pl.GetProperty("delaySeconds").GetDouble());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, pl.GetProperty("unless").ValueKind);
    }

    [Fact]
    public void Closed_guard_is_worded_closed()
    {
        var a = new Alarms("Main");
        a.Add("pm", 1e-4f, 1, SystemState.Idle, "m", guardValve: "GVM", guardValveOpen: false);

        Assert.Equal("GVM closed", Project(a).GetProperty("rules")[0].GetProperty("unless").GetString());
    }

    [Fact]
    public void Transitions_are_projected_by_kind()
    {
        var t = Project(MainAlarms()).GetProperty("transitions");

        var s = t.GetProperty("sensor")[0];
        Assert.Equal("Startup", s.GetProperty("from").GetString());
        Assert.Equal("Idle", s.GetProperty("to").GetString());
        Assert.Equal("pm", s.GetProperty("sensor").GetString());
        Assert.Equal(-1, s.GetProperty("condition").GetInt32());

        var v = t.GetProperty("valve")[0];
        Assert.Equal("A1", v.GetProperty("valve").GetString());
        Assert.True(v.GetProperty("open").GetBoolean());
        Assert.Equal("GVM closed", v.GetProperty("requirements")[0].GetString());

        var g = t.GetProperty("group")[0];
        Assert.Equal("Gas", g.GetProperty("group").GetString());
        Assert.Equal("AllOff", g.GetProperty("condition").GetString());
    }
}
