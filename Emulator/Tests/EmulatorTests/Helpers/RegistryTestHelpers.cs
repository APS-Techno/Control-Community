using System.Xml.Linq;
using EmulatorCore.Engine;
using EmulatorCore.Interfaces;
using EmulatorCore.Logging;
using EmulatorCore.Models;

namespace EmulatorTests;

public static class RegistryTestHelpers
{
    public static DeviceCreateContext Ctx(string xml, string shortType, string host = "")
        => new(XElement.Parse(xml), "valves", shortType, host, DefaultPhysics());

    public static PhysicsConfig DefaultPhysics() => new();
}

public static class TestConfigs
{
    public static string Write(string xml)
    {
        string path = Path.Combine(Path.GetTempPath(), $"hwconf_test_{Guid.NewGuid()}.xml");
        File.WriteAllText(path, xml);
        return path;
    }
}

public sealed class EmulatorLogCapture : IDisposable
{
    private readonly List<LogEntry> _entries = [];

    private EmulatorLogCapture() => EmulatorLog.EntryAdded += OnEntry;

    public static EmulatorLogCapture Start() => new();

    public IEnumerable<string> Warnings
    {
        get
        {
            lock (_entries)
                return _entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message).ToList();
        }
    }

    private void OnEntry(LogEntry entry)
    {
        lock (_entries) _entries.Add(entry);
    }

    public void Dispose() => EmulatorLog.EntryAdded -= OnEntry;
}
