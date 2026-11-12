using APS.SystemControl.Core.Classes;

namespace CoreLogicTests;

public class PollWatchdogTests
{
    [Fact]
    public void RunWithTimeout_fast_call_completes_without_timeout_callback()
    {
        bool timedOut = false;
        bool ran = false;
        bool ok = PollWatchdog.RunWithTimeout(
            "fast-device",
            read: () => ran = true,
            onTimeout: () => timedOut = true);

        Assert.True(ok);
        Assert.True(ran);
        Assert.False(timedOut);
    }

    [Fact]
    public void RunWithTimeout_hung_call_triggers_onTimeout_and_returns_false()
    {
        // Sleep longer than the hard timeout.
        bool timedOut = false;
        bool ok = PollWatchdog.RunWithTimeout(
            "stuck-device",
            read: () => Thread.Sleep(PollWatchdog.HardTimeoutMs + 500),
            onTimeout: () => timedOut = true);

        Assert.False(ok);
        Assert.True(timedOut);
    }

    [Fact]
    public void RunWithTimeout_throwing_call_reports_onError_and_returns_true()
    {
        Exception? caught = null;
        bool timedOut = false;
        var expected = new InvalidOperationException("boom");
        bool ok = PollWatchdog.RunWithTimeout(
            "angry-device",
            read: () => throw expected,
            onTimeout: () => timedOut = true,
            onError:  ex => caught = ex);

        // Threw-but-completed should still count as "not timed out".
        Assert.True(ok);
        Assert.False(timedOut);
        Assert.Same(expected, caught);
    }

    [Fact]
    public void RunWithTimeout_null_read_throws_ArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => PollWatchdog.RunWithTimeout("x", read: null!));
    }
}
