using System.Diagnostics;

namespace APS.SystemControl.Core.Classes;

/// <summary>
/// Wraps a potentially-blocking hardware read with a hard timeout and slow-call
/// warning. Designed for the 200 ms polling loop in <c>Core.ReadState</c>: if a
/// single driver hangs on a serial read, the entire cycle stalls and other
/// hardware loses state updates. The watchdog offloads the call to the
/// thread-pool and waits up to <see cref="HardTimeoutMs"/>; on timeout the task
/// is detached (it finishes eventually when the underlying serial timeout
/// expires) and the caller returns so the next device can be polled.
/// </summary>
public static class PollWatchdog
{
    /// <summary>Log a warning when a ReadFeedback takes longer than this.</summary>
    public const int SlowThresholdMs = 150;

    /// <summary>Give up waiting for a ReadFeedback after this many ms.</summary>
    public const int HardTimeoutMs = 500;

    /// <summary>
    /// Runs <paramref name="read"/> with a hard timeout. Returns true when the
    /// call completed within the budget, false when it timed out. Any
    /// exception thrown by <paramref name="read"/> is reported via
    /// <paramref name="onError"/> and treated as a completed call (the driver
    /// is expected to update its own IsResponding on failure).
    /// </summary>
    public static bool RunWithTimeout(
        string deviceId,
        Action read,
        Action? onTimeout = null,
        Action<Exception>? onError = null)
    {
        ArgumentNullException.ThrowIfNull(read);

        var sw = Stopwatch.StartNew();
        var task = Task.Run(() =>
        {
            try { read(); }
            catch (Exception ex) { onError?.Invoke(ex); }
        });

        if (!task.Wait(HardTimeoutMs))
        {
            Trace.TraceWarning(
                $"PollWatchdog[{deviceId}]: ReadFeedback exceeded {HardTimeoutMs} ms — detaching task.");
            onTimeout?.Invoke();
            return false;
        }

        sw.Stop();
        if (sw.ElapsedMilliseconds > SlowThresholdMs)
        {
            Trace.TraceWarning(
                $"PollWatchdog[{deviceId}]: ReadFeedback slow ({sw.ElapsedMilliseconds} ms).");
        }
        return true;
    }
}
