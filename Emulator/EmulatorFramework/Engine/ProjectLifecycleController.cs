using System.Xml.Serialization;
using APS.SystemEmulator.Setup;
using EmulatorCore.Engine;
using EmulatorCore.Interfaces;
using EmulatorCore.Logging;
using EmulatorCore.Models;

namespace APS.SystemEmulator.Engine;

public class ProjectLifecycleController
{
    public EmulatorSettings Settings { get; }
    public string? ActiveProjectPath { get; private set; }
    public string? ActiveProjectName { get; private set; }
    public SimulationEngine? Engine { get; private set; }
    public EmulatorConfig? Config { get; private set; }
    public IReadOnlyList<VentMapping> VentMappings { get; private set; } = Array.Empty<VentMapping>();

    public event Action<string>? ProjectOpened;
    public event Action<string, Exception>? ProjectOpenFailed;
    public event Action<string>? StateAutoloaded;
    public event Action<string, Exception>? StateAutoloadFailed;

    private readonly string _settingsPath;
    private readonly string? _defaultsBaseDirectory;
    private readonly IReadOnlyList<IDeviceRegistration> _registrations;

    /// <param name="defaultsBaseDirectory">Directory whose sibling "Configs" folder seeds an empty
    /// ConfigsRoot on first run; null disables the seeding.</param>
    public ProjectLifecycleController(EmulatorSettings settings, IReadOnlyList<IDeviceRegistration> registrations, string settingsPath, string? defaultsBaseDirectory = null)
    {
        Settings = settings;
        _registrations = registrations;
        _settingsPath = settingsPath;
        _defaultsBaseDirectory = defaultsBaseDirectory;
    }

    private void AdoptDefaultConfigsRoot()
    {
        if (!string.IsNullOrWhiteSpace(Settings.ConfigsRoot) || _defaultsBaseDirectory == null) return;
        string defaultRoot = FrmOpenProject.DefaultConfigsRoot(_defaultsBaseDirectory);
        if (Directory.Exists(defaultRoot)) SetConfigsRoot(defaultRoot);
    }

    public static string? ProjectPath(EmulatorSettings settings, string? projectName)
    {
        if (string.IsNullOrWhiteSpace(settings.ConfigsRoot) || string.IsNullOrWhiteSpace(projectName)) return null;
        return Path.Combine(settings.ConfigsRoot, projectName);
    }

