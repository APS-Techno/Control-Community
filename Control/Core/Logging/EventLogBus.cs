namespace APS.SystemControl.Core.Logging;

/// <summary>
/// Process-wide pub/sub for log events. Subscribers (Log tab grid, bottom
/// drawer, floating Scheme overlay) attach to <see cref="Entry"/>. Publishers
/// may call <see cref="Publish"/> from any thread; subscribers are responsible
/// for marshalling to their own UI thread.
/// </summary>
public static class EventLogBus
{
    public static event Action<EventLogEntry>? Entry;

    // Boot gate: while true, any Operator-level publish is downgraded to
    // Detail so the floating Events overlay stays quiet during the noisy
    // hardware-discovery / config-load phase. The full Log tab grid still
    // shows everything (it renders all levels). FrmMain flips this off
    // immediately after the "System is ready" message.
    private static int _isBooting = 1;

    public static bool IsBooting => Volatile.Read(ref _isBooting) != 0;

    public static void MarkBootComplete() => Interlocked.Exchange(ref _isBooting, 0);

    public static void Publish(string source, string message, LogEventLevel level)
    {
        if (level == LogEventLevel.Operator && IsBooting)
            level = LogEventLevel.Detail;
        Entry?.Invoke(new EventLogEntry(DateTime.Now, source, message, level));
    }

    public static void PublishClassified(string source, string message)
        => Publish(source, message, EventLogClassifier.Classify(source, message));

    /// <summary>
    /// Test-only: detach every subscriber and restore the boot gate to its
    /// process-initial state. Because <see cref="Entry"/> and the boot gate are
    /// static, a test that subscribes is exposed to publishes from any other
    /// test; this lets a test start from a known-clean bus. Pair with serialized
    /// (non-parallel) test execution so no other test mutates the bus mid-run.
    /// </summary>
    internal static void ResetForTests()
    {
        Entry = null;
        Interlocked.Exchange(ref _isBooting, 1);
    }
}
