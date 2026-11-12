using APS.ProcessPanel.Contracts;

namespace APS.SystemControl.Core.Remote.Agent;

/// <summary>Terminal result of the most recent recipe run: Complete, Error, or an Idle
/// update carrying <see cref="RecipeProgressTracker.AbortedDescription"/>.</summary>
internal sealed record RecipeOutcome(string Name, RecipeState State, string Message, DateTime At);

/// <summary>What a poller should see. <see cref="Progress"/> is the last worker update with
/// its clock carried forward to now while the recipe is running; <see cref="StepElapsed"/> is
/// how long the current step has been in progress; <see cref="LastOutcome"/> survives the
/// failure protocol's own progress updates.</summary>
internal sealed record RecipeStatusSnapshot(
    RecipeInfo? Progress,
    TimeSpan? StepElapsed,
    DateTime? UpdatedAt,
    RecipeOutcome? LastOutcome,
    bool FailureProtocolActive);

/// <summary>Last recipe-progress snapshot, readable from any thread. Fed by CoreThread.SendRecipeProgress.
///
/// The worker reports progress only at step boundaries, and it stamps the elapsed time into that
/// report. Between boundaries — a 300 s <c>pwait</c>, a 100 s <c>deposit</c> — the stored record
/// does not change, so a poller reading <see cref="Current"/> alone gets the same answer on every
/// call and cannot distinguish a long step from a stalled worker. <see cref="Snapshot"/> carries
/// the clock forward and adds the per-step age.
///
/// A failed or aborted run sends one terminal update and is then immediately followed by the
/// FAILURE_PROTOCOL's own Running updates under the same recipe name, which would overwrite the
/// terminal state within a second. <see cref="Snapshot"/> keeps it as <see cref="RecipeStatusSnapshot.LastOutcome"/>.
/// </summary>
internal static class RecipeProgressTracker
{
    /// <summary>The literal CoreThread sends, with <see cref="RecipeState.Idle"/>, when a run is cancelled.
    /// Shared here so the tracker's outcome detection and the worker cannot drift apart.</summary>
    internal const string AbortedDescription = "Aborted";

    private static readonly object _sync = new();
    private static RecipeInfo? _current;
    private static DateTime? _updatedAt;
    private static RecipeOutcome? _lastOutcome;
    private static bool _failureProtocolActive;

    /// <summary>Time source; tests substitute a fixed clock. Local time, matching CoreThread's
    /// <c>DateTime.Now</c> anchoring of recipe start times.</summary>
    internal static Func<DateTime> Clock { get; set; } = () => DateTime.Now;

    internal static RecipeInfo? Current { get { lock (_sync) return _current; } }

    internal static void Update(RecipeInfo info)
    {
        var now = Clock();
        lock (_sync)
        {
            _current = info;
            _updatedAt = now;
            if (IsTerminal(info))
                _lastOutcome = new RecipeOutcome(info.Name, info.State, info.StepDescription, now);
        }
    }

    /// <summary>Set for the duration of <c>CoreThread.RunFailureProtocol</c>, whose progress
    /// updates otherwise look like the user recipe still running.</summary>
    internal static void SetFailureProtocolActive(bool active) { lock (_sync) _failureProtocolActive = active; }

    internal static void Reset()
    {
        lock (_sync)
        {
            _current = null;
            _updatedAt = null;
            _lastOutcome = null;
            _failureProtocolActive = false;
        }
    }

    /// <param name="recipeRunning">Core's live running flag. When false the stored record is returned
    /// as-is: the worker has stopped, so its clock must not be carried forward.</param>
    internal static RecipeStatusSnapshot Snapshot(bool recipeRunning = true)
    {
        var now = Clock();
        RecipeInfo? current; DateTime? updatedAt; RecipeOutcome? outcome; bool fp;
        lock (_sync) { current = _current; updatedAt = _updatedAt; outcome = _lastOutcome; fp = _failureProtocolActive; }

        if (current is null || updatedAt is null)
            return new RecipeStatusSnapshot(null, null, null, outcome, fp);

        var stepElapsed = now - updatedAt.Value;
        if (stepElapsed < TimeSpan.Zero) stepElapsed = TimeSpan.Zero;

        var progress = current;
        if (recipeRunning && current.State == RecipeState.Running)
        {
            var elapsed = current.Elapsed + stepElapsed;

            // The runtime's estimate is a duration plan (CoreThread's RecipeTimeEstimator),
            // so between step boundaries it is counted DOWN by the current step's age, not
            // re-derived. Re-deriving it from step counts would make the number climb
            // while a long step runs.
            TimeSpan? remaining = current.EstimatedRemaining is TimeSpan stored
                ? (stored > stepElapsed ? stored - stepElapsed : TimeSpan.Zero)
                : null;

            progress = current with { Elapsed = elapsed, EstimatedRemaining = remaining };
        }

        return new RecipeStatusSnapshot(progress, stepElapsed, updatedAt, outcome, fp);
    }

    private static bool IsTerminal(RecipeInfo info) =>
        info.State is RecipeState.Complete or RecipeState.Error
        || (info.State == RecipeState.Idle && info.StepDescription == AbortedDescription);
}
