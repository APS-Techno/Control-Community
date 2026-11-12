using APS.SystemControl.Core.Logging;
using Xunit;

namespace APS.SystemControl.Tests.CoreLogic;

public class CrashReporterTests
{
    [Fact]
    public void BuildReport_IncludesTimestampClassMessageVersionAndOs()
    {
        var when = new DateTime(2026, 5, 6, 14, 23, 17);
        var ex = new InvalidOperationException("widget exploded");
        string report = CrashReporter.BuildReport(ex, "1.2.3", when);

        Assert.Contains("[2026-05-06 14:23:17]", report);
        Assert.Contains("InvalidOperationException", report);
        Assert.Contains("Message: widget exploded", report);
        Assert.Contains("App Version: 1.2.3", report);
        Assert.Contains("OS:", report);
    }

    [Fact]
    public void BuildReport_NullMessage_DoesNotThrow()
    {
        var when = DateTime.UtcNow;
        var ex = new Exception(string.Empty);
        string report = CrashReporter.BuildReport(ex, "1.0", when);
        Assert.Contains("Message:", report);
    }

    [Fact]
    public void BuildReport_IncludesStackTraceWhenPresent()
    {
        Exception captured;
        try { throw new InvalidOperationException("boom"); }
        catch (InvalidOperationException ex) { captured = ex; }

        string report = CrashReporter.BuildReport(captured, "1.0", DateTime.UtcNow);
        Assert.Contains("Stack:", report);
        Assert.Contains("at ", report);   // CLR stack-trace marker
    }

    [Fact]
    public void IsDuplicate_TrueForSameExceptionWithinThreshold()
    {
        var t0 = new DateTime(2026, 5, 6, 12, 0, 0, DateTimeKind.Utc);
        var ex = new InvalidOperationException("same");
        CrashReporter.ResetThrottleForTest();

        Assert.False(CrashReporter.IsDuplicate(ex, t0));
        Assert.True (CrashReporter.IsDuplicate(ex, t0.AddSeconds(2)));
        Assert.True (CrashReporter.IsDuplicate(ex, t0.AddSeconds(4.99)));
    }

    [Fact]
    public void IsDuplicate_FalseAfterThresholdExpires()
    {
        var t0 = new DateTime(2026, 5, 6, 12, 0, 0, DateTimeKind.Utc);
        var ex = new InvalidOperationException("same");
        CrashReporter.ResetThrottleForTest();

        Assert.False(CrashReporter.IsDuplicate(ex, t0));
        Assert.False(CrashReporter.IsDuplicate(ex, t0.AddSeconds(5.01)));
    }

    [Fact]
    public void IsDuplicate_FalseForDifferentMessage()
    {
        var t0 = new DateTime(2026, 5, 6, 12, 0, 0, DateTimeKind.Utc);
        CrashReporter.ResetThrottleForTest();

        Assert.False(CrashReporter.IsDuplicate(new InvalidOperationException("a"), t0));
        Assert.False(CrashReporter.IsDuplicate(new InvalidOperationException("b"), t0.AddSeconds(1)));
    }

    [Fact]
    public void AppendToLog_CreatesFileAndAppendsReport()
    {
        var dir = Path.Combine(Path.GetTempPath(), "aps_crash_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "crash.log");
            CrashReporter.AppendToLog(path, "report-line-1\n");
            CrashReporter.AppendToLog(path, "report-line-2\n");
            string content = File.ReadAllText(path);
            Assert.Contains("report-line-1", content);
            Assert.Contains("report-line-2", content);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void AppendToLog_DoesNotThrowOnInvalidPath()
    {
        // Locked / non-existent path should be swallowed.
        CrashReporter.AppendToLog(@"Z:\definitely\does\not\exist\crash.log", "x");
    }
}
