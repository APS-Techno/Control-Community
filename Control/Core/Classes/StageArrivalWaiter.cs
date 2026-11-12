namespace APS.SystemControl.Core.Classes;

/// <summary>
/// Pure polling helper that decides when a stage has arrived at its commanded
/// position.
/// </summary>
/// <remarks>
/// A fixed delay is unrelated to actual travel time: long HS moves (e.g.
/// <c>stage HS 280</c> in LOAD_SUBSTRATE) returned mid-motion and the recipe
/// advanced with the substrate at the wrong place (incident 2026-05-12).
///
/// Two consecutive in-tolerance reads are required so a single mid-motion
/// blip near the target (overshoot, queued reversal) cannot trigger a false
/// arrival.
/// </remarks>
public static class StageArrivalWaiter
{
    /// <summary>
    /// Polls <paramref name="readPosition"/> every <paramref name="pollMs"/>
    /// until two consecutive samples lie within <paramref name="tolerance"/>
    /// of <paramref name="target"/>. Returns the last sample.
    /// </summary>
    /// <param name="readPosition">Live position read; usually <c>() =&gt; stage.Position</c>.</param>
    /// <param name="target">Commanded position in mm.</param>
    /// <param name="tolerance">Half-band considered "arrived", in mm.</param>
    /// <param name="pollMs">Polling interval in ms.</param>
    /// <param name="timeoutMs">Maximum wait in ms.</param>
    /// <param name="ct">Cancellation token (recipe abort).</param>
    /// <exception cref="TimeoutException">Stage did not arrive in time.</exception>
    /// <exception cref="OperationCanceledException">Recipe was aborted.</exception>
    public static async Task<float> WaitAsync(
        Func<float> readPosition,
        float target,
        float tolerance,
        int pollMs,
        int timeoutMs,
        CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        float last = readPosition();
        bool prevInBand = Math.Abs(last - target) <= tolerance;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(pollMs, ct).ConfigureAwait(false);

            last = readPosition();
            bool inBand = Math.Abs(last - target) <= tolerance;
            if (inBand && prevInBand)
                return last;
            prevInBand = inBand;

            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException(
                    $"Stage did not reach {target:F1} mm within {timeoutMs} ms (last={last:F1} mm).");
        }
    }
}
