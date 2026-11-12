namespace APS.SystemControl.Core.Classes;

/// <summary>
/// Pure polling helper that decides when a slow valve has physically reached
/// its commanded state, as reported by its position sensor.
/// </summary>
/// <remarks>
/// The panel gate valve GVM is commanded by a 250 ms firmware pulse and its state
/// is a closed-limit sensor, so <c>valve GVM close</c> returns while the gate is
/// still travelling; a following <c>valve A1 open</c> then sees GVM still open and
/// the vent is refused (2026-09-07 SYSTEM_AIR_RELEASE failure on the real machine).
/// Valves whose actuation is not synchronous with the write declare a positive
/// <see cref="BaseHWClasses.BaseValve.SettleTimeoutMs"/>; the valve op then waits
/// here until the sensor agrees, or fails the step loudly.
/// </remarks>
public static class ValveSettleWaiter
{
    /// <summary>
    /// Polls <paramref name="readOpen"/> every <paramref name="pollMs"/> until it
    /// reports <paramref name="targetOpen"/>. The first sample is taken immediately,
    /// so a valve that is already in position returns without a delay.
    /// </summary>
    /// <param name="readOpen">Live open/closed read; usually <c>() =&gt; valve.IsOpen</c>.</param>
    /// <param name="targetOpen">Commanded state: true = open, false = closed.</param>
    /// <param name="pollMs">Polling interval in ms.</param>
    /// <param name="timeoutMs">Maximum wait in ms.</param>
    /// <param name="ct">Cancellation token (recipe abort).</param>
    /// <exception cref="TimeoutException">The sensor never reported the commanded state.</exception>
    /// <exception cref="OperationCanceledException">Recipe was aborted.</exception>
    public static async Task WaitAsync(
        Func<bool> readOpen,
        bool targetOpen,
        int pollMs,
        int timeoutMs,
        CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (readOpen() == targetOpen)
                return;
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException(
                    $"did not reach {(targetOpen ? "open" : "closed")} within {timeoutMs / 1000.0:0.#} s " +
                    $"— position sensor still reports {(targetOpen ? "closed" : "open")}");
            await Task.Delay(pollMs, ct).ConfigureAwait(false);
        }
    }
}
