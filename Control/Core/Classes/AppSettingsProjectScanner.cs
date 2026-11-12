using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace APS.SystemControl.Core.Classes;

internal static class AppSettingsProjectScanner
{
    /// <summary>
    /// Returns names (not full paths) of immediate subdirectories of
    /// <paramref name="configDir"/> that contain a Config.xml file.
    /// Returns empty for null, empty, or missing directories.
    /// Results are sorted alphabetically (case-sensitive, ordinal).
    /// </summary>
    public static IReadOnlyList<string> ScanProjects(string configDir)
    {
        if (string.IsNullOrWhiteSpace(configDir) || !Directory.Exists(configDir))
            return Array.Empty<string>();

        return Directory.EnumerateDirectories(configDir)
            .Where(d => File.Exists(Path.Combine(d, "Config.xml")))
            .Select(d => Path.GetFileName(d)!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }
}
