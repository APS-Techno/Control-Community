using APS.SystemControl.Core;
using APS.SystemControl.Core.Classes;

namespace CoreLogicTests;

/// <summary>
/// <see cref="Alarms.Check"/> compares "equal" with a relative-scale tolerance, not
/// <c>float.Epsilon</c>, under which a 1 Pa setpoint would never fire.
/// </summary>
public class AlarmEqualityToleranceTests
{
    private static Alarms MakeAlarmsWithEqualRule(float target)
    {
        var alarms = new Alarms();
        // condition = 0 → "equal"
        alarms.Add("pl", target, 0, SystemState.Processing, "equal-to alarm");
        return alarms;
    }

    [Fact]
    public void Equal_condition_fires_for_exact_match()
    {
        var alarms = MakeAlarmsWithEqualRule(1.0f);
        Assert.True(alarms.Check(SystemState.Processing, "pl", 1.0f));
    }

    [Fact]
    public void Equal_condition_fires_within_relative_tolerance()
    {
        // Target 1000.0 — tolerance is scale*1e-3, so 1000.5 must still match.
        var alarms = MakeAlarmsWithEqualRule(1000f);
        Assert.True(alarms.Check(SystemState.Processing, "pl", 1000.5f));
    }

    [Fact]
    public void Equal_condition_does_not_fire_outside_tolerance()
    {
        // 1000 * 1e-3 = 1.0 tolerance; 1002 is clearly outside.
        var alarms = MakeAlarmsWithEqualRule(1000f);
        Assert.False(alarms.Check(SystemState.Processing, "pl", 1002f));
    }

    [Fact]
    public void Equal_condition_fires_for_tiny_values_at_absolute_floor()
    {
        // The 1e-6 tolerance floor lets tiny setpoints match.
        var alarms = MakeAlarmsWithEqualRule(0f);
        Assert.True(alarms.Check(SystemState.Processing, "pl", 1e-7f));
    }

    [Fact]
    public void Less_and_more_conditions_unaffected_by_tolerance_change()
    {
        var alarms = new Alarms();
        alarms.Add("pl", 1.0f, -1, SystemState.Processing, "less"); // value < 1.0 triggers
        alarms.Add("pl", 1.0f,  1, SystemState.Processing, "more"); // value > 1.0 triggers

        Assert.True(alarms.Check(SystemState.Processing, "pl", 0.5f));
        Assert.True(alarms.Check(SystemState.Processing, "pl", 1.5f));
        Assert.False(alarms.Check(SystemState.Processing, "pl", 1.0f)); // neither <, nor >
    }
}
