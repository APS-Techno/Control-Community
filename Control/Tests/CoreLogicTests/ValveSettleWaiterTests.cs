using APS.SystemControl.Core.Classes;

namespace CoreLogicTests;

public class ValveSettleWaiterTests
{
    [Fact]
    public async Task Returns_when_the_sensor_reports_the_commanded_state()
    {
        // Gate reads open for the first three polls, then the closed-limit trips.
        int reads = 0;
        Func<bool> readOpen = () => ++reads <= 3;

        await ValveSettleWaiter.WaitAsync(readOpen, targetOpen: false,
            pollMs: 5, timeoutMs: 5_000, CancellationToken.None);

        Assert.Equal(4, reads);
    }

    [Fact]
    public async Task Returns_at_once_when_already_in_position()
    {
        int reads = 0;
        Func<bool> readOpen = () => { reads++; return false; };
        var sw = System.Diagnostics.Stopwatch.StartNew();

        await ValveSettleWaiter.WaitAsync(readOpen, targetOpen: false,
            pollMs: 500, timeoutMs: 5_000, CancellationToken.None);

        Assert.Equal(1, reads);
        Assert.True(sw.ElapsedMilliseconds < 400, $"waited {sw.ElapsedMilliseconds} ms for a valve already in position");
    }

    [Fact]
    public async Task Times_out_naming_the_state_it_was_waiting_for()
    {
        Func<bool> readOpen = () => true;   // never closes

        var ex = await Assert.ThrowsAsync<TimeoutException>(() =>
            ValveSettleWaiter.WaitAsync(readOpen, targetOpen: false,
                pollMs: 5, timeoutMs: 50, CancellationToken.None));

        Assert.Contains("did not reach closed", ex.Message);
        Assert.Contains("still reports open", ex.Message);
    }

    [Fact]
    public async Task Propagates_cancellation()
    {
        using var cts = new CancellationTokenSource();
        Func<bool> readOpen = () => { cts.Cancel(); return true; };   // abort during the wait

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ValveSettleWaiter.WaitAsync(readOpen, targetOpen: false,
                pollMs: 5, timeoutMs: 5_000, cts.Token));
    }
}
