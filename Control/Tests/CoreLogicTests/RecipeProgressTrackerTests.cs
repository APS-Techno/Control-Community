using APS.ProcessPanel.Contracts;
using APS.SystemControl.Core.Remote.Agent;
using Xunit;

namespace CoreLogicTests;

public class RecipeProgressTrackerTests : IDisposable
{
    private static readonly DateTime T0 = new(2026, 9, 2, 13, 25, 53);
    private DateTime _now = T0;

    public RecipeProgressTrackerTests()
    {
        RecipeProgressTracker.Reset();
        RecipeProgressTracker.Clock = () => _now;
    }

    public void Dispose()
    {
        RecipeProgressTracker.Reset();
        RecipeProgressTracker.Clock = () => DateTime.Now;
    }

    private static RecipeInfo Running(int step, string desc, double elapsedS, double? remainingS = null) =>
        new("Demo", step, 28, desc, RecipeState.Running,
            TimeSpan.FromSeconds(elapsedS),
            remainingS is null ? null : TimeSpan.FromSeconds(remainingS.Value));

    [Fact]
    public void Update_then_read_then_reset()
    {
        Assert.Null(RecipeProgressTracker.Current);
        var info = new RecipeInfo("Demo", 3, 10, "deposit Cu", RecipeState.Running,
            TimeSpan.FromSeconds(42), TimeSpan.FromSeconds(100));
        RecipeProgressTracker.Update(info);
        Assert.Equal(info, RecipeProgressTracker.Current);
        RecipeProgressTracker.Reset();
        Assert.Null(RecipeProgressTracker.Current);
    }

    // The tracker only stores what the worker sends at a step boundary, so during a long
    // step every poll returns the same elapsed time. A poller cannot tell "long step in
    // progress" from "worker hung". The snapshot must carry the clock forward.
    [Fact]
    public void Snapshot_elapsed_advances_between_polls_without_a_new_step()
    {
        RecipeProgressTracker.Update(Running(9, "pwait 2.50mT 300", 17));

        _now = T0.AddSeconds(30);
        var s = RecipeProgressTracker.Snapshot();

        Assert.NotNull(s.Progress);
        Assert.Equal(9, s.Progress!.CurrentStep);
        Assert.Equal(TimeSpan.FromSeconds(47), s.Progress.Elapsed);
        Assert.Equal(TimeSpan.FromSeconds(30), s.StepElapsed);
        Assert.Equal(T0, s.UpdatedAt);
    }

    // The estimate the runtime sends is a duration plan (RecipeTimeEstimator), not an
    // average over step counts, so a poller must count it DOWN with the step's age rather
    // than re-derive it. Re-deriving is what made the number climb while a long step ran:
    // more elapsed over the same step index reads as "every remaining step got dearer".
    [Fact]
    public void Snapshot_counts_the_stored_estimate_down_while_a_step_runs()
    {
        RecipeProgressTracker.Update(Running(9, "pwait 2.50mT 300", 17, 120));

        _now = T0.AddSeconds(30);
        var s = RecipeProgressTracker.Snapshot();

        Assert.NotNull(s.Progress!.EstimatedRemaining);
        Assert.Equal(90, s.Progress.EstimatedRemaining!.Value.TotalSeconds, 3);
    }

    [Fact]
    public void Snapshot_remaining_floors_at_zero_when_a_step_overruns_its_estimate()
    {
        RecipeProgressTracker.Update(Running(9, "pwait 2.50mT 300", 17, 10));

        _now = T0.AddSeconds(30);
        var s = RecipeProgressTracker.Snapshot();

        Assert.Equal(TimeSpan.Zero, s.Progress!.EstimatedRemaining);
    }

    [Fact]
    public void Snapshot_reports_no_remaining_when_the_runtime_sent_none()
    {
        RecipeProgressTracker.Update(Running(9, "pwait 2.50mT 300", 17));

        _now = T0.AddSeconds(30);
        Assert.Null(RecipeProgressTracker.Snapshot().Progress!.EstimatedRemaining);
    }

    // After the failure protocol's last step the stored record still says Running (the protocol
    // never sends Complete), but Core.IsRecipeRunning is false. The caller passes that flag so
    // the clock stops with the worker instead of counting up forever.
    [Fact]
    public void Snapshot_freezes_elapsed_when_the_core_reports_no_recipe_running()
    {
        RecipeProgressTracker.Update(Running(20, "valve M3 false", 92));

        _now = T0.AddSeconds(600);
        var s = RecipeProgressTracker.Snapshot(recipeRunning: false);

        Assert.Equal(TimeSpan.FromSeconds(92), s.Progress!.Elapsed);
        Assert.Equal(TimeSpan.FromSeconds(600), s.StepElapsed);
    }

