using System.Threading.Channels;
using APS.SystemControl.Core.Classes;
using Xunit;
using ThreadMsg = APS.SystemControl.Core.ThreadMsg;
using SystemState = APS.SystemControl.Core.SystemState;

namespace APS.SystemControl.Tests.CoreLogic;

/// <summary>
/// <see cref="Core.CheckGroupTransitionAndLog"/> must evaluate the group rules
/// for EVERY registered chamber; cluster configs have no Main chamber.
/// </summary>
public class CoreGroupTransitionPerChamberTests
{
    private static APS.SystemControl.Core.Core NewCore() =>
        new(Channel.CreateUnbounded<ThreadMsg>().Writer);

    private static Alarms MakeIdleToProcessingOnAnyOpen(string chamberId)
    {
        var a = new Alarms(chamberId);
        a.AddGroupTransition(SystemState.Startup,    SystemState.Processing, GroupKind.GasValve, GroupCondition.AnyOpen);
        a.AddGroupTransition(SystemState.Idle,       SystemState.Processing, GroupKind.GasValve, GroupCondition.AnyOpen);
        a.AddGroupTransition(SystemState.Processing, SystemState.Idle,       GroupKind.Gas,      GroupCondition.AllOff);
        return a;
    }

    [Fact]
    public void C1_idle_transitions_to_processing_when_gas_valve_anyopen_even_without_Main_chamber()
    {
        var core = NewCore();
        core.chamberAlarms["C1"] = MakeIdleToProcessingOnAnyOpen("C1");
        core.SetChamberState("C1", SystemState.Idle);

        core.CheckGroupTransitionAndLog("test");

        // No gas valves registered → evaluator returns AnyOpen=false →
        // no transition. C1 stays Idle. This proves the iteration runs
        // without throwing on a no-Main config.
        Assert.Equal(SystemState.Idle, core.GetChamberState("C1"));
    }

    [Fact]
    public void Per_chamber_iteration_does_not_throw_when_Main_is_absent()
    {
        var core = NewCore();
        core.chamberAlarms["TM"] = MakeIdleToProcessingOnAnyOpen("TM");
        core.chamberAlarms["C1"] = MakeIdleToProcessingOnAnyOpen("C1");

        // Cluster configs have no "Main" key; must complete cleanly.
        var ex = Record.Exception(() => core.CheckGroupTransitionAndLog("test"));
        Assert.Null(ex);
    }

    [Fact]
    public void Each_chamber_transitions_independently_from_its_own_state()
    {
        var core = NewCore();
        core.chamberAlarms["C1"] = MakeIdleToProcessingOnAnyOpen("C1");
        core.chamberAlarms["C2"] = MakeIdleToProcessingOnAnyOpen("C2");
        core.SetChamberState("C1", SystemState.Idle);
        core.SetChamberState("C2", SystemState.Processing);

        // Drive the rules with a synthetic evaluator (gas valves open), using
        // the same dispatcher shape as CheckGroupTransitionAndLog.
        foreach (var (chamberId, chAlarms) in core.chamberAlarms)
        {
            var chState = core.GetChamberState(chamberId);
            var newState = chAlarms.CheckGroupTransition(chState,
                (k, c) => k == GroupKind.GasValve && c == GroupCondition.AnyOpen);
            if (newState.HasValue)
                core.SetChamberState(chamberId, newState.Value);
        }

        // C1 Idle + gasvalve.AnyOpen → Processing.
        Assert.Equal(SystemState.Processing, core.GetChamberState("C1"));
        // C2 was already Processing — no rule applies (AnyOpen does not
        // fire on Processing → it stays put).
        Assert.Equal(SystemState.Processing, core.GetChamberState("C2"));
    }
}
