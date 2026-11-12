using APS.SystemControl.Core;
using Xunit;

namespace CoreLogicTests;

public class CoreTelemetryHookTests
{
    private static Core NewCore() => new CoreThread(new ChannelPool()).Core;

    [Fact]
    public void SetChamberState_raises_ChamberStateChanged_with_old_and_new_and_not_when_unchanged()
    {
        var core = NewCore();
        var seen = new List<(string, SystemState, SystemState)>();
        core.ChamberStateChanged += (ch, from, to) => seen.Add((ch, from, to));
        var before = core.GetChamberState("Main");
        core.SetChamberState("Main", SystemState.Processing);
        core.SetChamberState("Main", SystemState.Processing);
        core.SetChamberState("TM", SystemState.Idle);
        Assert.Equal(2, seen.Count);
        Assert.Equal(("Main", before, SystemState.Processing), seen[0]);
        Assert.Equal("TM", seen[1].Item1);
        Assert.Equal(SystemState.Idle, seen[1].Item3);
    }

    [Fact]
    public void RaiseRecipeLifecycle_reaches_subscribers()
    {
        var core = NewCore();
        RecipeLifecycleEvent? got = null;
        core.RecipeLifecycle += e => got = e;
        core.RaiseRecipeLifecycle("started", "R1", "");
        Assert.Equal(new RecipeLifecycleEvent("started", "R1", ""), got);
    }

    [Fact]
    public void FaultPause_send_and_end_raise_FaultPauseChanged()
    {
        var core = NewCore();
        var seen = new List<FaultPauseInfo?>();
        core.FaultPauseChanged += seen.Add;
        var info = new FaultPauseInfo("step 3", "PSU not responding", null, new HoldReport([], []));
        core.SendFaultPauseToUI(info);
        core.SendFaultPauseEndToUI();
        Assert.Equal(2, seen.Count);
        Assert.Same(info, seen[0]);
        Assert.Null(seen[1]);
    }

    [Fact]
    public void A_throwing_state_handler_does_not_break_the_transition()
    {
        var core = NewCore();
        core.ChamberStateChanged += (_, _, _) => throw new InvalidOperationException("boom");
        core.SetChamberState("Main", SystemState.Processing);
        Assert.Equal(SystemState.Processing, core.GetChamberState("Main"));
    }
}
