using System.Drawing;
using System.Windows.Forms;
using APS.SystemEmulator.Hosting;
using APS.SystemEmulator.Setup;

namespace EmulatorTests;

public class EmulatorSettingsTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"aps-emu-settings-{Guid.NewGuid():N}.json");

    [Fact]
    public void Load_NonExistentFile_ReturnsDefaults()
    {
        var s = EmulatorSettings.Load("C:\\no\\such\\file.json");
        Assert.Null(s.ConfigsRoot);
        Assert.Null(s.LastProject);
        Assert.False(s.LoadLastStateOnStartup);
        Assert.Empty(s.RecentProjects);
        Assert.Empty(s.LastStateFile);
    }

    [Fact]
    public void Load_MalformedJson_ReturnsDefaults()
    {
        string path = TempPath();
        File.WriteAllText(path, "{ this is not json");
        try
        {
            var s = EmulatorSettings.Load(path);
            Assert.Null(s.ConfigsRoot);
            Assert.Null(s.LastProject);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Roundtrip_PersistsAllFields()
    {
        string path = TempPath();
        var original = new EmulatorSettings
        {
            ConfigsRoot = @"C:\Lab\Configs",
            LastProject = "DemoProfile",
            LoadLastStateOnStartup = true,
            RecentProjects = new List<string> { "DemoProfile", "ADS_FULL" },
            LastStateFile = new Dictionary<string, string>
            {
                { "DemoProfile", @"C:\Lab\Configs\DemoProfile\Snapshots\last.emstate" },
                { "ADS_FULL", @"C:\Lab\Configs\ADS_FULL\Snapshots\demo.emstate" },
            },
            ControlRootConfigPath = @"C:\Lab\Control\bin\RootConfig.xml",
        };
        try
        {
            original.Save(path);
            var loaded = EmulatorSettings.Load(path);
            Assert.Equal(original.ConfigsRoot, loaded.ConfigsRoot);
            Assert.Equal(original.LastProject, loaded.LastProject);
            Assert.True(loaded.LoadLastStateOnStartup);
            Assert.Equal(new[] { "DemoProfile", "ADS_FULL" }, loaded.RecentProjects);
            Assert.Equal(2, loaded.LastStateFile.Count);
            Assert.Equal(original.LastStateFile["DemoProfile"], loaded.LastStateFile["DemoProfile"]);
            Assert.Equal(original.ControlRootConfigPath, loaded.ControlRootConfigPath);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void PromoteRecent_MovesToFrontAndCapsAtFive()
    {
        var s = new EmulatorSettings();
        foreach (var p in new[] { "A", "B", "C", "D", "E" }) s.PromoteRecent(p);
        Assert.Equal(new[] { "E", "D", "C", "B", "A" }, s.RecentProjects);

        s.PromoteRecent("B");
        Assert.Equal(new[] { "B", "E", "D", "C", "A" }, s.RecentProjects);

        s.PromoteRecent("F");
        Assert.Equal(new[] { "F", "B", "E", "D", "C" }, s.RecentProjects);
        Assert.Equal(5, s.RecentProjects.Count);
    }

    [Fact]
    public void SettingsPathFor_UsesEditionFolder()
    {
        string path = EmulatorSettings.SettingsPathFor("APS.EmulatorCommunity");
        string expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "APS.EmulatorCommunity", "settings.json");
        Assert.Equal(expected, path);
    }

    [Fact]
    public void Community_SettingsFolder_IsNotTheCommercialOne()
    {
        Assert.Equal("APS.EmulatorCommunity", EmulatorEdition.Community.SettingsFolder);
        Assert.NotEqual("APS.SystemEmulator", EmulatorEdition.Community.SettingsFolder);
    }

    [Fact]
    public void WindowPlacement_RoundtripsThroughJson()
    {
        var path = TempPath();
        try
        {
            var s = new EmulatorSettings();
            s.RememberWindow(FormWindowState.Normal, FormWindowState.Normal, new Rectangle(40, 30, 1300, 800));
            s.Save(path);

            var loaded = EmulatorSettings.Load(path);

            Assert.False(loaded.WindowMaximized);
            Assert.True(loaded.HasWindowBounds);
            Assert.Equal((40, 30, 1300, 800), (loaded.WindowX, loaded.WindowY, loaded.WindowWidth, loaded.WindowHeight));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LegacySettings_HaveNoWindowBounds()
    {
        var path = TempPath();
        try
        {
            File.WriteAllText(path, "{\"lastProject\":\"X\"}");
            var loaded = EmulatorSettings.Load(path);
            Assert.Null(loaded.WindowMaximized);
            Assert.False(loaded.HasWindowBounds);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RememberWindow_MinimizedKeepsPreviousStateAndBounds()
    {
        var s = new EmulatorSettings();
        s.RememberWindow(FormWindowState.Normal, FormWindowState.Normal, new Rectangle(40, 30, 1300, 800));
        s.RememberWindow(FormWindowState.Minimized, FormWindowState.Maximized, new Rectangle(-32000, -32000, 160, 28));

        Assert.True(s.WindowMaximized);
        Assert.Equal((40, 30, 1300, 800), (s.WindowX, s.WindowY, s.WindowWidth, s.WindowHeight));
    }

    [Fact]
    public void FitWindow_CentersOnPrimary_WhenSavedMonitorIsGone()
    {
        var primary = new Rectangle(0, 0, 1920, 1040);
        var saved = new Rectangle(2100, 100, 1400, 800);

        Assert.Equal(saved, EmulatorSettings.FitWindow(saved, [primary, new Rectangle(1920, 0, 1920, 1040)], primary));
        Assert.Equal(new Rectangle(260, 120, 1400, 800), EmulatorSettings.FitWindow(saved, [primary], primary));
    }

    [Fact]
    public void DefaultConfigsRoot_IsSiblingOfBin()
        => Assert.Equal(@"C:\x\Configs", FrmOpenProject.DefaultConfigsRoot(@"C:\x\bin\"));
}
