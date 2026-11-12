using System.Xml.Linq;
using APS.SystemEmulator.Setup;

namespace EmulatorTests;

public class ControlConfigSyncTests
{
    private static string TempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), $"aps-emu-sync-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string WriteRootConfig(string dir, string content)
    {
        var path = Path.Combine(dir, "RootConfig.xml");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Sync_PreservesUnrelatedFields()
    {
        var dir = TempDir();
        try
        {
            var target = WriteRootConfig(dir, """
                <?xml version="1.0"?>
                <AppConfig>
                  <ConfigDir>C:\old\configs</ConfigDir>
                  <Prefix>OldProject</Prefix>
                  <WorkDir>C:\custom\work</WorkDir>
                  <UIScale>1.25</UIScale>
                </AppConfig>
                """);

            var result = ControlConfigSync.TrySync(
                emulatorBaseDir: dir,
                overridePath: target,
                configsRoot: @"C:\Lab\Configs",
                projectName: "Sputter",
                out var resolvedPath,
                out var err);

            Assert.Equal(ControlSyncResult.Synced, result);
            Assert.Equal(target, resolvedPath);
            Assert.Null(err);

            var doc = XDocument.Load(target);
            Assert.Equal(@"C:\Lab\Configs", (string?)doc.Root!.Element("ConfigDir"));
            Assert.Equal("Sputter",                 (string?)doc.Root.Element("Prefix"));
            Assert.Equal(@"C:\custom\work",          (string?)doc.Root.Element("WorkDir"));
            Assert.Equal("1.25",                     (string?)doc.Root.Element("UIScale"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Sync_InsertsMissingElements()
    {
        var dir = TempDir();
        try
        {
            var target = WriteRootConfig(dir, """
                <?xml version="1.0"?>
                <AppConfig>
                  <WorkDir>C:\custom\work</WorkDir>
                </AppConfig>
                """);

            var result = ControlConfigSync.TrySync(
                dir, target, @"C:\Lab\Configs", "Sputter",
                out _, out _);

            Assert.Equal(ControlSyncResult.Synced, result);
            var doc = XDocument.Load(target);
            Assert.Equal(@"C:\Lab\Configs", (string?)doc.Root!.Element("ConfigDir"));
            Assert.Equal("Sputter", (string?)doc.Root.Element("Prefix"));
            Assert.Equal(@"C:\custom\work", (string?)doc.Root.Element("WorkDir"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Sync_FileNotFound_DoesNotCreate()
    {
        var dir = TempDir();
        try
        {
            var target = Path.Combine(dir, "RootConfig.xml"); // not created

            var result = ControlConfigSync.TrySync(
                dir, target, @"C:\Lab\Configs", "Sputter",
                out var resolvedPath, out var err);

            Assert.Equal(ControlSyncResult.FileNotFound, result);
            Assert.Equal(target, resolvedPath);
            Assert.Null(err);
            Assert.False(File.Exists(target));
            Assert.False(File.Exists(target + ".tmp"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Sync_MalformedXml_LeavesOriginalUntouched()
    {
        var dir = TempDir();
        try
        {
            var target = WriteRootConfig(dir, "<not xml");

            var result = ControlConfigSync.TrySync(
                dir, target, @"C:\Lab\Configs", "Sputter",
                out _, out var err);

            Assert.Equal(ControlSyncResult.IOError, result);
            Assert.NotNull(err);
            Assert.Equal("<not xml", File.ReadAllText(target));
            Assert.False(File.Exists(target + ".tmp"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Sync_PreservesUnknownElements()
    {
        var dir = TempDir();
        try
        {
            var target = WriteRootConfig(dir, """
                <?xml version="1.0"?>
                <AppConfig>
                  <ConfigDir>C:\old</ConfigDir>
                  <Prefix>Old</Prefix>
                  <FutureField>do-not-wipe</FutureField>
                </AppConfig>
                """);

            var result = ControlConfigSync.TrySync(
                dir, target, @"C:\Lab\Configs", "Sputter",
                out _, out _);

            Assert.Equal(ControlSyncResult.Synced, result);
            var doc = XDocument.Load(target);
            Assert.Equal("do-not-wipe", (string?)doc.Root!.Element("FutureField"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Sync_LeavesBackupFile()
    {
        var dir = TempDir();
        try
        {
            const string pre = """
                <?xml version="1.0"?>
                <AppConfig>
                  <ConfigDir>C:\old</ConfigDir>
                  <Prefix>OldProject</Prefix>
                </AppConfig>
                """;
            var target = WriteRootConfig(dir, pre);

            var result = ControlConfigSync.TrySync(
                dir, target, @"C:\Lab\Configs", "Sputter",
                out _, out _);

            Assert.Equal(ControlSyncResult.Synced, result);
            Assert.True(File.Exists(target + ".bak"));
            var bakDoc = XDocument.Parse(File.ReadAllText(target + ".bak"));
            Assert.Equal("OldProject", (string?)bakDoc.Root!.Element("Prefix"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Sync_DefaultPath_PrefersSharedBinNextToEmulator()
    {
        var dir = TempDir();
        try
        {
            // Shared-bin layout: Control's RootConfig.xml sits in the same
            // directory the Emulator runs from.
            var target = WriteRootConfig(dir, """
                <?xml version="1.0"?>
                <AppConfig><ConfigDir>C:\old</ConfigDir><Prefix>Old</Prefix></AppConfig>
                """);

            var result = ControlConfigSync.TrySync(
                emulatorBaseDir: dir,
                overridePath: null,
                configsRoot: @"C:\Lab\Configs",
                projectName: "Sputter",
                out var resolvedPath, out _);

            Assert.Equal(ControlSyncResult.Synced, result);
            Assert.Equal(Path.GetFullPath(target), resolvedPath);
            var doc = XDocument.Load(target);
            Assert.Equal("Sputter", (string?)doc.Root!.Element("Prefix"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Sync_DefaultPath_FallsBackToPerRepoBinConvention()
    {
        var root = TempDir();
        try
        {
            // Per-repo-bin layout: <root>\Emulator\bin (no RootConfig.xml here)
            // and <root>\Control\bin\RootConfig.xml.
            var emulatorBin = Path.Combine(root, "Emulator", "bin");
            Directory.CreateDirectory(emulatorBin);
            var controlBin = Path.Combine(root, "Control", "bin");
            Directory.CreateDirectory(controlBin);
            var target = WriteRootConfig(controlBin, """
                <?xml version="1.0"?>
                <AppConfig><ConfigDir>C:\old</ConfigDir><Prefix>Old</Prefix></AppConfig>
                """);

            var result = ControlConfigSync.TrySync(
                emulatorBaseDir: emulatorBin,
                overridePath: null,
                configsRoot: @"C:\Lab\Configs",
                projectName: "Sputter",
                out var resolvedPath, out _);

            Assert.Equal(ControlSyncResult.Synced, result);
            Assert.Equal(Path.GetFullPath(target), resolvedPath);
            var doc = XDocument.Load(target);
            Assert.Equal("Sputter", (string?)doc.Root!.Element("Prefix"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Sync_DefaultPath_NeitherExists_ReportsSharedBinPath()
    {
        var dir = TempDir();
        try
        {
            var result = ControlConfigSync.TrySync(
                emulatorBaseDir: dir,
                overridePath: null,
                configsRoot: @"C:\Lab\Configs",
                projectName: "Sputter",
                out var resolvedPath, out _);

            Assert.Equal(ControlSyncResult.FileNotFound, result);
            var expected = Path.GetFullPath(Path.Combine(dir, "RootConfig.xml"));
            Assert.Equal(expected, resolvedPath);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Sync_RelativeOverridePath_ResolvesAgainstBaseDir()
    {
        var dir = TempDir();
        try
        {
            var fooDir = Path.Combine(dir, "foo");
            Directory.CreateDirectory(fooDir);
            var target = Path.Combine(fooDir, "bar.xml");
            File.WriteAllText(target, """
                <?xml version="1.0"?>
                <AppConfig><ConfigDir>C:\old</ConfigDir><Prefix>Old</Prefix></AppConfig>
                """);

            var result = ControlConfigSync.TrySync(
                emulatorBaseDir: dir,
                overridePath: @"foo\bar.xml",
                configsRoot: @"C:\Lab\Configs",
                projectName: "Sputter",
                out var resolvedPath, out _);

            Assert.Equal(ControlSyncResult.Synced, result);
            Assert.Equal(Path.GetFullPath(target), resolvedPath);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
