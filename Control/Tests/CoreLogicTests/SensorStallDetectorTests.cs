using APS.SystemControl.Core.Classes;

namespace CoreLogicTests;

/// <summary>
/// Stall detection used by pwait: warn once, recover once, so the log is not
/// spammed and a stuck sensor is not missed.
/// </summary>
public class SensorStallDetectorTests
{
    [Fact]
    public void No_warning_before_threshold_reached()
    {
        var d = new SensorStallDetector(stallSamples: 3);
        Assert.Null(d.Tick(1.0f).Warning);
        Assert.Null(d.Tick(1.0f).Warning); // stableCount = 1
        Assert.Null(d.Tick(1.0f).Warning); // stableCount = 2 (not yet)
    }

    [Fact]
    public void Warning_fires_exactly_at_threshold()
    {
        var d = new SensorStallDetector(stallSamples: 3);
        d.Tick(1.0f);                       // prime lastValue
        d.Tick(1.0f);                       // stableCount = 1
        d.Tick(1.0f);                       // stableCount = 2
        var r = d.Tick(1.0f);               // stableCount = 3 → warning
        Assert.NotNull(r.Warning);
        Assert.Equal(1.0f, r.Warning!.Value);
        Assert.Null(r.Recovered);
    }

    [Fact]
    public void Warning_fires_only_once_while_stuck()
    {
        var d = new SensorStallDetector(stallSamples: 2);
        d.Tick(1.0f);
        d.Tick(1.0f);
        var first = d.Tick(1.0f);           // warning fires here (count=2)
        Assert.NotNull(first.Warning);

        for (int i = 0; i < 10; i++)
            Assert.Null(d.Tick(1.0f).Warning);
    }

    [Fact]
    public void Recovery_fires_when_value_changes_after_warning()
    {
        var d = new SensorStallDetector(stallSamples: 2);
        d.Tick(1.0f);
        d.Tick(1.0f);
        d.Tick(1.0f);   // warning

        var r = d.Tick(2.0f);
        Assert.Null(r.Warning);
        Assert.NotNull(r.Recovered);
        Assert.Equal(2.0f, r.Recovered!.Value);
    }

    [Fact]
    public void Recovery_does_not_fire_if_never_warned()
    {
        var d = new SensorStallDetector(stallSamples: 100);
        d.Tick(1.0f);
        var r = d.Tick(2.0f);   // change, but no warning ever fired
        Assert.Null(r.Warning);
        Assert.Null(r.Recovered);
    }

    [Fact]
    public void Rewarn_fires_if_sensor_sticks_again_after_recovery()
    {
        var d = new SensorStallDetector(stallSamples: 2);
        d.Tick(1.0f);                // prime lastValue
        d.Tick(1.0f);                // stableCount=1
        d.Tick(1.0f);                // stableCount=2 → warning
        d.Tick(2.0f);                // recovery, counters reset
        d.Tick(2.0f);                // stableCount=1
        var r = d.Tick(2.0f);        // stableCount=2 → warning again
        Assert.NotNull(r.Warning);
        Assert.Equal(2.0f, r.Warning!.Value);
    }

    [Fact]
    public void Small_changes_under_epsilon_count_as_stuck()
    {
        var d = new SensorStallDetector(stallSamples: 2, epsilon: 0.1f);
        d.Tick(1.00f);           // prime
        d.Tick(1.05f);           // stableCount=1  (diff=0.05 < 0.1)
        var r = d.Tick(1.02f);   // stableCount=2  (diff=0.03 < 0.1) → warning
        Assert.NotNull(r.Warning);
    }

    [Fact]
    public void Changes_over_epsilon_reset_the_counter()
    {
        var d = new SensorStallDetector(stallSamples: 2, epsilon: 0.01f);
        d.Tick(1.0f);
        d.Tick(1.0f);          // count = 1
        d.Tick(2.0f);          // reset (jumped by 1.0 > epsilon)
        var r = d.Tick(2.0f);   // count = 1 after reset — no warning yet
        Assert.Null(r.Warning);
    }

    [Fact]
    public void Reset_clears_counters_and_suppresses_recovery()
    {
        var d = new SensorStallDetector(stallSamples: 2);
        d.Tick(1.0f); d.Tick(1.0f); d.Tick(1.0f);  // warning

        d.Reset();

        var r = d.Tick(2.0f);
        Assert.Null(r.Warning);
        Assert.Null(r.Recovered);   // reset cleared the warned flag
    }

    [Fact]
    public void Ctor_rejects_invalid_args()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SensorStallDetector(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SensorStallDetector(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SensorStallDetector(1, float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SensorStallDetector(1, -0.1f));
    }
}
