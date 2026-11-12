using System.IO;
using System.Xml.Serialization;
using APS.SystemControl.Core.CoreClass;

namespace CoreLogicTests;

public class SaveRootConfigTests : IDisposable
{
    private readonly string _tmpDir;

    public SaveRootConfigTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "rootcfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmpDir, true); } catch { /* best-effort */ }
    }

    [Fact]
    public void SaveRootConfig_writes_all_fields_and_can_be_round_tripped()
    {
        var path = Path.Combine(_tmpDir, "RootConfig.xml");
        var cfg = new AppConfig
        {
            ConfigDir = @"C:\Configs",
            Prefix = "ADS_FULL",
            WorkDir = @"C:\Work",
            HWDir = @"C:\HW",
            GUIDir = @"C:\GUI",
            WorkerDir = @"C:\Workers",
            UIScale = 1.25f,
        };

        Config.SaveRootConfig(cfg, path);

        Assert.True(File.Exists(path));
        var serializer = new XmlSerializer(typeof(AppConfig));
        using var reader = new StreamReader(path);
        var roundTrip = (AppConfig)serializer.Deserialize(reader)!;

        Assert.Equal(cfg.ConfigDir, roundTrip.ConfigDir);
        Assert.Equal(cfg.Prefix, roundTrip.Prefix);
        Assert.Equal(cfg.WorkDir, roundTrip.WorkDir);
        Assert.Equal(cfg.HWDir, roundTrip.HWDir);
        Assert.Equal(cfg.GUIDir, roundTrip.GUIDir);
        Assert.Equal(cfg.WorkerDir, roundTrip.WorkerDir);
        Assert.Equal(cfg.UIScale, roundTrip.UIScale);
    }

    [Fact]
    public void Load_then_save_preserves_unknown_root_elements()
    {
        var path = Path.Combine(_tmpDir, "RootConfig.xml");
        File.WriteAllText(path, "<AppConfig><Prefix>P</Prefix><Remote screenshotEnabled=\"true\" sharedKey=\"dev\"/></AppConfig>");

        var cfg = new Config(path);
        var outPath = Path.Combine(_tmpDir, "Out.xml");
        Config.SaveRootConfig(Config.SnapshotOf(cfg.Snapshot()), outPath);

        var saved = System.Xml.Linq.XDocument.Load(outPath).Root!.Element("Remote");
        Assert.NotNull(saved);
        Assert.Equal("true", saved!.Attribute("screenshotEnabled")!.Value);
        Assert.Equal("dev", saved.Attribute("sharedKey")!.Value);
    }

    [Fact]
    public void SaveRootConfig_overwrites_existing_file_atomically_and_leaves_bak()
    {
        var path = Path.Combine(_tmpDir, "RootConfig.xml");
        File.WriteAllText(path, "<AppConfig><Prefix>OLD</Prefix></AppConfig>");

        var cfg = new AppConfig { Prefix = "NEW", UIScale = 1.0f };
        Config.SaveRootConfig(cfg, path);

        var serializer = new XmlSerializer(typeof(AppConfig));
        using var reader = new StreamReader(path);
        var roundTrip = (AppConfig)serializer.Deserialize(reader)!;
        Assert.Equal("NEW", roundTrip.Prefix);

        var bakPath = path + ".bak";
        Assert.True(File.Exists(bakPath));
        Assert.Contains("OLD", File.ReadAllText(bakPath));
    }

    [Fact]
    public void Snapshot_returns_independent_copy()
    {
        var original = new AppConfig
        {
            ConfigDir = "A", Prefix = "B", WorkDir = "C",
            HWDir = "D", GUIDir = "E", WorkerDir = "F", UIScale = 1.5f,
        };
        var clone = Config.SnapshotOf(original);
        clone.Prefix = "Z";
        clone.UIScale = 0.75f;
        Assert.Equal("B", original.Prefix);
        Assert.Equal("F", original.WorkerDir);
        Assert.Equal(1.5f, original.UIScale);
    }

    [Fact]
    public void SaveRootConfig_round_trips_recipes_logs_and_data_dirs()
    {
        var path = Path.Combine(_tmpDir, "RootConfig.xml");
        var cfg = new AppConfig
        {
            Prefix = "ADS_FULL",
            RecipesDir = @"C:\Recipes",
            LogsDir = @"D:\Snapshots",
            DataDir = @"D:\RunData",
            UIScale = 1.0f,
        };

        Config.SaveRootConfig(cfg, path);

        var serializer = new XmlSerializer(typeof(AppConfig));
        using var reader = new StreamReader(path);
        var roundTrip = (AppConfig)serializer.Deserialize(reader)!;

        Assert.Equal(@"C:\Recipes", roundTrip.RecipesDir);
        Assert.Equal(@"D:\Snapshots", roundTrip.LogsDir);
        Assert.Equal(@"D:\RunData", roundTrip.DataDir);
    }

    [Fact]
    public void SnapshotOf_copies_workerdir_recipes_logs_and_data()
    {
        var original = new AppConfig
        {
            WorkerDir = "F", RecipesDir = "R", LogsDir = "L", DataDir = "D",
        };
        var clone = Config.SnapshotOf(original);
        Assert.Equal("F", clone.WorkerDir);
        Assert.Equal("R", clone.RecipesDir);
        Assert.Equal("L", clone.LogsDir);
        Assert.Equal("D", clone.DataDir);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Blank_or_missing_plugin_dirs_resolve_to_kind_subfolders_not_bin_root(string? blank)
    {
        // Regression: a blank/missing <WorkerDir> (e.g. an older RootConfig, or one
        // saved by a dialog that doesn't expose the field) must still find the
        // workers in bin\Workers — NOT fall back to bin\ root, which left the worker
        // loader probing bin\ and reporting "worker not found" on a shipped machine.
        var bin = Path.Combine(_tmpDir, "bin");
        var cfg = new Config(new AppConfig
        {
            Prefix = "DemoProfile",
            WorkDir = bin,
            HWDir = blank,
            GUIDir = blank,
            WorkerDir = blank,
        });

        Assert.Equal(Path.Combine(bin, "HW_LIB"),  cfg.HwDllBasePath);
        Assert.Equal(Path.Combine(bin, "GUI_LIB"), cfg.GUIDllBasePath);
        Assert.Equal(Path.Combine(bin, "Workers"), cfg.WorkerDllBasePath);
    }

    [Fact]
    public void Configured_plugin_dirs_override_the_subfolder_defaults()
    {
        var bin = Path.Combine(_tmpDir, "bin");
        var cfg = new Config(new AppConfig
        {
            Prefix = "DemoProfile",
            WorkDir = bin,
            HWDir = @"C:\custom\hw",
            GUIDir = @"C:\custom\gui",
            WorkerDir = @"C:\custom\workers",
        });

        Assert.Equal(@"C:\custom\hw",      cfg.HwDllBasePath);
        Assert.Equal(@"C:\custom\gui",     cfg.GUIDllBasePath);
        Assert.Equal(@"C:\custom\workers", cfg.WorkerDllBasePath);
    }

    [Fact]
    public void SaveRootConfig_does_not_leave_tmp_when_rename_fails()
    {
        // An existing target file forces SaveRootConfig down the File.Replace
        // branch. Holding an open read handle on the target makes File.Replace fail
        // with IOException — but only AFTER the .tmp has already been written.
        var path = Path.Combine(_tmpDir, "RootConfig.xml");
        File.WriteAllText(path, "<AppConfig><Prefix>OLD</Prefix></AppConfig>");

        var cfg = new AppConfig { Prefix = "NEW", UIScale = 1.0f };
        var tmpPath = path + ".tmp";

        using (var hold = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.ThrowsAny<Exception>(() => Config.SaveRootConfig(cfg, path));
        }

        Assert.False(File.Exists(tmpPath),
            "SaveRootConfig must delete the staged .tmp file when File.Replace fails.");
    }
}
