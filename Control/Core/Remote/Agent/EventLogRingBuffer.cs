using APS.SystemControl.Core.Logging;

namespace APS.SystemControl.Core.Remote.Agent;

/// <summary>Thread-safe rolling capture of EventLogBus for the agent's logs.tail command.</summary>
internal sealed class EventLogRingBuffer : IDisposable
{
    private readonly int _capacity;
    private readonly Queue<EventLogEntry> _entries = new();
    private readonly object _sync = new();

    public EventLogRingBuffer(int capacity = 500)
    {
        _capacity = capacity;
        EventLogBus.Entry += OnEntry;
    }

    private void OnEntry(EventLogEntry entry)
    {
        lock (_sync)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > _capacity) _entries.Dequeue();
        }
    }

    public IReadOnlyList<EventLogEntry> Tail(int count)
    {
        lock (_sync)
        {
            int skip = Math.Max(0, _entries.Count - count);
            return _entries.Skip(skip).ToList();
        }
    }

    public void Dispose() => EventLogBus.Entry -= OnEntry;
}
