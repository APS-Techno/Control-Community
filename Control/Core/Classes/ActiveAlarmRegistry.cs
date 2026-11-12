namespace APS.SystemControl.Core.Classes;

public sealed record ActiveAlarm(string Chamber, string Message, DateTime FirstSeen);

/// <summary>Currently-active alarm set, sticky until operator reset — mirrors the
/// AlarmBannerPanel semantics but queryable from any thread (poll thread reports,
/// UI thread clears, agent server snapshots).</summary>
public sealed class ActiveAlarmRegistry
{
    private readonly object _sync = new();
    private readonly Dictionary<(string Chamber, string Message), ActiveAlarm> _active = [];

    /// <summary>Raised outside the lock on the reporting thread (poll thread for Report, UI thread for
    /// Clear) with <c>active=true</c> on first sighting and <c>active=false</c> per alarm on Clear.
    /// Handlers must not block; a throwing handler is contained.</summary>
    public event Action<ActiveAlarm, bool>? Changed;

    public void Report(string chamberId, string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        ActiveAlarm? added = null;
        lock (_sync)
        {
            var key = (chamberId, message);
            if (!_active.ContainsKey(key))
                _active[key] = added = new ActiveAlarm(chamberId, message, DateTime.Now);
        }
        if (added is not null) Raise(added, true);
    }

    public void Clear()
    {
        List<ActiveAlarm> cleared;
        lock (_sync) { cleared = [.. _active.Values]; _active.Clear(); }
        foreach (var a in cleared) Raise(a, false);
    }

    /// <summary>Is this exact (chamber, message) rule currently active? Lock-guarded, allocation-free
    /// and safe from any thread — the telemetry bus reads it once per alarm point per sampler pass.</summary>
    public bool IsActive(string chamberId, string message)
    {
        lock (_sync) return _active.ContainsKey((chamberId, message));
    }

    public IReadOnlyList<ActiveAlarm> Snapshot()
    {
        lock (_sync) return _active.Values.OrderBy(a => a.FirstSeen).ToList();
    }

    private void Raise(ActiveAlarm a, bool active)
    {
        try { Changed?.Invoke(a, active); }
        catch (Exception ex) { System.Diagnostics.Trace.TraceWarning($"ActiveAlarmRegistry.Changed handler threw: {ex.Message}"); }
    }
}
