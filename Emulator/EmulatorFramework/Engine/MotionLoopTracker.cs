namespace APS.SystemEmulator.Engine;

public enum MotionLoopAction { None, Start, Stop }

/// <summary>
/// Decision logic for the looping motor sound: aggregates motion sources,
/// detects idle↔moving transitions, and schedules a loop re-issue after a
/// one-shot sound cut the single winmm channel. No audio, no timers.
/// Source delegates are invoked OUTSIDE the internal lock so they may
/// acquire device locks without lock-order inversion against
/// <see cref="NotifyOneShot"/>, which may arrive while a device lock is held.
/// </summary>
public sealed class MotionLoopTracker
{
    private const double OneShotReissueDelayMs = 250;

    private readonly object _gate = new();
    private IReadOnlyList<Func<bool>> _sources = [];
    private bool _loopActive;
    private bool _cutPending;
    private double _msSinceCut;

    public bool LoopActive { get { lock (_gate) return _loopActive; } }

    public void SetSources(IEnumerable<Func<bool>> sources)
    {
        lock (_gate) _sources = sources.ToList();
    }

    /// <summary>Drop all sources and reset. The caller stops the player itself.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _sources = [];
            _loopActive = false;
            _cutPending = false;
        }
    }

    /// <summary>Sound disabled: forget the active loop so a re-enable can restart it.</summary>
    public void Deactivate()
    {
        lock (_gate)
        {
            _loopActive = false;
            _cutPending = false;
        }
    }

    /// <summary>A one-shot sound just played, cutting the loop if it was sounding.</summary>
    public void NotifyOneShot()
    {
        lock (_gate)
        {
            if (!_loopActive) return;
            _cutPending = true;
            _msSinceCut = 0;
        }
    }

    /// <param name="elapsedMs">
    /// The caller's nominal poll interval in milliseconds (not a measured
    /// wall-clock delta); used only to pace the one-shot re-issue delay.
    /// </param>
    public MotionLoopAction Tick(double elapsedMs)
    {
        // Snapshot the source list OUTSIDE the lock so the delegates can
        // acquire device locks without creating a lock-order inversion with
        // NotifyOneShot (which arrives under a device lock).
        IReadOnlyList<Func<bool>> sources;
        lock (_gate) sources = _sources;

        bool anyMoving = false;
        foreach (var source in sources)
        {
            try { if (source()) { anyMoving = true; break; } }
            catch { /* sound is cosmetic — a throwing source counts as not moving */ }
        }

        lock (_gate)
        {
            if (anyMoving && !_loopActive)
            {
                _loopActive = true;
                _cutPending = false;
                return MotionLoopAction.Start;
            }
            if (!anyMoving && _loopActive)
            {
                _loopActive = false;
                _cutPending = false;
                return MotionLoopAction.Stop;
            }
            if (anyMoving && _cutPending)
            {
                _msSinceCut += elapsedMs;
                if (_msSinceCut >= OneShotReissueDelayMs)
                {
                    _cutPending = false;
                    return MotionLoopAction.Start;
                }
            }
            return MotionLoopAction.None;
        }
    }
}
