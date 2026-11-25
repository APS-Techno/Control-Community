using APS.SystemEmulator.Engine;
using APS.SystemEmulator.Hosting;
using APS.SystemEmulator.Setup;
using EmulatorCore.Interfaces;

namespace EmulatorTests;

public class ProjectLifecycleControllerTests
{
    private static readonly IReadOnlyList<IDeviceRegistration> Regs = EmulatorEdition.Community.Registrations;

    private static string MakeTempConfigsRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), $"aps-emu-cfg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void CreateValidProject(string root, string name)
    {
        string dir = Path.Combine(root, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "HWConf.Emulator.xml"), "<Hardware></Hardware>");
    }

    [Fact]
    public void ListProjects_ReturnsOnlyFoldersWithHWConfEmulator()
    {
        string root = MakeTempConfigsRoot();
        try
        {
            CreateValidProject(root, "DemoProfile");
            CreateValidProject(root, "ADS_FULL");
            Directory.CreateDirectory(Path.Combine(root, "Wizard_Test")); // no HWConf.Emulator.xml
            File.WriteAllText(Path.Combine(root, "notes.txt"), "");       // stray file

            var settings = new EmulatorSettings { ConfigsRoot = root };
            var controller = new ProjectLifecycleController(settings, Regs, Path.Combine(root, "s.json"));

            var projects = controller.ListProjects();
            Assert.Contains("DemoProfile", projects);
            Assert.Contains("ADS_FULL", projects);
            Assert.DoesNotContain("Wizard_Test", projects);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ListProjects_NoConfigsRoot_ReturnsEmpty()
    {
        var settings = new EmulatorSettings();
        var controller = new ProjectLifecycleController(settings, Regs, Path.Combine(Path.GetTempPath(), "aps-unused-settings.json"));
        Assert.Empty(controller.ListProjects());
    }

    [Fact]
    public void ListProjects_MissingRootDir_ReturnsEmpty()
    {
        var settings = new EmulatorSettings { ConfigsRoot = @"C:\no\such\dir" };
        var controller = new ProjectLifecycleController(settings, Regs, Path.Combine(Path.GetTempPath(), "aps-unused-settings.json"));
        Assert.Empty(controller.ListProjects());
    }

    [Fact]
    public void ListProjects_TrimsWhitespaceInConfigsRoot()
    {
        string root = MakeTempConfigsRoot();
        try
        {
            CreateValidProject(root, "DemoProfile");
            var settings = new EmulatorSettings { ConfigsRoot = "  " + root + "  " };
            var controller = new ProjectLifecycleController(settings, Regs, Path.Combine(root, "s.json"));
            Assert.Contains("DemoProfile", controller.ListProjects());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private const string MinimalHwConf = """
        <Hardware>
          <Valves hwtype="Vendor.S7200Valve">
            <Valve address="0" index="0" delay="1.0">GVM</Valve>
          </Valves>
          <sensors hwtype="Vendor.S7200Sensor">
            <sensor address="100" chamber="Main" descr="Main pressure">pm</sensor>
          </sensors>
          <pumps hwtype="Vendor.S7200Pump">
            <pump address="0" index="5" chamber="Main" gate="" descr="Scroll pump">SP1</pump>
          </pumps>
        </Hardware>
        """;

    private const string MinimalEmulatorConfig = """
        <EmulatorConfig>
          <Engine TickIntervalMs="100" Sound="false" />
          <Physics>
            <Chamber id="Main">
              <InitialPressure>760</InitialPressure>
              <Volume>750</Volume>
              <BasePressure>1E-7</BasePressure>
              <LeakRate>1E-6</LeakRate>
              <PumpdownTau>5.0</PumpdownTau>
            </Chamber>
          </Physics>
        </EmulatorConfig>
        """;

    private static void CreateProjectWithConfig(string root, string name)
    {
        string dir = Path.Combine(root, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "HWConf.Emulator.xml"), MinimalHwConf);
        File.WriteAllText(Path.Combine(dir, "EmulatorConfig.xml"), MinimalEmulatorConfig);
    }

    private static string MakeBaseWithConfigs(string temp, bool createConfigs, out string configs)
    {
        string bin = Path.Combine(temp, "bin");
        Directory.CreateDirectory(bin);
        configs = Path.Combine(temp, "Configs");
        if (createConfigs) CreateProjectWithConfig(configs, "Sputter");
        return bin + Path.DirectorySeparatorChar;
    }

    [Fact]
    public void OpenProject_EmptyConfigsRoot_AdoptsAndPersistsDefaultConfigsRoot()
    {
        string temp = MakeTempConfigsRoot();
        try
        {
            string baseDir = MakeBaseWithConfigs(temp, true, out string configs);
            string settingsPath = Path.Combine(temp, "settings.json");
            var settings = new EmulatorSettings();
            var c = new ProjectLifecycleController(settings, Regs, settingsPath, baseDir);

            c.OpenProject("Sputter");

            Assert.Equal("Sputter", c.ActiveProjectName);
            Assert.Equal(configs, settings.ConfigsRoot);
            Assert.Equal(configs, EmulatorSettings.Load(settingsPath).ConfigsRoot);
            c.Engine?.Dispose();
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public void OpenProject_EmptyConfigsRoot_NoDefaultFolder_FailsUnchanged()
    {
        string temp = MakeTempConfigsRoot();
        try
        {
            string baseDir = MakeBaseWithConfigs(temp, false, out _);
            var settings = new EmulatorSettings();
            var c = new ProjectLifecycleController(settings, Regs, Path.Combine(temp, "settings.json"), baseDir);
            Exception? captured = null;
            c.ProjectOpenFailed += (_, ex) => captured = ex;

            c.OpenProject("Sputter");

            Assert.Null(c.ActiveProjectName);
            Assert.Contains("ConfigsRoot is not set", captured!.Message);
            Assert.True(string.IsNullOrEmpty(settings.ConfigsRoot));
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public void OpenProject_ConfigsRootAlreadySet_IsNotOverwrittenByDefault()
    {
        string temp = MakeTempConfigsRoot();
        try
        {
            string baseDir = MakeBaseWithConfigs(temp, true, out _);
            string custom = Path.Combine(temp, "Custom");
            CreateProjectWithConfig(custom, "Mine");
            var settings = new EmulatorSettings { ConfigsRoot = custom };
            var c = new ProjectLifecycleController(settings, Regs, Path.Combine(temp, "settings.json"), baseDir);

            c.OpenProject("Mine");

            Assert.Equal("Mine", c.ActiveProjectName);
            Assert.Equal(custom, settings.ConfigsRoot);
            c.Engine?.Dispose();
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    // Must carry at least one chamber: LoadFromFile throws InvalidDataException on an empty
    // Chambers dict (a snapshot that restores nothing must fail loudly, not silently).
    private const string MinimalSnapshotJson = """
        {
          "Timestamp": "2026-01-01T00:00:00",
          "SimTime": 0,
          "Chambers": {
            "Main": { "Id": "Main", "Type": 0, "Pressure": 0.01, "Volume": 750.0,
                      "BasePressure": 1E-07, "BaseLeakRate": 1E-06, "LeakRate": 1E-06,
                      "PumpSpeed": 0.0, "GasLoad": 0.0, "ThrottlePosition": 0.0 }
          },
          "Thermal": { "Temperature": 20, "ChamberTemperature": 20, "AmbientTemp": 20, "HeatLoad": 0, "SubstrateHeatLoad": 0 },
          "ValveStates": {},
          "DeviceStates": {}
        }
        """;

    [Fact]
    public void OpenProject_Valid_LoadsEngineAndRaisesOpened()
    {
        string root = MakeTempConfigsRoot();
        try
        {
            CreateProjectWithConfig(root, "DemoProfile");
            var settings = new EmulatorSettings { ConfigsRoot = root };
            string settingsPath = Path.Combine(root, "settings.json");
            var controller = new ProjectLifecycleController(settings, Regs, settingsPath);

            string? opened = null;
            controller.ProjectOpened += name => opened = name;

            controller.OpenProject("DemoProfile");

            Assert.Equal("DemoProfile", opened);
            Assert.Equal("DemoProfile", controller.ActiveProjectName);
            Assert.NotNull(controller.Engine);
            Assert.NotNull(controller.Config);
            Assert.Equal("DemoProfile", settings.LastProject);
            Assert.Contains("DemoProfile", settings.RecentProjects);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void OpenProject_InvalidName_RaisesFailedAndDoesNotUpdateSettings()
    {
        string root = MakeTempConfigsRoot();
        try
        {
            var settings = new EmulatorSettings { ConfigsRoot = root };
            var controller = new ProjectLifecycleController(settings, Regs, Path.Combine(root, "s.json"));
            Exception? captured = null;
            controller.ProjectOpenFailed += (_, ex) => captured = ex;

            controller.OpenProject("DoesNotExist");

            Assert.NotNull(captured);
            Assert.Null(settings.LastProject);
            Assert.Null(controller.Engine);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void OpenProject_NoPerProjectConfig_FailsWithFileNotFound()
    {
        string root = MakeTempConfigsRoot();
        try
        {
            string dir = Path.Combine(root, "Bare");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "HWConf.Emulator.xml"), MinimalHwConf);
            // No EmulatorConfig.xml in project folder — every project must own its config.

            var settings = new EmulatorSettings { ConfigsRoot = root };
            var controller = new ProjectLifecycleController(settings, Regs, Path.Combine(root, "s.json"));

            Exception? captured = null;
            controller.ProjectOpenFailed += (_, ex) => captured = ex;
            controller.OpenProject("Bare");

            Assert.IsType<FileNotFoundException>(captured);
            Assert.Null(controller.Engine);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void OpenProject_SecondOpen_ReplacesFirstEngineWithNewInstance()
    {
        string root = MakeTempConfigsRoot();
        try
        {
            CreateProjectWithConfig(root, "Proj1");
            CreateProjectWithConfig(root, "Proj2");
            var settings = new EmulatorSettings { ConfigsRoot = root };
            var controller = new ProjectLifecycleController(settings, Regs, Path.Combine(root, "s.json"));

            controller.OpenProject("Proj1");
            var firstEngine = controller.Engine!;

            controller.OpenProject("Proj2");
            var secondEngine = controller.Engine!;

            Assert.NotSame(firstEngine, secondEngine);
            Assert.Equal("Proj2", controller.ActiveProjectName);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void OpenProject_WithAutoloadEnabledAndSavedState_RestoresEngineState()
    {
        string root = MakeTempConfigsRoot();
        try
        {
            CreateProjectWithConfig(root, "DemoProfile");

            var s1 = new EmulatorSettings { ConfigsRoot = root, LoadLastStateOnStartup = false };
            var c1 = new ProjectLifecycleController(s1, Regs, Path.Combine(root, "s.json"));
            c1.OpenProject("DemoProfile");
            c1.Engine!.State.Chambers["Main"].Pressure = 1.23e-5f;

            string stateDir = Path.Combine(root, "DemoProfile", "Snapshots");
            Directory.CreateDirectory(stateDir);
            string stateFile = Path.Combine(stateDir, "saved.emstate");
            var snap = EmulatorCore.Models.SnapshotService.Capture(c1.Engine.State, c1.Engine.Devices);
            EmulatorCore.Models.SnapshotService.SaveToFile(snap, stateFile);

            var s2 = new EmulatorSettings
            {
                ConfigsRoot = root,
                LoadLastStateOnStartup = true,
                LastStateFile = new Dictionary<string, string> { { "DemoProfile", stateFile } },
            };
            var c2 = new ProjectLifecycleController(s2, Regs, Path.Combine(root, "s2.json"));
            string? autoloaded = null;
            c2.StateAutoloaded += p => autoloaded = p;

            c2.OpenProject("DemoProfile");

            Assert.Equal(stateFile, autoloaded);
            Assert.Equal(1.23e-5f, c2.Engine!.State.Chambers["Main"].Pressure, 3);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void OpenProject_AutoloadMissingFile_RaisesFailedAndKeepsCleanEngine()
    {
        string root = MakeTempConfigsRoot();
        try
        {
            CreateProjectWithConfig(root, "DemoProfile");
            var s = new EmulatorSettings
            {
                ConfigsRoot = root,
                LoadLastStateOnStartup = true,
                LastStateFile = new Dictionary<string, string>
                {
                    { "DemoProfile", Path.Combine(root, "DemoProfile", "Snapshots", "missing.emstate") },
                },
            };
            var c = new ProjectLifecycleController(s, Regs, Path.Combine(root, "s.json"));
            Exception? err = null;
            c.StateAutoloadFailed += (_, ex) => err = ex;

            c.OpenProject("DemoProfile");

            Assert.NotNull(err);
            Assert.NotNull(c.Engine);   // engine still up — autoload failure is non-fatal
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void OpenProject_AutoloadDisabled_DoesNotRestoreState()
    {
        string root = MakeTempConfigsRoot();
        try
        {
            CreateProjectWithConfig(root, "DemoProfile");
            var s = new EmulatorSettings
            {
                ConfigsRoot = root,
                LoadLastStateOnStartup = false,
                LastStateFile = new Dictionary<string, string> { { "DemoProfile", "ignored" } },
            };
            var c = new ProjectLifecycleController(s, Regs, Path.Combine(root, "s.json"));
            bool autoCalled = false;
            c.StateAutoloaded += _ => autoCalled = true;
            c.StateAutoloadFailed += (_, _) => autoCalled = true;

            c.OpenProject("DemoProfile");

            Assert.False(autoCalled);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void TryOpenLastOrPrompt_NoLastProject_ReturnsFalse()
    {
        string root = MakeTempConfigsRoot();
        try
        {
            var s = new EmulatorSettings { ConfigsRoot = root };
            var c = new ProjectLifecycleController(s, Regs, Path.Combine(root, "s.json"));
            Assert.False(c.TryOpenLastOrPrompt());
            Assert.Null(c.ActiveProjectName);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void TryOpenLastOrPrompt_WithValidLastProject_OpensIt()
    {
        string root = MakeTempConfigsRoot();
        try
        {
            CreateProjectWithConfig(root, "DemoProfile");
            var s = new EmulatorSettings { ConfigsRoot = root, LastProject = "DemoProfile" };
            var c = new ProjectLifecycleController(s, Regs, Path.Combine(root, "s.json"));
            Assert.True(c.TryOpenLastOrPrompt());
            Assert.Equal("DemoProfile", c.ActiveProjectName);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void TryOpenLastOrPrompt_InvalidLastProject_ReturnsFalseAndClearsIt()
    {
        string root = MakeTempConfigsRoot();
        try
        {
            var s = new EmulatorSettings { ConfigsRoot = root, LastProject = "Ghost" };
            var c = new ProjectLifecycleController(s, Regs, Path.Combine(root, "s.json"));
            Assert.False(c.TryOpenLastOrPrompt());
            Assert.Null(s.LastProject);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void RememberStateFile_UpdatesAndPersistsSettings()
    {
        string root = MakeTempConfigsRoot();
        try
        {
            CreateProjectWithConfig(root, "DemoProfile");
            string settingsPath = Path.Combine(root, "s.json");
            var s = new EmulatorSettings { ConfigsRoot = root };
            var c = new ProjectLifecycleController(s, Regs, settingsPath);
            c.OpenProject("DemoProfile");

            string stateFile = Path.Combine(root, "DemoProfile", "Snapshots", "saved.emstate");
            c.RememberStateFile(stateFile);

            Assert.Equal(stateFile, s.LastStateFile["DemoProfile"]);
            var reloaded = EmulatorSettings.Load(settingsPath);
            Assert.Equal(stateFile, reloaded.LastStateFile["DemoProfile"]);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void RememberStateFile_EmptyOrWhitespacePath_IsIgnored()
    {
        string root = MakeTempConfigsRoot();
        try
        {
            CreateProjectWithConfig(root, "DemoProfile");
            var s = new EmulatorSettings { ConfigsRoot = root };
            var c = new ProjectLifecycleController(s, Regs, Path.Combine(root, "s.json"));
            c.OpenProject("DemoProfile");

            c.RememberStateFile("");
            c.RememberStateFile("   ");

            Assert.False(s.LastStateFile.ContainsKey("DemoProfile"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void OpenProject_WithStateOverride_RestoresThatFileEvenWhenAutoloadDisabled()
    {
        string root = MakeTempConfigsRoot();
        try
        {
            CreateProjectWithConfig(root, "DemoProfile");
            string dir = Path.Combine(root, "DemoProfile");
            string stateFile = Path.Combine(dir, "explicit.emstate");
            File.WriteAllText(stateFile, MinimalSnapshotJson);

            var settings = new EmulatorSettings { ConfigsRoot = root, LoadLastStateOnStartup = false };
            var ctrl = new ProjectLifecycleController(settings, Regs, Path.Combine(root, "settings.json"));

            string? autoloaded = null;
            ctrl.StateAutoloaded += p => autoloaded = p;
            ctrl.OpenProject("DemoProfile", stateFile);

            Assert.Equal(stateFile, autoloaded);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void OpenProject_WithStateOverride_PreservesRestoredSimTimeAcrossSubsequentTicks()
    {
        string root = MakeTempConfigsRoot();
        try
        {
            CreateProjectWithConfig(root, "DemoProfile");
            string dir = Path.Combine(root, "DemoProfile");
            string stateFile = Path.Combine(dir, "explicit.emstate");
            File.WriteAllText(stateFile, MinimalSnapshotJson.Replace("\"SimTime\": 0,", "\"SimTime\": 3271.9,"));

            var settings = new EmulatorSettings { ConfigsRoot = root, LoadLastStateOnStartup = false };
            var ctrl = new ProjectLifecycleController(settings, Regs, Path.Combine(root, "settings.json"));

            ctrl.OpenProject("DemoProfile", stateFile);
            ctrl.Engine!.Time.Start();
            ctrl.Engine.Tick();

            Assert.Equal(3272.0f, ctrl.Engine.State.SimTime, 0.05f);
            Assert.Equal(3272.0f, ctrl.Engine.Time.SimTime, 0.05f);
            ctrl.Engine.Dispose();
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
