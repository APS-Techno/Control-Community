using EmulatorCore.Logging;

namespace EmulatorTests;

public class EmulatorLogTests : IDisposable
{
    public EmulatorLogTests() => EmulatorLog.Clear();
    public void Dispose() => EmulatorLog.Clear();

    [Fact]
    public void Log_AddsEntry()
    {
        EmulatorLog.Clear();
        EmulatorLog.Log(LogLevel.Info, "Test", "Hello");
        var entries = EmulatorLog.GetEntries();
        var entry = Assert.Single(entries, e => e.Source == "Test" && e.Message == "Hello");
        Assert.Equal(LogLevel.Info, entry.Level);
    }

    [Fact]
    public void Log_CircularBuffer_DropsOldest()
    {
        EmulatorLog.Clear();
        for (int i = 0; i < 10_005; i++)
            EmulatorLog.Log(LogLevel.Debug, "BufTest", $"Msg {i}");
        var entries = EmulatorLog.GetEntries();
        Assert.True(entries.Count <= 10_000, $"Buffer should cap at 10,000 but had {entries.Count}");
        // Verify oldest entries were dropped: "Msg 0" through "Msg 4" should be gone
        Assert.DoesNotContain(entries, e => e.Source == "BufTest" && e.Message == "Msg 0");
        Assert.Contains(entries, e => e.Source == "BufTest" && e.Message == "Msg 10004");
    }

    [Fact]
    public void Log_FiresEntryAddedEvent()
    {
        bool received = false;
        void handler(LogEntry e)
        {
            if (e.Level == LogLevel.Warning && e.Source == "Src" && e.Message == "Warn!")
                received = true;
        }
        EmulatorLog.EntryAdded += handler;
        try
        {
            EmulatorLog.Log(LogLevel.Warning, "Src", "Warn!");
            Assert.True(received);
        }
        finally
        {
            EmulatorLog.EntryAdded -= handler;
        }
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        EmulatorLog.Log(LogLevel.Info, "Test", "data");
        EmulatorLog.Clear();
        Assert.Empty(EmulatorLog.GetEntries());
    }
}
