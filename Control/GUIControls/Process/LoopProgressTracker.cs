using APS.ProcessPanel.Contracts;

namespace APS.SystemControl.GUIControls.Process;

/// <summary>
/// IterationEstimate and LoopRemaining are null until at least one iteration
/// has completed — the first iteration of a stack usually carries pump-down or
/// warm-up and is not representative of the rest.
/// </summary>
internal sealed record LoopProgressSnapshot(
    bool      IsActive,
    int       CurrentStack,
    int       TotalStacks,
    TimeSpan  IterationElapsed,
    TimeSpan? IterationEstimate,
    TimeSpan? LoopRemaining)
{
    public static readonly LoopProgressSnapshot Inactive =
        new(false, 0, 0, TimeSpan.Zero, null, null);
}

/// <summary>
/// Only the outermost loop is tracked, because that is all the
/// runtime publishes (CoreThread.Execute.cs — nested loops deliberately do not
/// touch the stack counters).
///
/// Timings anchor on <see cref="RecipeInfo.Elapsed"/> — the runtime's own recipe
/// clock — rather than wall clock, so the numbers cannot drift against the UI
/// update timer and need no timer of their own.
/// </summary>
internal sealed class LoopProgressTracker
{
    private string?  _recipeName;
    private int      _lastStack;
    private TimeSpan _iterationStartElapsed;
    private TimeSpan _lastElapsed;
    private int      _completedCount;
    private TimeSpan _completedTotal;

    public LoopProgressSnapshot Update(RecipeInfo? info)
    {
        if (info is null || info.TotalStacks <= 0 ||
            (info.State != RecipeState.Running && info.State != RecipeState.Paused))
        {
            Reset();
            return LoopProgressSnapshot.Inactive;
        }

        // A different recipe, or the same one restarted (recipe clock went
        // backwards) — previous iteration durations no longer apply.
        if (!string.Equals(_recipeName, info.Name, StringComparison.Ordinal) ||
            info.Elapsed < _lastElapsed)
        {
            Reset();
            _recipeName = info.Name;
        }
        _lastElapsed = info.Elapsed;

        // Guard the paint path: the runtime sets TotalStacks one statement before
        // CurrentStack, so a progress message can carry 0/N transiently.
        int current = Math.Clamp(info.CurrentStack, 1, info.TotalStacks);

        if (current != _lastStack)
        {
            if (_lastStack >= 1)
            {
                TimeSpan done = info.Elapsed - _iterationStartElapsed;
                if (done > TimeSpan.Zero)
                {
                    _completedCount++;
                    _completedTotal += done;
                }
            }
            _iterationStartElapsed = info.Elapsed;
            _lastStack = current;
        }

        TimeSpan iterationElapsed = info.Elapsed - _iterationStartElapsed;
        if (iterationElapsed < TimeSpan.Zero) iterationElapsed = TimeSpan.Zero;

        TimeSpan? estimate = _completedCount > 0
            ? _completedTotal / _completedCount
            : null;

        TimeSpan? remaining = null;
        if (estimate is TimeSpan est)
        {
            TimeSpan restOfCurrent = est - iterationElapsed;
            if (restOfCurrent < TimeSpan.Zero) restOfCurrent = TimeSpan.Zero;
            remaining = restOfCurrent + est * (info.TotalStacks - current);
        }

        return new LoopProgressSnapshot(
            IsActive:          true,
            CurrentStack:      current,
            TotalStacks:       info.TotalStacks,
            IterationElapsed:  iterationElapsed,
            IterationEstimate: estimate,
            LoopRemaining:     remaining);
    }

    public void Reset()
    {
        _recipeName            = null;
        _lastStack             = 0;
        _iterationStartElapsed = TimeSpan.Zero;
        _lastElapsed           = TimeSpan.Zero;
        _completedCount        = 0;
        _completedTotal        = TimeSpan.Zero;
    }
}
