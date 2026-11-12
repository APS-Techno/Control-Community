using System.Text.Json;
using System.Text.Json.Serialization;

namespace APS.SystemEmulator.Setup;

public class EmulatorSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [JsonPropertyName("configsRoot")]
    public string? ConfigsRoot { get; set; }

    [JsonPropertyName("lastProject")]
    public string? LastProject { get; set; }

    [JsonPropertyName("loadLastStateOnStartup")]
    public bool LoadLastStateOnStartup { get; set; }

    [JsonPropertyName("recentProjects")]
    public List<string> RecentProjects { get; set; } = new();

    [JsonPropertyName("lastStateFile")]
    public Dictionary<string, string> LastStateFile { get; set; } = new();

    [JsonPropertyName("controlRootConfigPath")]
    public string? ControlRootConfigPath { get; set; }

    public static string SettingsPathFor(string settingsFolder)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, settingsFolder, "settings.json");
    }

    /// <summary>Returns a default instance if the file is missing or unreadable.</summary>
    internal static EmulatorSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new EmulatorSettings();
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<EmulatorSettings>(json, JsonOptions) ?? new EmulatorSettings();
        }
        catch
        {
            return new EmulatorSettings();
        }
    }

    /// <summary>Creates the directory if needed.</summary>
    internal void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(path, json);
    }

    public void PromoteRecent(string projectName)
    {
        RecentProjects.Remove(projectName);
        RecentProjects.Insert(0, projectName);
        while (RecentProjects.Count > 5)
            RecentProjects.RemoveAt(RecentProjects.Count - 1);
    }
}
