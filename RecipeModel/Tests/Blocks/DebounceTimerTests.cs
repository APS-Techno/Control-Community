using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class DebounceTimerTests
{
    [StaFact]
    public void Elapsed_Fires_After_Quiet_Interval()
    {
        using var timer = new DebounceTimer(intervalMs: 50);
        int fired = 0;
        timer.Elapsed += () => fired++;

        timer.Ping();
        SpinWait.SpinUntil(() => { Application.DoEvents(); return fired == 1; }, TimeSpan.FromMilliseconds(500));

        Assert.Equal(1, fired);
    }

    [StaFact]
    public void Rapid_Pings_Coalesce_To_One_Elapsed()
    {
        using var timer = new DebounceTimer(intervalMs: 50);
        int fired = 0;
        timer.Elapsed += () => fired++;

        for (int i = 0; i < 5; i++)
        {
            timer.Ping();
            Thread.Sleep(5); // total ~25ms, less than the 50ms interval
            Application.DoEvents();
        }

        SpinWait.SpinUntil(() => { Application.DoEvents(); return fired >= 1; }, TimeSpan.FromMilliseconds(500));
        Assert.Equal(1, fired);
    }

    [StaFact]
    public void Cancel_Prevents_Firing()
    {
        using var timer = new DebounceTimer(intervalMs: 50);
        int fired = 0;
        timer.Elapsed += () => fired++;

        timer.Ping();
        timer.Cancel();
        Thread.Sleep(150);
        Application.DoEvents();

        Assert.Equal(0, fired);
    }
}
