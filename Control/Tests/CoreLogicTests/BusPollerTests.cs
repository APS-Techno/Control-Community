using System.Threading;
using System.Threading.Tasks;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.HWClasses.Base;
using APS.SystemControl.Core.Threads;

namespace CoreLogicTests;

public class BusPollerTests
{
    private sealed class CountingSensor : BaseSensor
    {
        public int Polls;
        public CountingSensor() : base(new BaseDeviceParams("s", 0, "")) { Connected = true; }
        protected override float ReadHW() { Polls++; return 1f; }
    }

    // Records whether the poll ran on a thread-pool thread. Device reads must run on the poller's own
    // dedicated (LongRunning) thread, not the pool, to avoid starving it with one bus poller per
    // serial device.
    private sealed class ThreadCapturingSensor : BaseSensor
    {
        public volatile bool SawPoolThread;
        public volatile bool Polled;
        public ThreadCapturingSensor() : base(new BaseDeviceParams("tc", 0, "")) { Connected = true; }
        protected override float ReadHW()
        {
            if (Thread.CurrentThread.IsThreadPoolThread) SawPoolThread = true;
            Polled = true;
            return 1f;
        }
    }

    private sealed class ThrowingSensor : BaseSensor
    {
        public ThrowingSensor() : base(new BaseDeviceParams("boom", 0, "")) { Connected = true; }
        protected override float ReadHW() => throw new System.InvalidOperationException("boom");
    }

    [Fact]
    public async Task Run_polls_off_the_thread_pool()
    {
        var s = new ThreadCapturingSensor();
        var poller = new BusPoller("test", new IBaseComponent[] { s }, null);
        using var cts = new CancellationTokenSource();

        var task = poller.Run(cts.Token);
        await Task.Delay(80);
        cts.Cancel();
        await task;

        Assert.True(s.Polled);
        Assert.False(s.SawPoolThread);
    }

    [Fact]
    public async Task Run_isolates_a_throwing_device_from_its_siblings()
    {
        var boom = new ThrowingSensor();
        var ok = new CountingSensor();
        // Throwing device first: the sibling after it must still be polled.
        var poller = new BusPoller("test", new IBaseComponent[] { boom, ok }, null);
        using var cts = new CancellationTokenSource();

        var task = poller.Run(cts.Token);
        await Task.Delay(80);
        cts.Cancel();
        await task;

        Assert.True(ok.Polls > 0);
        Assert.NotEqual(0, poller.LastTickUtc);
    }

    [Fact]
    public async Task Run_polls_each_device_and_stamps_tick()
    {
        var a = new CountingSensor();
        var b = new CountingSensor();
        var poller = new BusPoller("test", new IBaseComponent[] { a, b }, prePass: null);
        using var cts = new CancellationTokenSource();

        var task = poller.Run(cts.Token);
        await Task.Delay(80);
        cts.Cancel();
        await task;

        Assert.True(a.Polls > 0);
        Assert.True(b.Polls > 0);
        Assert.NotEqual(0, poller.LastTickUtc);
    }

    [Fact]
    public async Task Run_invokes_prePass_each_cycle()
    {
        int prePass = 0;
        var poller = new BusPoller("test", System.Array.Empty<IBaseComponent>(), prePass: () => prePass++);
        using var cts = new CancellationTokenSource();

        var task = poller.Run(cts.Token);
        await Task.Delay(80);
        cts.Cancel();
        await task;

        Assert.True(prePass > 0);
    }

    [Fact]
    public async Task Run_honors_min_cycle_period()
    {
        // A single fast device with a 50 ms min period must be paced to ~1 poll
        // per 50 ms — NOT hammered at the 1 ms spin-floor rate (which is what
        // flooded the GAUGE_A serial bus). ~275 ms / 50 ms ≈ 5-6 cycles; a generous
        // range absorbs scheduling jitter while staying far below the ~275 polls
        // an unpaced (period-0) bus would rack up in the same window.
        var s = new CountingSensor();
        var poller = new BusPoller("throttled", new IBaseComponent[] { s }, null, minCyclePeriodMs: 50);
        using var cts = new CancellationTokenSource();

        var task = poller.Run(cts.Token);
        await Task.Delay(275);
        cts.Cancel();
        await task;

        Assert.InRange(s.Polls, 2, 12);
    }

    [Fact]
    public async Task LastTickUtc_advances_across_cycles()
    {
        var s = new CountingSensor();
        var poller = new BusPoller("test", new IBaseComponent[] { s }, null);
        using var cts = new CancellationTokenSource();
        var task = poller.Run(cts.Token);

        await Task.Delay(40);
        long t1 = poller.LastTickUtc;
        await Task.Delay(40);
        long t2 = poller.LastTickUtc;

        cts.Cancel(); await task;
        Assert.True(t2 >= t1 && t1 != 0);
    }
}
