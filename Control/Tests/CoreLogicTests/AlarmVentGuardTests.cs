using APS.SystemControl.Core;
using APS.SystemControl.Core.Classes;

namespace CoreLogicTests;

/// <summary>
/// Guard for the spurious "LL pressure too high!" alarm during
/// LOADLOCK_AIR_RELEASE. A pressure-too-high alarm must not fire while the
/// chamber's own vent valve is open — venting is an intentional, controlled
/// pressure rise, not a fault. Expressed in Alarms.xml as unless="LL_A1 open".
/// </summary>
public class AlarmVentGuardTests
{
    private const int CondMore = 1; // ConditionParser: "more" => 1 (value > threshold)

    private static Alarms LlAlarmsWithVentGuard()
    {
        var a = new Alarms("LL");
        // <alarm sensor="LL_pm" cond="more" val="0.1" state="idle" unless="LL_A1 open">
        a.Add("LL_pm", 0.1f, CondMore, SystemState.Idle, "LL pressure too high!",
              guardValve: "LL_A1", guardValveOpen: true);
        return a;
    }

    [Fact]
    public void Alarm_Suppressed_WhileVentValveOpen()
    {
        var a = LlAlarmsWithVentGuard();
        bool fired = a.Check(SystemState.Idle, "LL_pm", 760f,
            valveId => valveId == "LL_A1" ? true : (bool?)null);
        Assert.False(fired);
    }

    [Fact]
    public void Alarm_Fires_WhenVentValveClosed()
    {
        var a = LlAlarmsWithVentGuard();
        bool fired = a.Check(SystemState.Idle, "LL_pm", 760f,
            valveId => valveId == "LL_A1" ? false : (bool?)null);
        Assert.True(fired);
    }

    [Fact]
    public void Alarm_Fires_WhenGuardValveUnresolvable()
    {
        // Defensive: if the guard valve can't be resolved, err toward firing.
        var a = LlAlarmsWithVentGuard();
        bool firedNoResolver = a.Check(SystemState.Idle, "LL_pm", 760f);
        bool firedNullState  = a.Check(SystemState.Idle, "LL_pm", 760f, _ => null);
        Assert.True(firedNoResolver);
        Assert.True(firedNullState);
    }

    [Fact]
    public void UnguardedAlarm_StillFires_RegardlessOfValves()
    {
        var a = new Alarms("LL");
        a.Add("LL_pm", 0.1f, CondMore, SystemState.Idle, "LL pressure too high!");
        Assert.True(a.Check(SystemState.Idle, "LL_pm", 760f, _ => true));
    }
}
