using APS.ProcessPanel.Contracts;
using APS.SystemControl.GUIControls.Process;
using Xunit;

namespace CoreLogicTests;

public class LoopProgressTrackerTests
{
    // The tracker only reads Name, State, Elapsed, CurrentStack and TotalStacks —
    // the rest of RecipeInfo is filled with plausible constants.
    private static RecipeInfo Info(
        int stack, int total, double elapsedSec,
        string name = "STACK",
        RecipeState state = RecipeState.Running)
        => new(name, 1, 10, "step", state, TimeSpan.FromSeconds(elapsedSec), null, stack, total);

    [Fact]
    public void Inactive_when_no_loop_is_running()
    {
        var t = new LoopProgressTracker();
        Assert.False(t.Update(Info(0, 0, 12)).IsActive);
    }

    [Fact]
    public void Active_while_the_outer_loop_runs()
    {
        var s = new LoopProgressTracker().Update(Info(1, 10, 0));

        Assert.True(s.IsActive);
        Assert.Equal(1, s.CurrentStack);
        Assert.Equal(10, s.TotalStacks);
    }

    [Fact]
    public void Deactivates_when_the_recipe_completes()
    {
        var t = new LoopProgressTracker();
        t.Update(Info(2, 4, 30));

        Assert.False(t.Update(Info(2, 4, 35, state: RecipeState.Complete)).IsActive);
    }

    [Fact]
    public void Deactivates_on_error_even_if_counters_are_still_set()
    {
        var t = new LoopProgressTracker();
        t.Update(Info(2, 4, 30));

        Assert.False(t.Update(Info(2, 4, 31, state: RecipeState.Error)).IsActive);
    }

    [Fact]
    public void Iteration_elapsed_is_measured_from_the_iteration_start()
    {
        var t = new LoopProgressTracker();
        t.Update(Info(1, 4, 10));               // iteration 1 anchors at 10 s

        Assert.Equal(TimeSpan.FromSeconds(30), t.Update(Info(1, 4, 40)).IterationElapsed);
    }

    [Fact]
    public void No_estimate_during_the_first_iteration()
    {
        var t = new LoopProgressTracker();
        t.Update(Info(1, 4, 0));
        var s = t.Update(Info(1, 4, 45));

        Assert.Null(s.IterationEstimate);
        Assert.Null(s.LoopRemaining);
    }

    [Fact]
    public void Stack_transition_records_the_completed_iteration_and_reanchors()
    {
        var t = new LoopProgressTracker();
        t.Update(Info(1, 4, 0));
        var s = t.Update(Info(2, 4, 60));       // iteration 1 took 60 s

        Assert.Equal(TimeSpan.FromSeconds(60), s.IterationEstimate);
        Assert.Equal(TimeSpan.Zero, s.IterationElapsed);
    }

    [Fact]
    public void Estimate_is_the_mean_of_completed_iterations()
    {
        var t = new LoopProgressTracker();
        t.Update(Info(1, 4, 0));
        t.Update(Info(2, 4, 60));               // iteration 1 = 60 s
        var s = t.Update(Info(3, 4, 160));      // iteration 2 = 100 s

        Assert.Equal(TimeSpan.FromSeconds(80), s.IterationEstimate);
    }

    [Fact]
    public void Loop_remaining_is_rest_of_current_iteration_plus_pending_ones()
    {
        var t = new LoopProgressTracker();
        t.Update(Info(1, 4, 0));
        t.Update(Info(2, 4, 60));               // estimate 60 s, iteration 2 anchors at 60 s
        var s = t.Update(Info(2, 4, 70));       // 10 s into iteration 2

        // (60 - 10) + 60 * (4 - 2) = 170
        Assert.Equal(TimeSpan.FromSeconds(170), s.LoopRemaining);
    }

    [Fact]
    public void An_overrunning_iteration_never_yields_negative_remaining()
    {
        var t = new LoopProgressTracker();
        t.Update(Info(1, 4, 0));
        t.Update(Info(2, 4, 60));
        var s = t.Update(Info(2, 4, 200));      // far past the estimate

        // 0 + 60 * (4 - 2) = 120
        Assert.Equal(TimeSpan.FromSeconds(120), s.LoopRemaining);
    }

    [Fact]
    public void Resets_when_the_recipe_name_changes()
    {
        var t = new LoopProgressTracker();
        t.Update(Info(1, 4, 0));
        t.Update(Info(2, 4, 60));               // statistics exist for STACK
        var s = t.Update(Info(1, 4, 5, name: "OTHER"));

        Assert.Null(s.IterationEstimate);
        Assert.Equal(TimeSpan.Zero, s.IterationElapsed);
    }

    [Fact]
    public void Resets_when_the_recipe_clock_goes_backwards()
    {
        var t = new LoopProgressTracker();
        t.Update(Info(1, 4, 0));
        t.Update(Info(2, 4, 60));
        var s = t.Update(Info(1, 4, 2));        // same recipe re-run from the top

        Assert.Null(s.IterationEstimate);
    }

    [Fact]
    public void Out_of_range_stack_numbers_are_clamped()
    {
        Assert.Equal(4, new LoopProgressTracker().Update(Info(9, 4, 5)).CurrentStack);
        Assert.Equal(1, new LoopProgressTracker().Update(Info(0, 4, 5)).CurrentStack);
    }
}
