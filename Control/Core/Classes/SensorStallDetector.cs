namespace APS.SystemControl.Core.Classes;

/// <summary>
/// Stall-detection state machine for <c>pwait</c> sensor-polling loops (L8).
///
/// Fed one sensor reading per tick; remembers how many consecutive ticks
/// the value has been unchanged and emits a "may not be responding"
/// event the moment the stall threshold is crossed. Emits a recovery
/// event the first time a new value is seen after a warning.
///
/// Pure state machine — no timers, no threads.
/// </summary>
public sealed class SensorStallDetector
{
    /// <summary>
    /// How many consecutive identical readings trigger the stall warning.
    /// </summary>
    public int StallSamples { get; }

    /// <summary>
    /// Absolute floor for "same value" comparison. Readings closer than
    /// this are treated as unchanged.
    /// </summary>
    public float Epsilon { get; }

    private float? _lastValue;
    private int _stableCount;
    private bool _warned;

    public SensorStallDetector(int stallSamples = 25, float epsilon = 1e-6f)
    {
        if (stallSamples < 1)
            throw new ArgumentOutOfRangeException(nameof(stallSamples), stallSamples,
                "Stall samples must be at least 1.");
        if (!float.IsFinite(epsilon) || epsilon < 0f)
            throw new ArgumentOutOfRangeException(nameof(epsilon), epsilon,
                "Epsilon must be finite and non-negative.");
        StallSamples = stallSamples;
        Epsilon = epsilon;
    }

    public sealed record StallWarning(float Value);

    public sealed record StallRecovered(float Value);

    /// <summary>
    /// Both null when nothing notable happened.
    /// </summary>
    public readonly record struct TickResult(StallWarning? Warning, StallRecovered? Recovered);

    public TickResult Tick(float value)
    {
        TickResult result = default;

        if (_lastValue is float prev && Math.Abs(value - prev) < Epsilon)
        {
            _stableCount++;
            if (_stableCount == StallSamples && !_warned)
            {
                _warned = true;
                result = new TickResult(new StallWarning(value), null);
            }
        }
        else
        {
            if (_warned)
                result = new TickResult(null, new StallRecovered(value));
            _stableCount = 0;
            _warned = false;
        }

        _lastValue = value;
        return result;
    }

    public void Reset()
    {
        _lastValue = null;
        _stableCount = 0;
        _warned = false;
    }
}
