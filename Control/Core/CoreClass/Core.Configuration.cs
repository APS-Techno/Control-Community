using System.Xml.Serialization;

namespace APS.SystemControl.Core.CoreClass;


public class AppConfig
{
    public string? ConfigDir { get; set; }

    public string? Prefix { get; set; }
    public string? WorkDir { get; set; }
    
    public string? HWDir { get; set; }

    public string? GUIDir { get; set; }

    public string? WorkerDir { get; set; }

    public string? RecipesDir { get; set; }

    public string? LogsDir { get; set; }

    /// <summary>Root folder for run data: TSV data files + .depo.json deposition
    /// snapshots (&lt;DataDir&gt; element). Blank/missing = WorkDir\Data.</summary>
    public string? DataDir { get; set; }

    /// <summary>Manual UI scale override (&gt;0 enables, clamped [0.5, 2.0]). 0 or missing = auto.</summary>
    public float UIScale { get; set; }

    /// <summary>Log user (.recipe/.rcpx) runs: TSV data file + step-log stream. Default on.</summary>
    public bool LogUserRecipes { get; set; } = true;

    /// <summary>Log embedded (SYSTEM_*, LOAD_*, …) runs: TSV data file + step-log stream. Default off.</summary>
    public bool LogEmbeddedRecipes { get; set; } = false;

    [XmlAnyElement]
    public System.Xml.XmlElement[]? ExtraElements { get; set; }
}

internal class Config
{
    AppConfig? appConfig;

    public string WorkDir
    {
        get
        {
            string? workDir = appConfig!.WorkDir;
            return string.IsNullOrEmpty(workDir) ? AppDomain.CurrentDomain.BaseDirectory : workDir;
        }
    }
    // Plugin DLLs live in by-kind subfolders under bin\ (HW_LIB / GUI_LIB /
    // Workers). When the corresponding RootConfig entry is missing OR blank, fall
    // back to that standard subfolder — NOT bin\ root. A blank <WorkerDir> (e.g.
    // from an older RootConfig) must still find bin\Workers, otherwise the worker
    // loader probes bin\ and reports "worker not found".
    public string HwDllBasePath
    {
        get
        {
           if (!string.IsNullOrEmpty(appConfig!.HWDir))
             return Resolve(appConfig.HWDir);
           else
             return Path.Combine(WorkDir, @"HW_LIB");
        }
    }

    public string GUIDllBasePath
    {
        get
        {
            if (!string.IsNullOrEmpty(appConfig!.GUIDir))
                return Resolve(appConfig.GUIDir);
            else
                return Path.Combine(WorkDir, @"GUI_LIB");
        }
    }
    public string WorkerDllBasePath
    {
        get
        {
            if (!string.IsNullOrEmpty(appConfig!.WorkerDir))
                return Resolve(appConfig.WorkerDir);
            else
                return Path.Combine(WorkDir, @"Workers");
        }
    }
    private string ImagesDir => Path.Combine(WorkDir, @"Images");
    public string SystemScheme { get => Path.Combine(ImagesDir, appConfig!.Prefix + ".png"); }
    public string ConfigDir => Path.Combine(Resolve(appConfig!.ConfigDir!), appConfig.Prefix);

    public string RecipesDir =>
        !string.IsNullOrEmpty(appConfig!.RecipesDir) ? Resolve(appConfig.RecipesDir)
                                                      : Path.Combine(ConfigDir, "Recipes");

    public string LogsDir =>
        !string.IsNullOrEmpty(appConfig!.LogsDir) ? Resolve(appConfig.LogsDir)
                                                  : Path.Combine(WorkDir, "Logs");

    public string DataDir =>
        !string.IsNullOrEmpty(appConfig!.DataDir) ? Resolve(appConfig.DataDir)
                                                  : Path.Combine(WorkDir, "Data");

    private string Resolve(string path) => ResolvePath(path, appConfig!.WorkDir);