    public IReadOnlyList<string> ListProjects()
    {
        var root = Settings.ConfigsRoot?.Trim();
        if (string.IsNullOrEmpty(root)) return Array.Empty<string>();
        if (!Directory.Exists(root)) return Array.Empty<string>();

        // EnumerateDirectories is intentionally top-level only — do not add SearchOption.AllDirectories.
        return Directory.EnumerateDirectories(root)
            .Where(d => File.Exists(Path.Combine(d, "HWConf.Emulator.xml")))
            .Select(d => Path.GetFileName(d)!)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void OpenProject(string projectName) => OpenProject(projectName, null);

    /// <summary>Open a project, optionally restoring an explicit .emstate instead of the
    /// remembered one. A non-null override also bypasses the LoadLastStateOnStartup setting:
    /// an explicit --state on the command line always wins.</summary>
    public void OpenProject(string projectName, string? stateFileOverride)
    {
        try
        {
            AdoptDefaultConfigsRoot();
            if (string.IsNullOrWhiteSpace(Settings.ConfigsRoot))
                throw new InvalidOperationException("ConfigsRoot is not set.");

            string projectPath = Path.Combine(Settings.ConfigsRoot, projectName);
            string hwPath = Path.Combine(projectPath, "HWConf.Emulator.xml");
            if (!File.Exists(hwPath))
                throw new FileNotFoundException($"HWConf.Emulator.xml not found in project '{projectName}'.", hwPath);

            // Tear down previous engine (disposes timer and protocol servers).
            SoundManager.ClearMotionSources();
            Engine?.Dispose();
            Engine = null;

            string configPath = Path.Combine(projectPath, "EmulatorConfig.xml");
            if (!File.Exists(configPath))
                throw new FileNotFoundException(
                    $"EmulatorConfig.xml not found in project '{projectName}'.", configPath);

            Config = DeserializeConfig(configPath);

            ConfigLoader.Validate(Config.Physics.Chambers, hwPath);

            var registry = new DeviceRegistry(_registrations);
            var loader = new ConfigLoader(registry, Config.Physics);
            var devices = loader.LoadHWConfig(hwPath);
            VentMappings = ConfigLoader.LoadVentMappings(hwPath);

            var engine = new SimulationEngine(Config.Physics);
            engine.TickIntervalMs = Config.Engine.TickIntervalMs;
            // ClearChambers is a no-op on a freshly constructed engine, but is kept
            // to make the boot order explicit.
            engine.State.ClearChambers();
            foreach (var ch in Config.Physics.Chambers)
            {
                var chamber = new ChamberState();
                chamber.Id = ch.Id;
                chamber.Type = ch.Type;
                chamber.Pressure = ch.InitialPressure;
                chamber.Volume = ch.Volume;
                chamber.BasePressure = ch.BasePressure;
                chamber.BaseLeakRate = ch.LeakRate;
                chamber.LeakRate = ch.LeakRate;

                engine.State.SetChamber(chamber);
            }

            engine.State.VentMappings.AddRange(VentMappings);
            engine.State.SlitConnections.AddRange(ConfigLoader.LoadSlitConnections(hwPath));
            foreach (var d in devices) engine.AddDevice(d);

            registry.WireAll(devices);

            string sourcesPath = Path.Combine(projectPath, "Sources.xml");
            if (File.Exists(sourcesPath))
                engine.LoadSources(sourcesPath, Config.Physics.Magnetrons);

            Engine = engine;
            SoundManager.SetMotionSources(devices.OfType<IAudibleMotion>()
                .Select(d => (Func<bool>)(() => d.IsInMotion))
                .ToList());
            ActiveProjectPath = projectPath;
            ActiveProjectName = projectName;

            Settings.LastProject = projectName;
            Settings.PromoteRecent(projectName);
            Settings.Save(_settingsPath);

            // Autoload last saved state (non-fatal if missing or corrupt).
            TryAutoloadState(projectName, stateFileOverride);

            ProjectOpened?.Invoke(projectName);
        }
        catch (Exception ex)
        {
            EmulatorLog.Log(LogLevel.Error, "ProjectLifecycle", $"OpenProject('{projectName}') failed: {ex.Message}");
            ProjectOpenFailed?.Invoke(projectName, ex);
        }
    }

    private void TryAutoloadState(string projectName, string? stateFileOverride)
    {
        string? path = stateFileOverride;
        if (path is null)
        {
            if (!Settings.LoadLastStateOnStartup) return;
            if (!Settings.LastStateFile.TryGetValue(projectName, out path) || string.IsNullOrWhiteSpace(path))
                return;
        }
        else if (!Path.IsPathRooted(path))
        {
            path = Path.Combine(ActiveProjectPath!, path);
        }

        try
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"State file not found: {path}", path);

            var snapshot = SnapshotService.LoadFromFile(path);
            Engine!.RestoreSnapshot(snapshot);
            StateAutoloaded?.Invoke(path);
        }
        catch (Exception ex)
        {
            EmulatorLog.Log(LogLevel.Warning, "ProjectLifecycle", $"Autoload state failed: {ex.Message}");
            StateAutoloadFailed?.Invoke(path!, ex);
        }
    }

    private static EmulatorConfig DeserializeConfig(string path)
    {
        var serializer = new XmlSerializer(typeof(EmulatorConfig));
        using var stream = File.OpenRead(path);
        return serializer.Deserialize(stream) as EmulatorConfig
            ?? throw new InvalidDataException($"EmulatorConfig.xml at '{path}' could not be deserialized (wrong root element?).");
    }

    public bool TryOpenLastOrPrompt()
    {
        AdoptDefaultConfigsRoot();
        if (string.IsNullOrWhiteSpace(Settings.ConfigsRoot)) return false;
        if (!Directory.Exists(Settings.ConfigsRoot)) return false;
        if (string.IsNullOrWhiteSpace(Settings.LastProject)) return false;

        string projectPath = Path.Combine(Settings.ConfigsRoot, Settings.LastProject);
        if (!File.Exists(Path.Combine(projectPath, "HWConf.Emulator.xml")))
        {
            Settings.LastProject = null;
            Settings.Save(_settingsPath);
            return false;
        }

        string candidate = Settings.LastProject;
        OpenProject(candidate);
        return ActiveProjectName == candidate;
    }

    public void RememberStateFile(string path)
    {
        if (ActiveProjectName == null) return;
        if (string.IsNullOrWhiteSpace(path)) return;
        Settings.LastStateFile[ActiveProjectName] = path;
        Settings.Save(_settingsPath);
    }

    public void SetLoadLastStateOnStartup(bool value)
    {
        Settings.LoadLastStateOnStartup = value;
        Settings.Save(_settingsPath);
    }

    public void SetConfigsRoot(string path)
    {
        Settings.ConfigsRoot = path?.Trim();
        Settings.Save(_settingsPath);
    }
}
