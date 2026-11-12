namespace APS.SystemControl.Core.Logging;

public enum LogEventLevel
{
    Operator,
    Detail,
    Debug,
}

public sealed record EventLogEntry(
    DateTime Timestamp,
    string Source,
    string Message,
    LogEventLevel Level);