    [Fact]
    public void Snapshot_does_not_advance_elapsed_when_not_running()
    {
        RecipeProgressTracker.Update(new RecipeInfo("Demo", 28, 28, "Complete", RecipeState.Complete,
            TimeSpan.FromSeconds(63), null));

        _now = T0.AddSeconds(30);
        var s = RecipeProgressTracker.Snapshot();

        Assert.Equal(TimeSpan.FromSeconds(63), s.Progress!.Elapsed);
        Assert.Null(s.Progress.EstimatedRemaining);
    }

    // Within a second of the worker sending an Error, the failure protocol starts sending
    // Running progress under the SAME recipe name, so a poller never sees the Error. The
    // last terminal outcome must be kept beside progress.
    [Fact]
    public void Snapshot_keeps_the_terminal_outcome_when_the_failure_protocol_overwrites_progress()
    {
        RecipeProgressTracker.Update(Running(18, "deposit B4C 4 false", 63));
        _now = T0.AddSeconds(63);
        RecipeProgressTracker.Update(new RecipeInfo("Demo", 0, 28, "Unknown position 'false'",
            RecipeState.Error, TimeSpan.FromSeconds(63), null));
        _now = T0.AddSeconds(64);
        RecipeProgressTracker.Update(new RecipeInfo("Demo", 0, 20, "Starting...", RecipeState.Running,
            TimeSpan.FromSeconds(64), null));
        RecipeProgressTracker.Update(new RecipeInfo("Demo", 3, 20, "power PSU-C false", RecipeState.Running,
            TimeSpan.FromSeconds(65), null));

        var s = RecipeProgressTracker.Snapshot();

        Assert.Equal("power PSU-C false", s.Progress!.StepDescription);
        Assert.NotNull(s.LastOutcome);
        Assert.Equal(RecipeState.Error, s.LastOutcome!.State);
        Assert.Equal("Unknown position 'false'", s.LastOutcome.Message);
        Assert.Equal("Demo", s.LastOutcome.Name);
        Assert.Equal(T0.AddSeconds(63), s.LastOutcome.At);
    }

    [Fact]
    public void Snapshot_records_abort_as_an_outcome()
    {
        RecipeProgressTracker.Update(Running(5, "pwait 0.05mT 120", 10));
        _now = T0.AddSeconds(79);
        RecipeProgressTracker.Update(new RecipeInfo("Demo", 0, 3, RecipeProgressTracker.AbortedDescription,
            RecipeState.Idle, TimeSpan.Zero, null));

        var s = RecipeProgressTracker.Snapshot();

        Assert.NotNull(s.LastOutcome);
        Assert.Equal(RecipeState.Idle, s.LastOutcome!.State);
        Assert.Equal(RecipeProgressTracker.AbortedDescription, s.LastOutcome.Message);
    }

    [Fact]
    public void Snapshot_records_completion_as_an_outcome_and_a_new_run_replaces_it()
    {
        RecipeProgressTracker.Update(new RecipeInfo("First", 28, 28, "Complete", RecipeState.Complete,
            TimeSpan.FromSeconds(63), null));
        Assert.Equal(RecipeState.Complete, RecipeProgressTracker.Snapshot().LastOutcome!.State);
        Assert.Equal("First", RecipeProgressTracker.Snapshot().LastOutcome!.Name);

        _now = T0.AddSeconds(100);
        RecipeProgressTracker.Update(new RecipeInfo("Second", 0, 5, "Cannot start recipe: 'Holder'",
            RecipeState.Error, TimeSpan.Zero, null));

        var o = RecipeProgressTracker.Snapshot().LastOutcome!;
        Assert.Equal("Second", o.Name);
        Assert.Equal(RecipeState.Error, o.State);
    }

    [Fact]
    public void Snapshot_reports_the_failure_protocol_phase()
    {
        Assert.False(RecipeProgressTracker.Snapshot().FailureProtocolActive);
        RecipeProgressTracker.SetFailureProtocolActive(true);
        Assert.True(RecipeProgressTracker.Snapshot().FailureProtocolActive);
        RecipeProgressTracker.SetFailureProtocolActive(false);
        Assert.False(RecipeProgressTracker.Snapshot().FailureProtocolActive);
    }

    [Fact]
    public void Reset_clears_progress_outcome_and_phase()
    {
        RecipeProgressTracker.Update(new RecipeInfo("Demo", 0, 1, "boom", RecipeState.Error, TimeSpan.Zero, null));
        RecipeProgressTracker.SetFailureProtocolActive(true);
        RecipeProgressTracker.Reset();

        var s = RecipeProgressTracker.Snapshot();
        Assert.Null(s.Progress);
        Assert.Null(s.LastOutcome);
        Assert.Null(s.UpdatedAt);
        Assert.Null(s.StepElapsed);
        Assert.False(s.FailureProtocolActive);
    }

