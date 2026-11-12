using APS.SystemControl.Core;
using APS.SystemControl.Core.Classes;

namespace CoreLogicTests;

/// <summary>
/// Group transitions: Idle/Startup -> Processing when any gas valve
/// opens; Processing -> Idle when every gas valve is closed AND every
/// MFC setpoint is zero.
/// </summary>
public class GroupTransitionTests
{
    private static Alarms MakeRules()
    {
        var a = new Alarms();
        a.AddGroupTransition(SystemState.Startup,    SystemState.Processing, GroupKind.GasValve, GroupCondition.AnyOpen);
        a.AddGroupTransition(SystemState.Idle,       SystemState.Processing, GroupKind.GasValve, GroupCondition.AnyOpen);
        a.AddGroupTransition(SystemState.Processing, SystemState.Idle,       GroupKind.Gas,      GroupCondition.AllOff);
        return a;
    }

    private static Func<GroupKind, GroupCondition, bool> Only(GroupKind k, GroupCondition c)
        => (kind, cond) => kind == k && cond == c;

    [Fact]
    public void Gas_valve_open_from_idle_goes_processing()
        => Assert.Equal(SystemState.Processing,
            MakeRules().CheckGroupTransition(SystemState.Idle, Only(GroupKind.GasValve, GroupCondition.AnyOpen)));

    [Fact]
    public void Gas_valve_open_from_startup_goes_processing()
        => Assert.Equal(SystemState.Processing,
            MakeRules().CheckGroupTransition(SystemState.Startup, Only(GroupKind.GasValve, GroupCondition.AnyOpen)));

    [Fact]
    public void All_off_from_processing_goes_idle()
        => Assert.Equal(SystemState.Idle,
            MakeRules().CheckGroupTransition(SystemState.Processing, Only(GroupKind.Gas, GroupCondition.AllOff)));

    [Fact]
    public void Any_open_from_processing_stays_processing()
        => Assert.Null(MakeRules().CheckGroupTransition(SystemState.Processing,
            Only(GroupKind.GasValve, GroupCondition.AnyOpen)));

    [Fact]
    public void All_off_from_idle_does_not_fire()
        => Assert.Null(MakeRules().CheckGroupTransition(SystemState.Idle,
            Only(GroupKind.Gas, GroupCondition.AllOff)));

    [Fact]
    public void Evaluator_false_for_all_returns_null()
        => Assert.Null(MakeRules().CheckGroupTransition(SystemState.Idle, (_, _) => false));

    [Fact]
    public void State_with_no_rules_registered_returns_null()
        => Assert.Null(MakeRules().CheckGroupTransition(SystemState.Stopped, (_, _) => true));

    [Fact]
    public void First_matching_transition_wins()
    {
        var a = new Alarms();
        a.AddGroupTransition(SystemState.Idle, SystemState.Stopped,    GroupKind.GasValve, GroupCondition.AnyOpen);
        a.AddGroupTransition(SystemState.Idle, SystemState.Processing, GroupKind.GasValve, GroupCondition.AnyOpen);
        Assert.Equal(SystemState.Stopped,
            a.CheckGroupTransition(SystemState.Idle, (_, _) => true));
    }
}
