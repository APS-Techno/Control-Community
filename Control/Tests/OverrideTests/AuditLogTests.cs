using APS.SystemControl.Core.Classes;

namespace APS.SystemControl.Tests.Overrides;

public class AuditLogTests
{
    [Fact]
    public void LogSuppressedAlarm_Writes_One_Line_Within_Window()
    {
        var log = new FakeLogWriter();
        DateTime now = new(2026, 4, 13, 12, 0, 0);
        var r = new OverrideRegistry(log, clock: () => now);
        r.SetAlarmOverride("a1", true, false, "drift", "u");
        log.Clear();

        for (int i = 0; i < 50; i++) r.LogSuppressedAlarm("a1", "Pressure spike");

        Assert.Single(log.Lines);
        Assert.Contains("[SUPPRESSED]", log.Lines[0]);
        Assert.Contains("a1", log.Lines[0]);
        Assert.Contains("Pressure spike", log.Lines[0]);
    }

    [Fact]
    public void LogSuppressedAlarm_Writes_Again_After_Window_Elapses()
    {
        var log = new FakeLogWriter();
        DateTime now = new(2026, 4, 13, 12, 0, 0);
        var r = new OverrideRegistry(log, clock: () => now);
        r.SetAlarmOverride("a1", true, false, "drift", "u");
        log.Clear();

        r.LogSuppressedAlarm("a1", "Spike");
        now = now.AddSeconds(5);
        r.LogSuppressedAlarm("a1", "Spike");
        now = now.AddSeconds(6);   // total 11s since first
        r.LogSuppressedAlarm("a1", "Spike");

        Assert.Equal(2, log.Lines.Count);
    }

    [Fact]
    public void LogSuppressedInterlock_Is_Not_Rate_Limited()
    {
        var log = new FakeLogWriter();
        var r = new OverrideRegistry(log, clock: () => new DateTime(2026, 4, 13, 12, 0, 0));
        r.SetInterlockOverride("i1", true, false, "commissioning", "u");
        log.Clear();

        r.LogSuppressedInterlock("i1", "Open M1 bypassed");
        r.LogSuppressedInterlock("i1", "Open M1 bypassed");

        Assert.Equal(2, log.Lines.Count);
        Assert.All(log.Lines, l => Assert.Contains("[SUPPRESSED]", l));
    }

    [Fact]
    public void SetAlarmOverride_Logs_Set_And_Clear_Lines()
    {
        var log = new FakeLogWriter();
        var r = new OverrideRegistry(log, clock: () => new DateTime(2026, 4, 13, 12, 0, 0));

        r.SetAlarmOverride("a1", true,  false, "drift", "engineer");
        r.SetAlarmOverride("a1", false, false, "", "engineer");

        Assert.Equal(2, log.Lines.Count);
        Assert.Contains("[OVERRIDE SET]",   log.Lines[0]);
        Assert.Contains("reason=\"drift\"", log.Lines[0]);
        Assert.Contains("persist=false",    log.Lines[0]);
        Assert.Contains("[OVERRIDE CLEAR]", log.Lines[1]);
    }

    [Fact]
    public void LogSuppressedAlarm_Reset_After_Toggle_Off_Then_On()
    {
        var log = new FakeLogWriter();
        DateTime now = new(2026, 4, 13, 12, 0, 0);
        var r = new OverrideRegistry(log, clock: () => now);

        r.SetAlarmOverride("a1", true, false, "r1", "u");
        r.LogSuppressedAlarm("a1", "Spike");      // first log line
        now = now.AddSeconds(2);
        r.LogSuppressedAlarm("a1", "Spike");      // rate-limited (window=10s)

        r.SetAlarmOverride("a1", false, false, "", "u");  // removes override + rate-limit entry
        now = now.AddSeconds(1);
        r.SetAlarmOverride("a1", true, false, "r2", "u"); // re-disable
        log.Clear();

        r.LogSuppressedAlarm("a1", "Spike");
        Assert.Single(log.Lines);
        Assert.Contains("[SUPPRESSED]", log.Lines[0]);
    }

    [Fact]
    public void LogSuppressedAlarm_Recovers_From_Clock_Jumping_Backward()
    {
        var log = new FakeLogWriter();
        DateTime now = new(2026, 4, 13, 12, 0, 0);
        var r = new OverrideRegistry(log, clock: () => now);
        r.SetAlarmOverride("a1", true, false, "r", "u");
        log.Clear();

        r.LogSuppressedAlarm("a1", "Spike");
        // Simulate DST rollback — clock jumps backward by 1 hour.
        now = now.AddHours(-1);

        r.LogSuppressedAlarm("a1", "Spike");
        Assert.Equal(2, log.Lines.Count);  // second call should log, not be stuck in the future window
    }
}
