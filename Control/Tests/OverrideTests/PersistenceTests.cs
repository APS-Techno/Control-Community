using APS.SystemControl.Core.Classes;

namespace APS.SystemControl.Tests.Overrides;

public class PersistenceTests : IDisposable
{
    private readonly string tempDir;
    private readonly string filePath;

    public PersistenceTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "OverrideTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        filePath = Path.Combine(tempDir, "RuntimeOverrides.xml");
    }

    public void Dispose() { try { Directory.Delete(tempDir, recursive: true); } catch { } }

    [Fact]
    public void Missing_File_Loads_Empty_Without_Error()
    {
        var log = new FakeLogWriter();
        var r = new OverrideRegistry(log, clock: () => DateTime.UtcNow);
        var validAlarmIds = new HashSet<string>();
        var validInterlockIds = new HashSet<string>();

        r.Load(filePath, validAlarmIds, validInterlockIds);

        Assert.Equal(0, r.ActiveCount);
    }

    [Fact]
    public void Persisted_Override_Roundtrips()
    {
        var log = new FakeLogWriter();
        var r1 = new OverrideRegistry(log, clock: () => new DateTime(2026, 4, 13, 9, 0, 0));
        r1.SetAlarmOverride("alarm:pl|Processing|>|0.05", true,
            persist: true, reason: "drift", by: "engineer");
        r1.Save(filePath);

        var r2 = new OverrideRegistry(log, clock: () => DateTime.UtcNow);
        var valid = new HashSet<string> { "alarm:pl|Processing|>|0.05" };
        r2.Load(filePath, valid, new HashSet<string>());

        Assert.True(r2.IsAlarmOverridden("alarm:pl|Processing|>|0.05"));
        Assert.Equal(1, r2.ActiveCount);
    }

    [Fact]
    public void Session_Only_Overrides_Are_Not_Saved()
    {
        var r = new OverrideRegistry(new FakeLogWriter(), clock: () => DateTime.UtcNow);
        r.SetAlarmOverride("a1", true, persist: false, reason: "", by: "u");
        r.Save(filePath);

        Assert.False(File.Exists(filePath) && new FileInfo(filePath).Length > 0
                     && File.ReadAllText(filePath).Contains("a1"));
    }

    [Fact]
    public void Orphan_Ids_Are_Dropped_And_Logged()
    {
        // Seed a file with two alarm entries, one of which is orphan.
        var r1 = new OverrideRegistry(new FakeLogWriter(), clock: () => DateTime.UtcNow);
        r1.SetAlarmOverride("valid:id", true, persist: true, reason: "x", by: "u");
        r1.SetAlarmOverride("gone:id",  true, persist: true, reason: "y", by: "u");
        r1.Save(filePath);

        var log = new FakeLogWriter();
        var r2 = new OverrideRegistry(log, clock: () => DateTime.UtcNow);
        var valid = new HashSet<string> { "valid:id" };
        r2.Load(filePath, valid, new HashSet<string>());

        Assert.True(r2.IsAlarmOverridden("valid:id"));
        Assert.False(r2.IsAlarmOverridden("gone:id"));
        Assert.Contains(log.Lines, l => l.Contains("[OVERRIDE ORPHAN]") && l.Contains("gone:id"));
    }

    [Fact]
    public void Corrupt_File_Is_Renamed_And_Registry_Starts_Empty()
    {
        File.WriteAllText(filePath, "<not valid xml");
        var log = new FakeLogWriter();
        var r = new OverrideRegistry(log, clock: () => DateTime.UtcNow);

        r.Load(filePath, new HashSet<string>(), new HashSet<string>());

        Assert.Equal(0, r.ActiveCount);
        Assert.False(File.Exists(filePath));
        var badFiles = Directory.GetFiles(tempDir, "RuntimeOverrides.xml.bad.*");
        Assert.Single(badFiles);
    }
}
