using APS.SystemEmulator.Engine;

namespace EmulatorTests;

public class MotionLoopTrackerTests
{
    [Fact]
    public void Tick_IdleToMoving_ReturnsStart()
    {
        var t = new MotionLoopTracker();
        bool moving = false;
        t.SetSources([() => moving]);

        Assert.Equal(MotionLoopAction.None, t.Tick(200));
        moving = true;
        Assert.Equal(MotionLoopAction.Start, t.Tick(200));
        Assert.True(t.LoopActive);
    }

    [Fact]
    public void Tick_WhileMoving_DoesNotRepeatStart()
    {
        var t = new MotionLoopTracker();
        t.SetSources([() => true]);

        Assert.Equal(MotionLoopAction.Start, t.Tick(200));
        Assert.Equal(MotionLoopAction.None, t.Tick(200));
        Assert.Equal(MotionLoopAction.None, t.Tick(200));
    }

    [Fact]
    public void Tick_MovingToIdle_ReturnsStop()
    {
        var t = new MotionLoopTracker();
        bool moving = true;
        t.SetSources([() => moving]);

        Assert.Equal(MotionLoopAction.Start, t.Tick(200));
        moving = false;
        Assert.Equal(MotionLoopAction.Stop, t.Tick(200));
        Assert.False(t.LoopActive);
        Assert.Equal(MotionLoopAction.None, t.Tick(200));
    }

    [Fact]
    public void AnyOfManySources_DrivesTheLoop()
    {
        var t = new MotionLoopTracker();
        bool a = false, b = false;
        t.SetSources([() => a, () => b]);

        Assert.Equal(MotionLoopAction.None, t.Tick(200));
        b = true;
        Assert.Equal(MotionLoopAction.Start, t.Tick(200));
        a = true; b = false;                       // handover between movers
        Assert.Equal(MotionLoopAction.None, t.Tick(200));
        a = false;
        Assert.Equal(MotionLoopAction.Stop, t.Tick(200));
    }

    [Fact]
    public void NotifyOneShot_WhileMoving_ReissuesStartAfterDelay()
    {
        var t = new MotionLoopTracker();
        t.SetSources([() => true]);
        Assert.Equal(MotionLoopAction.Start, t.Tick(200));

        t.NotifyOneShot();                          // valve chpok cut the channel
        Assert.Equal(MotionLoopAction.None, t.Tick(200));   // 200 ms < 250 ms
        Assert.Equal(MotionLoopAction.Start, t.Tick(200));  // 400 ms >= 250 ms
        Assert.Equal(MotionLoopAction.None, t.Tick(200));   // re-issued once only
    }

    [Fact]
    public void NotifyOneShot_WhileIdle_IsIgnored()
    {
        var t = new MotionLoopTracker();
        t.SetSources([() => false]);
        Assert.Equal(MotionLoopAction.None, t.Tick(200));

        t.NotifyOneShot();
        Assert.Equal(MotionLoopAction.None, t.Tick(200));
        Assert.Equal(MotionLoopAction.None, t.Tick(200));
    }

    [Fact]
    public void ThrowingSource_IsTreatedAsNotMoving()
    {
        var t = new MotionLoopTracker();
        t.SetSources([() => throw new InvalidOperationException("boom")]);

        Assert.Equal(MotionLoopAction.None, t.Tick(200));
        Assert.False(t.LoopActive);
    }

    [Fact]
    public void Clear_ResetsStateAndSources()
    {
        var t = new MotionLoopTracker();
        t.SetSources([() => true]);
        Assert.Equal(MotionLoopAction.Start, t.Tick(200));

        t.Clear();                                  // caller stops the player itself
        Assert.False(t.LoopActive);
        Assert.Equal(MotionLoopAction.None, t.Tick(200));
    }

    [Fact]
    public void Deactivate_AllowsRestartOnNextTick()
    {
        var t = new MotionLoopTracker();
        t.SetSources([() => true]);
        Assert.Equal(MotionLoopAction.Start, t.Tick(200));

        t.Deactivate();                             // sound checkbox unticked...
        Assert.False(t.LoopActive);
        Assert.Equal(MotionLoopAction.Start, t.Tick(200)); // ...re-ticked while still moving
    }

    [Fact]
    public void CutPending_IsDiscardedAcrossStopStartCycle()
    {
        var t = new MotionLoopTracker();
        bool moving = true;
        t.SetSources([() => moving]);
        Assert.Equal(MotionLoopAction.Start, t.Tick(200));

        t.NotifyOneShot();
        moving = false;
        Assert.Equal(MotionLoopAction.Stop, t.Tick(200));   // cut discarded here
        moving = true;
        Assert.Equal(MotionLoopAction.Start, t.Tick(200));  // fresh start
        Assert.Equal(MotionLoopAction.None, t.Tick(200));   // no phantom re-issue
        Assert.Equal(MotionLoopAction.None, t.Tick(200));
    }
}
