using System.Collections.Frozen;

namespace APS.SystemControl.Core.Logging;

/// <summary>
/// Pure classifier mapping (source, message) → LogEventLevel.
///
/// Default behavior: every log message is treated as Operator (shown on
/// the floating events overlay) except for a small set of known-noisy
/// sources (polling, watchdog ticks, telemetry chatter), which downgrade
/// to Detail. This mirrors the Delphi DepositionControl2 Event Memo
/// behavior — operators expect to see what the system just did, not a
/// curated subset.
/// </summary>
public static class EventLogClassifier
{
    // Sources we never want on the floating overlay — repetitive or low-signal.
    // Matched case-insensitively against the source string.
    private static readonly FrozenSet<string> NoisySources =
        new[]
        {
            "OpDelay",   // operation-delay timer ticks
            "Watchdog",  // poll watchdog heartbeats
            "Poll",
            "Sensor",
            "PLC",
            "MFC",       // raw MFC chatter (setpoint applied is separate)
            "Menus",     // designer/loader warnings only the dev cares about
        }
        .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static LogEventLevel Classify(string source, string message)
    {
        if (NoisySources.Contains(source ?? string.Empty))
            return LogEventLevel.Detail;
        // Recipe-internal trace lines from SendRecipeLog (exec:, target=,
        // FindElement(...), result:, bound device:, EXCEPTION:, etc.) are
        // emitted with a two-space indent. They're useful in the full log
        // grid but only clutter the operator events overlay.
        if (!string.IsNullOrEmpty(message) && char.IsWhiteSpace(message![0]))
            return LogEventLevel.Detail;
        return LogEventLevel.Operator;
    }
}
