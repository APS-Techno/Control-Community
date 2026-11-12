using APS.SystemControl.Core.Classes;

namespace CoreLogicTests;

/// <summary>
/// A fixed <c>Task.Delay(3000)</c> in <c>OpStage</c> let LOAD_SUBSTRATE advance
/// with HS mid-motion (2026-05-12 incident). The waiter returns only when
/// |current - target| &lt;= tolerance for one extra stable tick.
/// </summary>
public class StageArrivalWaiterTests
{
    [Fact]
    public async Task Returns_when_position_converges_to_target()
    {
        // Stage starts far away and "walks" toward the target by 10 mm per poll.
        // After ~3 polls it lands within tolerance.
        float pos = 0f;
        const float target = 25f;
        Func<float> read = () =>
        {
            pos += 10f;
            if (pos > target) pos = target;
            return pos;
        };

        var final = await StageArrivalWaiter.WaitAsync(
            read, target, tolerance: 1.0f,
            pollMs: 5, timeoutMs: 5_000, CancellationToken.None);

        Assert.InRange(final, target - 1.0f, target + 1.0f);
    }

    [Fact]
    public async Task Tolerance_means_close_enough_counts_as_arrived()
    {
        // Stage parks 0.4 mm short of the commanded position — within 1 mm tolerance.
        Func<float> read = () => 24.6f;
        var final = await StageArrivalWaiter.WaitAsync(
            read, target: 25f, tolerance: 1.0f,
            pollMs: 5, timeoutMs: 1_000, CancellationToken.None);
        Assert.Equal(24.6f, final, precision: 2);
    }

    [Fact]
    public async Task Requires_two_consecutive_in_tolerance_samples()
    {
        // First read is in-tolerance, second is far away (transient stop or
        // mis-read during travel), third lands. We must not return on the
        // single in-tolerance blip.
        int call = 0;
        float[] sequence = [25f, 5f, 25f, 25f];
        Func<float> read = () => sequence[Math.Min(call++, sequence.Length - 1)];

        var final = await StageArrivalWaiter.WaitAsync(
            read, target: 25f, tolerance: 1.0f,
            pollMs: 1, timeoutMs: 5_000, CancellationToken.None);

        Assert.True(call >= 3, $"expected at least 3 reads, got {call}");
        Assert.Equal(25f, final, precision: 2);
    }

    [Fact]
    public async Task Throws_TimeoutException_when_stage_never_arrives()
    {
        Func<float> read = () => 0f;

        await Assert.ThrowsAsync<TimeoutException>(() =>
            StageArrivalWaiter.WaitAsync(
                read, target: 25f, tolerance: 1.0f,
                pollMs: 5, timeoutMs: 80, CancellationToken.None));
    }

    [Fact]
    public async Task Honors_cancellation_token()
    {
        Func<float> read = () => 0f;
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(40);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            StageArrivalWaiter.WaitAsync(
                read, target: 25f, tolerance: 1.0f,
                pollMs: 5, timeoutMs: 5_000, cts.Token));
    }
}
