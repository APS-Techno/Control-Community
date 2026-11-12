using System.Text;

namespace APS.SystemControl.Core.Logging;

/// <summary>
/// Global crash reporter. Mirrors Delphi unit_CrashReporter.pas behavior:
/// throttles duplicates within 5 s, appends a structured report to
/// crash.log, and shows a modal dialog. Wired up by Program.Main —
/// not safe to call ShowDialog from threads other than the UI thread.
/// </summary>
public static class CrashReporter
{
    private const int ThrottleSeconds = 5;

    private static readonly object _lock = new();
    private static string? _lastClass;
    private static string? _lastMessage;
    private static DateTime _lastTimeUtc;

    /// <summary>
    /// Pure formatter — builds the multi-line report. Public for unit tests.
    /// </summary>
    public static string BuildReport(Exception ex, string appVersion, DateTime when)
    {
        var sb = new StringBuilder();
        sb.AppendFormat("[{0:yyyy-MM-dd HH:mm:ss}] {1}\n", when, ex.GetType().Name);
        sb.AppendFormat("  Message: {0}\n", ex.Message ?? "");
        sb.AppendFormat("  App Version: {0}\n", appVersion);
        sb.AppendFormat("  OS: Windows {0}\n", Environment.OSVersion.Version);
        if (!string.IsNullOrEmpty(ex.StackTrace))
        {
            sb.AppendLine("  Stack:");
            sb.AppendLine(ex.StackTrace);
        }
        sb.AppendLine("  ----");
        return sb.ToString();
    }

    /// <summary>
    /// Returns true if this exception matches the previous one (same class +
    /// same message) within ThrottleSeconds. Always updates the last-seen
    /// state so a burst-then-quiet pattern shows one entry, then the next
    /// distinct event is reported.
    /// </summary>
    public static bool IsDuplicate(Exception ex, DateTime nowUtc)
    {
        lock (_lock)
        {
            bool dup = string.Equals(_lastClass, ex.GetType().Name, StringComparison.Ordinal)
                    && string.Equals(_lastMessage, ex.Message ?? "", StringComparison.Ordinal)
                    && (nowUtc - _lastTimeUtc).TotalSeconds < ThrottleSeconds;
            _lastClass = ex.GetType().Name;
            _lastMessage = ex.Message ?? "";
            _lastTimeUtc = nowUtc;
            return dup;
        }
    }

    internal static void ResetThrottleForTest()
    {
        lock (_lock)
        {
            _lastClass = null;
            _lastMessage = null;
            _lastTimeUtc = DateTime.MinValue;
        }
    }

    /// <summary>
    /// Appends <paramref name="report"/> to <paramref name="path"/>. Creates
    /// the file and any missing parent directories. Swallows all I/O errors —
    /// must never throw, since callers run inside an exception handler.
    /// </summary>
    public static void AppendToLog(string path, string report)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.AppendAllText(path, report);
        }
        catch
        {
        }
    }

    private static string _logPath = "";
    private static string _appVersion = "";

    /// <summary>
    /// One-time configuration. Called from Program.Main before hooks are wired.
    /// </summary>
    public static void Configure(string logPath, string appVersion)
    {
        _logPath = logPath;
        _appVersion = appVersion;
    }

    /// <summary>
    /// Top-level handler. Throttles, logs, shows the dialog. Never throws.
    /// Safe to call from any thread, but ShowDialog requires the UI thread —
    /// callers should marshal via Control.Invoke when not on the UI thread.
    /// </summary>
    public static void Report(Exception ex)
    {
        try
        {
            var now = DateTime.UtcNow;
            if (IsDuplicate(ex, now))
                return;

            string report = BuildReport(ex, _appVersion, now.ToLocalTime());
            AppendToLog(_logPath, report);

            ShowDialog(ex.GetType().Name, ex.Message ?? "", report, _logPath);
        }
        catch
        {
        }
    }

    private static void ShowDialog(string className, string message, string report, string logPath)
    {
        try
        {
            using var dlg = new APS.SystemControl.Forms.frmCrashReport(className, message, report, logPath);
            dlg.ShowDialog();
        }
        catch
        {
            // Dialog itself crashed — fall back to FailFast which guarantees
            // the original exception still produces a Windows error report.
            // Skip this if a debugger is attached so devs see the inner failure.
            if (!System.Diagnostics.Debugger.IsAttached)
                Environment.FailFast("CrashReporter dialog failed", new Exception(report));
        }
    }
}
