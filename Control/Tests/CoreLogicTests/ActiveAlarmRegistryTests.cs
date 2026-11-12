using APS.SystemControl.Core.Classes;
using Xunit;

namespace CoreLogicTests;

public class ActiveAlarmRegistryTests
{
    [Fact]
    public void Report_accumulates_and_dedups_by_chamber_and_message()
    {
        var reg = new ActiveAlarmRegistry();
        reg.Report("Main", "Pressure too high");
        reg.Report("Main", "Pressure too high");   // refire — deduped
        reg.Report("LL", "Pressure too high");     // other chamber — distinct
        var snap = reg.Snapshot();
        Assert.Equal(2, snap.Count);
        Assert.Equal("Main", snap[0].Chamber);
        Assert.Equal("LL", snap[1].Chamber);
    }

    [Fact]
    public void Blank_messages_are_ignored()
    {
        var reg = new ActiveAlarmRegistry();
        reg.Report("Main", "");
        reg.Report("Main", "  ");
        Assert.Empty(reg.Snapshot());
    }

    [Fact]
    public void Clear_empties_and_next_report_is_fresh()
    {
        var reg = new ActiveAlarmRegistry();
        reg.Report("Main", "TP fault");
        reg.Clear();
        Assert.Empty(reg.Snapshot());
        reg.Report("Main", "TP fault");
        Assert.Single(reg.Snapshot());
    }

    [Fact]
    public void Changed_fires_once_per_new_alarm_and_once_per_alarm_on_clear()
    {
        var reg = new ActiveAlarmRegistry();
        var seen = new List<(string Msg, bool Active)>();
        reg.Changed += (a, active) => seen.Add((a.Message, active));
        reg.Report("Main", "A");
        reg.Report("Main", "A");            // duplicate: no event
        reg.Report("TM", "B");
        reg.Clear();
        reg.Clear();                        // nothing left: no event
        Assert.Equal([("A", true), ("B", true), ("A", false), ("B", false)], seen);
    }

    [Fact]
    public void Changed_handler_exception_does_not_break_report()
    {
        var reg = new ActiveAlarmRegistry();
        reg.Changed += (_, _) => throw new InvalidOperationException("ui died");
        reg.Report("Main", "A");
        Assert.Single(reg.Snapshot());
    }
}