    internal static string ResolvePath(string path, string? workDir)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path)) return path;
        string baseDir = string.IsNullOrEmpty(workDir) ? AppDomain.CurrentDomain.BaseDirectory : workDir;
        return Path.GetFullPath(Path.Combine(baseDir, path));
    }
    public string GUIVacuumConfigFile => Path.Combine(ConfigDir, @"Controls.xml");
    public string HWConfigFile => Path.Combine(ConfigDir, @"HWConf.xml");
    public string WorkerConfigFile => Path.Combine(ConfigDir, @"Worker.xml");
    public string EmbedRecipesFile => Path.Combine(ConfigDir, @"EmbedRecipes.xml");
    public string InterlocksFile => Path.Combine(ConfigDir, @"Interlocks.xml");
    public string AlarmFile => Path.Combine(ConfigDir, @"Alarms.xml");
    public string DefaultJobsList => Path.Combine(ConfigDir, @"DefaultJobs.xml");
    public string GUIPanelsConfigFile => Path.Combine(ConfigDir, @"Panels.xml");
    public string MenusConfigFile => Path.Combine(ConfigDir, @"Menus.xml");
    public string GraphFile => Path.Combine(ConfigDir, @"Graphs.xml");
    public string SourcesFile => Path.Combine(ConfigDir, @"Sources.xml");
    public string RemoteProfileFile => Path.Combine(ConfigDir, @"RemoteProfile.xml");

    private void LoadRootConfig(string filePath)
    {
        XmlSerializer serializer = new(typeof(AppConfig));

        try
        {
            using StreamReader reader = new(filePath);
            appConfig = (AppConfig)serializer.Deserialize(reader)!;
        }
        catch (Exception ex)
        {
            // RootConfig.xml is hand-edited on target machines — fail with the
            // path and the cause so the crash dialog shows something the
            // operator can fix, not a bare NullReferenceException (audit C5).
            throw new InvalidOperationException(
                $"RootConfig could not be loaded from '{filePath}': {ex.Message}", ex);
        }
    }

    public Config() => LoadRootConfig(
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RootConfig.xml"));

    internal Config(string filePath) => LoadRootConfig(filePath);

    internal Config(AppConfig cfg) => appConfig = cfg;

    public bool LogUserRecipes => appConfig?.LogUserRecipes ?? true;

    public bool LogEmbeddedRecipes => appConfig?.LogEmbeddedRecipes ?? false;

    public AppConfig Snapshot() => SnapshotOf(appConfig ?? new AppConfig());

    public static AppConfig SnapshotOf(AppConfig src) => new AppConfig
    {
        ConfigDir = src.ConfigDir,
        Prefix = src.Prefix,
        WorkDir = src.WorkDir,
        HWDir = src.HWDir,
        GUIDir = src.GUIDir,
        WorkerDir = src.WorkerDir,
        RecipesDir = src.RecipesDir,
        LogsDir = src.LogsDir,
        DataDir = src.DataDir,
        UIScale = src.UIScale,
        LogUserRecipes = src.LogUserRecipes,
        LogEmbeddedRecipes = src.LogEmbeddedRecipes,
        // Elements this class does not model (an edition's own sections, e.g. <Remote>)
        // must survive a save from the App Settings dialog.
        ExtraElements = src.ExtraElements,
    };

    /// <summary>
    /// Atomically writes <paramref name="cfg"/> to <paramref name="filePath"/>.
    /// Writes to "&lt;filePath&gt;.tmp" first, then File.Replace renames to the target
    /// and preserves the prior file as "&lt;filePath&gt;.bak". Throws on IO failure.
    /// </summary>
    public static void SaveRootConfig(AppConfig cfg, string filePath)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        var tmpPath = filePath + ".tmp";
        var serializer = new XmlSerializer(typeof(AppConfig));
        using (var writer = new StreamWriter(tmpPath))
        {
            serializer.Serialize(writer, cfg);
        }

        try
        {
            if (File.Exists(filePath))
            {
                var bakPath = filePath + ".bak";
                File.Replace(tmpPath, filePath, bakPath);
            }
            else
            {
                File.Move(tmpPath, filePath);
            }
        }
        catch
        {
            try { File.Delete(tmpPath); } catch { /* best-effort */ }
            throw;
        }
    }

    public void Save(string fileName = "RootConfig.xml")
    {
        if (appConfig is null)
            throw new InvalidOperationException(
                "Cannot save: RootConfig.xml was not successfully loaded.");
        var workDir = AppDomain.CurrentDomain.BaseDirectory;
        var filePath = Path.Combine(workDir, fileName);
        SaveRootConfig(appConfig, filePath);
    }
}