    [Fact]
    public void Receipt_is_empty_before_any_run()
    {
        var (quotes, deposits) = RecipeProgressTracker.Receipt();
        Assert.Empty(quotes);
        Assert.Equal(0, deposits);
    }

    [Fact]
    public void Receipt_keeps_quotes_in_order_and_counts_deposits()
    {
        RecipeProgressTracker.RecordBasePressureLog("Base pressure (C1, C1_pm): 1.00E-006 Torr");
        RecipeProgressTracker.RecordBasePressureLog("Base pressure (C2, C2_pm): 2.00E-006 Torr");
        RecipeProgressTracker.RecordDepositCompleted();
        RecipeProgressTracker.RecordDepositCompleted();

        var (quotes, deposits) = RecipeProgressTracker.Receipt();
        Assert.Equal(
            ["Base pressure (C1, C1_pm): 1.00E-006 Torr", "Base pressure (C2, C2_pm): 2.00E-006 Torr"], quotes);
        Assert.Equal(2, deposits);
    }

    [Fact]
    public void BeginRun_and_Reset_clear_the_receipt()
    {
        RecipeProgressTracker.RecordBasePressureLog("x");
        RecipeProgressTracker.RecordDepositCompleted();
        RecipeProgressTracker.BeginRun();
        Assert.Equal((0, 0), (RecipeProgressTracker.Receipt().BasePressureLogQuotes.Length,
            RecipeProgressTracker.Receipt().ExecutedDepositsCount));

        RecipeProgressTracker.RecordBasePressureLog("y");
        RecipeProgressTracker.RecordDepositCompleted();
        RecipeProgressTracker.Reset();
        Assert.Equal((0, 0), (RecipeProgressTracker.Receipt().BasePressureLogQuotes.Length,
            RecipeProgressTracker.Receipt().ExecutedDepositsCount));
    }

    [Fact]
    public void Receipt_array_is_a_copy()
    {
        RecipeProgressTracker.RecordBasePressureLog("a");
        var first = RecipeProgressTracker.Receipt().BasePressureLogQuotes;
        RecipeProgressTracker.RecordBasePressureLog("b");
        Assert.Single(first);
        Assert.Equal(2, RecipeProgressTracker.Receipt().BasePressureLogQuotes.Length);
    }

    [Fact]
    public void WatchCounters_increment_stepSequence_on_running_steps_and_ignore_failure_protocol()
    {
        RecipeProgressTracker.BeginRun();
        Assert.Equal(0, RecipeProgressTracker.WatchCounters().StepSequence);

        RecipeProgressTracker.Update(Running(1, "valve V1 open", 1));
        RecipeProgressTracker.Update(Running(2, "deposit Ru 31.26 Home", 5));
        Assert.Equal(2, RecipeProgressTracker.WatchCounters().StepSequence);

        RecipeProgressTracker.SetFailureProtocolActive(true);
        RecipeProgressTracker.Update(Running(1, "power PSU_C off", 6));
        Assert.Equal(2, RecipeProgressTracker.WatchCounters().StepSequence);

        RecipeProgressTracker.BeginRun();
        Assert.Equal(0, RecipeProgressTracker.WatchCounters().StepSequence);
    }

    [Fact]
    public void WatchCounters_track_nested_loops_and_clear_on_pop_and_reset()
    {
        RecipeProgressTracker.BeginRun();
        RecipeProgressTracker.PushLoop(2);
        RecipeProgressTracker.SetLoopIteration(1);
        RecipeProgressTracker.PushLoop(50);
        RecipeProgressTracker.SetLoopIteration(5);

        var snap = RecipeProgressTracker.WatchCounters();
        Assert.Equal(5, snap.CurrentLoopIteration);
        Assert.Equal(2, snap.LoopStack.Length);
        Assert.Equal(new LoopFrame(1, 2), snap.LoopStack[0]);
        Assert.Equal(new LoopFrame(5, 50), snap.LoopStack[1]);

        RecipeProgressTracker.PopLoop();
        Assert.Equal(1, RecipeProgressTracker.WatchCounters().CurrentLoopIteration);
        Assert.Single(RecipeProgressTracker.WatchCounters().LoopStack);

        RecipeProgressTracker.Reset();
        Assert.Empty(RecipeProgressTracker.WatchCounters().LoopStack);
        Assert.Equal(0, RecipeProgressTracker.WatchCounters().CurrentLoopIteration);
    }
}
