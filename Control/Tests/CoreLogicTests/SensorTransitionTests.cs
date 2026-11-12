using APS.SystemControl.Core;
using APS.SystemControl.Core.Classes;

namespace CoreLogicTests;

public class SensorTransitionTests
{
    private static Alarms MakeStartupSystem()
    {
        var a = new Alarms();
        // Baseline transitions for a typical config shape.
        // Startup → Idle  when pm < 1e-6
        // Idle    → Startup when pm > 1e-4 (chamber lost vacuum)
        a.AddSensorTransition(SystemState.Startup, SystemState.Idle, "pm", -1, 1e-6f);
        a.AddSensorTransition(SystemState.Idle,    SystemState.Startup, "pm",  1, 1e-4f);
        return a;
    }

    [Fact]
    public void Startup_to_Idle_fires_when_pressure_below_threshold()
    {
        var a = MakeStartupSystem();
        Assert.Equal(SystemState.Idle,
            a.CheckSensorTransitions(SystemState.Startup, "pm", 5e-7f));
    }

    [Fact]
    public void Startup_does_not_transition_when_pressure_above_threshold()
    {
        var a = MakeStartupSystem();
        Assert.Null(a.CheckSensorTransitions(SystemState.Startup, "pm", 1e-3f));
    }

    [Fact]
    public void Idle_does_not_use_Startup_to_Idle_rule()
    {
        var a = MakeStartupSystem();
        Assert.Null(a.CheckSensorTransitions(SystemState.Idle, "pm", 5e-7f));
    }

    [Fact]
    public void Idle_to_Startup_fires_when_pressure_spikes()
    {
        var a = MakeStartupSystem();
        Assert.Equal(SystemState.Startup,
            a.CheckSensorTransitions(SystemState.Idle, "pm", 2e-4f));
    }

    [Fact]
    public void Wrong_sensor_does_not_fire_transition()
    {
        var a = MakeStartupSystem();
        Assert.Null(a.CheckSensorTransitions(SystemState.Startup, "pl", 5e-7f));
    }

    [Fact]
    public void Equal_condition_uses_relative_tolerance_not_epsilon()
    {
        // Regression test: the original code compared with float.Epsilon,
        // which is ~1.4e-45 and effectively never matches real sensor
        // values. Transition must fire at a realistic tolerance.
        var a = new Alarms();
        a.AddSensorTransition(SystemState.Processing, SystemState.Idle, "tmp", 0, 25f);
        Assert.Equal(SystemState.Idle,
            a.CheckSensorTransitions(SystemState.Processing, "tmp", 25.01f));
    }

    [Fact]
    public void Equal_condition_does_not_fire_outside_tolerance()
    {
        var a = new Alarms();
        a.AddSensorTransition(SystemState.Processing, SystemState.Idle, "tmp", 0, 25f);
        Assert.Null(a.CheckSensorTransitions(SystemState.Processing, "tmp", 30f));
    }

    [Fact]
    public void No_transitions_registered_returns_null()
    {
        var a = new Alarms();
        Assert.Null(a.CheckSensorTransitions(SystemState.Startup, "pm", 0f));
    }

    [Fact]
    public void Startup_to_Idle_does_not_fire_when_state_just_changed_at_low_pressure()
    {
        // SYSTEM_AIR_RELEASE regression: system was steady in Idle at operating
        // vacuum (pm well below 1e-6). Recipe issues `state startup`. The next
        // poll used to fire the Startup→Idle rule immediately because pm<1e-6
        // was already level-true, bouncing the alarm machine back to Idle and
        // un-suppressing the high-pm alarm before A1 finished venting the
        // chamber. The rule must wait for an actual falling crossing.
        var a = MakeStartupSystem();
        a.CheckSensorTransitions(SystemState.Idle, "pm", 1e-7f);
        a.CheckSensorTransitions(SystemState.Idle, "pm", 1e-7f);
        Assert.Null(a.CheckSensorTransitions(SystemState.Startup, "pm", 1e-7f));
    }

    [Fact]
    public void Startup_to_Idle_still_fires_on_real_pump_down_crossing()
    {
        // Real pump-down: pm starts above threshold and falls past 1e-6.
        var a = MakeStartupSystem();
        Assert.Null(a.CheckSensorTransitions(SystemState.Startup, "pm", 1e-3f));
        Assert.Null(a.CheckSensorTransitions(SystemState.Startup, "pm", 1e-5f));
        Assert.Equal(SystemState.Idle,
            a.CheckSensorTransitions(SystemState.Startup, "pm", 5e-7f));
    }

    [Fact]
    public void First_matching_transition_wins_when_multiple_registered()
    {
        // Two rules with the same From+Sensor but different targets.
        // Implementation returns the first match — locks that behaviour in.
        var a = new Alarms();
        a.AddSensorTransition(SystemState.Startup, SystemState.Idle,       "pm", -1, 1e-6f);
        a.AddSensorTransition(SystemState.Startup, SystemState.Processing, "pm", -1, 1e-6f);
        Assert.Equal(SystemState.Idle,
            a.CheckSensorTransitions(SystemState.Startup, "pm", 1e-7f));
    }
}
