using APS.Controls.Theme;
using Xunit;

namespace VacuumControls.Tests.Theme;

public class PendingStateTests
{
    // Exposes the protected pending API for testing without a message pump.
    private sealed class PendingProbe : ThemedVacuumControl
    {
        public void Report(bool actual) => ReportActualState(actual);
        public void Advance(int ms) => AdvancePendingWatchdog(ms);
    }

    [Fact]
    public void BeginPending_sets_IsPending_true()
    {
        using var p = new PendingProbe();
        Assert.False(p.IsPending);
        p.BeginPending(target: true);
        Assert.True(p.IsPending);
    }

    [Fact]
    public void ReportActualState_matching_target_clears_pending()
    {
        using var p = new PendingProbe();
        p.BeginPending(target: true);
        p.Report(true);
        Assert.False(p.IsPending);
    }

    [Fact]
    public void ReportActualState_other_value_keeps_pending()
    {
        using var p = new PendingProbe();
        p.BeginPending(target: true);
        p.Report(false);
        Assert.True(p.IsPending);
    }

    [Fact]
    public void Watchdog_timeout_clears_pending()
    {
        using var p = new PendingProbe();
        p.BeginPending(target: true, timeoutMs: 8000);
        p.Advance(7000);
        Assert.True(p.IsPending);
        p.Advance(2000);   // total 9000 > 8000
        Assert.False(p.IsPending);
    }

    [Fact]
    public void Disconnect_while_pending_clears_pending()
    {
        using var p = new PendingProbe { Connected = true };
        p.BeginPending(target: true);
        p.Connected = false;
        Assert.False(p.IsPending);
    }

    [Fact]
    public void Reclick_restarts_watchdog_clock()
    {
        using var p = new PendingProbe();
        p.BeginPending(target: true, timeoutMs: 8000);
        p.Advance(7000);
        p.BeginPending(target: true, timeoutMs: 8000); // restart
        p.Advance(7000);                               // 7000 since restart
        Assert.True(p.IsPending);
    }
}
