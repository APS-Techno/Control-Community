using System.Linq;
using APS.ProcessPanel.Contracts;

namespace APS.SystemControl;

/// <summary>
/// The FrmMain channel handlers call <see cref="Push(string, double?, ReadingState)"/>
/// as values arrive; a UI timer periodically calls <see cref="Snapshot"/> and hands the
/// array to <see cref="IProcessPanel.UpdateReadings"/>.
///
/// All access is from the UI thread. No locking.
/// </summary>
internal sealed class ProcessTelemetryCollector
{
    private readonly Dictionary<string, Entry> _cache = new(StringComparer.Ordinal);

    /// <summary>
    /// Register a reading Id the panel cares about. Unregistered Ids passed
    /// to <see cref="Push"/> are silently ignored — this keeps the collector
    /// cheap even when dozens of unrelated devices share the bus.
    /// </summary>
    public void Register(string? id)
    {
        if (id == null) return;
        if (!_cache.ContainsKey(id))
            _cache[id] = new Entry(null, ReadingState.Off);
    }

    public void RegisterMany(IEnumerable<string?> ids)
    {
        foreach (var id in ids) Register(id);
    }

    /// <summary>Silently ignores unregistered ids.</summary>
    public void Push(string? id, double? value, ReadingState state)
    {
        if (id == null) return;
        if (_cache.ContainsKey(id))
            _cache[id] = new Entry(value, state);
    }

    /// <summary>
    /// Value cleared, state = Off. Use when the device disconnects.
    /// </summary>
    public void MarkOffline(string? id)
    {
        if (id == null) return;
        if (_cache.ContainsKey(id))
            _cache[id] = new Entry(null, ReadingState.Off);
    }

    /// <summary>
    /// Mark a device and every reading derived from it offline: the id itself
    /// plus any <c>&lt;device&gt;_*</c> id (a supply's <c>_V</c>/<c>_I</c>/<c>_W</c>,
    /// a multi-channel analyser's <c>_V1</c>.., a throttle controller's <c>_P</c>/<c>_Pos</c>).
    /// </summary>
    public void MarkOfflineByDevice(string? device)
    {
        if (string.IsNullOrEmpty(device)) return;
        string prefix = device + "_";
        foreach (var id in _cache.Keys.ToList())
        {
            if (id == device || id.StartsWith(prefix, StringComparison.Ordinal))
                _cache[id] = new Entry(null, ReadingState.Off);
        }
    }

    /// <summary>
    /// Returns a fresh array, safe to hand off to the panel without further copying.
    /// </summary>
    public ProcessReading[] Snapshot()
    {
        var result = new ProcessReading[_cache.Count];
        int i = 0;
        foreach (var kvp in _cache)
            result[i++] = new ProcessReading(kvp.Key, kvp.Value.Value, kvp.Value.State);
        return result;
    }

    private readonly record struct Entry(double? Value, ReadingState State);
}
