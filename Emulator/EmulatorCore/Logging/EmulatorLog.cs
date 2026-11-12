namespace EmulatorCore.Logging;

public enum LogLevel { Trace, Debug, Info, Warning, Error }

public record LogEntry(DateTime Time, LogLevel Level, string Source, string Message);

public static class EmulatorLog
{
    private const int MaxEntries = 10_000;
    private static readonly List<LogEntry> _entries = new(MaxEntries + 100);
    private static readonly Lock _lock = new();

    public static event Action<LogEntry>? EntryAdded;

    public static void Log(LogLevel level, string source, string message)
    {
        var entry = new LogEntry(DateTime.Now, level, source, message);
        lock (_lock)
        {
            _entries.Add(entry);
            if (_entries.Count > MaxEntries)
                _entries.RemoveRange(0, _entries.Count - MaxEntries);
        }
        // Raise outside the lock. Subscribers may marshal to the UI thread or log
        // re-entrantly; Lock is non-reentrant, so invoking inside the lock would
        // serialize all logging behind arbitrary subscriber work and can deadlock.
        EntryAdded?.Invoke(entry);
    }

    public static IReadOnlyList<LogEntry> GetEntries()
    {
        lock (_lock)
        {
            return [.. _entries];
        }
    }

    public static void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
        }
    }
}
