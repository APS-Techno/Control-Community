using System.Threading.Channels;
using APS.SystemControl.Core.Menus.Services;
using Xunit;
using ThreadMsg = APS.SystemControl.Core.ThreadMsg;

namespace CoreLogicTests;

/// <summary>
/// IRuntimeState is the only window a menu contributor has onto recipe state.
/// The jog dial disables itself through it, so a stale or inverted reading
/// would let an operator jog the holder mid-deposition.
/// </summary>
public class RuntimeStateAdapterTests
{
    private static APS.SystemControl.Core.Core NewCore() =>
        new(Channel.CreateUnbounded<ThreadMsg>().Writer);

    [Fact]
    public void Reports_false_when_no_recipe_holds_the_slot()
    {
        var core = NewCore();
        var adapter = new RuntimeStateAdapter(core);
        Assert.False(adapter.IsRecipeRunning);
    }

    [Fact]
    public void Reports_true_while_a_recipe_holds_the_slot()
    {
        var core = NewCore();
        var adapter = new RuntimeStateAdapter(core);

        Assert.True(core.TryBeginRecipeSlot());
        Assert.True(adapter.IsRecipeRunning);

        core.EndRecipeSlot();
        Assert.False(adapter.IsRecipeRunning);
    }

    /// <summary>The two flags must be wired to DIFFERENT Core properties. Pointing
    /// <c>IsManualControlGated</c> at <c>IsRecipeRunning</c> compiles, passes every other test
    /// here, and silently leaves the jog dial dead for the whole of a hold — the one state the
    /// operator was handed the machine in.</summary>
    [Fact]
    public void The_manual_gate_opens_for_a_held_run_while_the_run_flag_stays_set()
    {
        var core = NewCore();
        var adapter = new RuntimeStateAdapter(core);

        Assert.True(core.TryBeginRecipeSlot());
        Assert.True(adapter.IsManualControlGated);

        core.SetHeldForOperator(true);
        Assert.False(adapter.IsManualControlGated);   // the dial re-enables
        Assert.True(adapter.IsRecipeRunning);         // the run still owns the machine

        core.SetHeldForOperator(false);
        Assert.True(adapter.IsManualControlGated);
        core.EndRecipeSlot();
    }
}
