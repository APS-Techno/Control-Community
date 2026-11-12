using APS.SystemControl.Core.Logging;
using APS.SystemControl.Core.Remote.Agent;
using Xunit;

namespace CoreLogicTests;

public class EventLogRingBufferTests : IDisposable
{
    public EventLogRingBufferTests() => EventLogBus.ResetForTests();
    public void Dispose() => EventLogBus.ResetForTests();

    [Fact]
    public void Captures_published_entries_newest_last()
    {
        using var buf = new EventLogRingBuffer(10);
        EventLogBus.Publish("T", "one", LogEventLevel.Detail);
        EventLogBus.Publish("T", "two", LogEventLevel.Detail);
        var tail = buf.Tail(5);
        Assert.Equal(2, tail.Count);
        Assert.Equal("two", tail[^1].Message);
    }

    [Fact]
    public void Capacity_trims_oldest_and_tail_respects_count()
    {
        using var buf = new EventLogRingBuffer(3);
        for (int i = 0; i < 5; i++) EventLogBus.Publish("T", $"m{i}", LogEventLevel.Detail);
        Assert.Equal(["m2", "m3", "m4"], buf.Tail(10).Select(e => e.Message));
        Assert.Equal(["m4"], buf.Tail(1).Select(e => e.Message));
    }

    [Fact]
    public void Dispose_unsubscribes()
    {
        var buf = new EventLogRingBuffer(10);
        buf.Dispose();
        EventLogBus.Publish("T", "late", LogEventLevel.Detail);
        Assert.Empty(buf.Tail(10));
    }
}
