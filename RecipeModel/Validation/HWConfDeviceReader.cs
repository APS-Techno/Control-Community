using System.Xml.Linq;

namespace APS.RecipeModel.Validation;

public static class HWConfDeviceReader
{
    private static readonly (string element, string label)[] Sections =
    [
        ("valves", "Valves"),
        ("mfcs", "MFCs"),
        ("pumps", "Pumps"),
        ("stages", "Stages"),
        ("sensors", "Sensors"),
        ("powersupplies", "Power Supplies"),
        ("drives", "Drives"),
        ("controllers", "Controllers"),
    ];

    /// <summary>
    /// Returns an empty dictionary if the file doesn't exist or can't be parsed.
    /// </summary>
    public static Dictionary<string, List<string>> ReadDevicesByCategory(string hwConfPath)
    {
        var result = new Dictionary<string, List<string>>();

        if (!File.Exists(hwConfPath))
            return result;

        try
        {
            var doc = XDocument.Load(hwConfPath);
            var hardware = doc.Element("hardware");
            if (hardware is null) return result;

            foreach (var (element, label) in Sections)
            {
                var names = new List<string>();
                foreach (var group in hardware.Elements(element))
                {
                    foreach (var child in group.Elements())
                    {
                        string name = child.Value.Trim();
                        if (name.Length > 0)
                            names.Add(name);
                    }
                }
                if (names.Count > 0)
                    result[label] = names;
            }

            // Robot faces are a synthetic category: <robots>/<robot>/<face name="..."/>.
            // "park" and "Home" are always-valid synonyms for face 0 and are prepended.
            var faces = ReadRobotFaceNames(hardware);
            if (faces.Count > 0)
            {
                var withSynonyms = new List<string> { "park", "Home" };
                withSynonyms.AddRange(faces);
                result["Robot Faces"] = withSynonyms;
            }
        }
        catch
        {
            // If the file can't be read, return empty — validation will skip device checks
        }

        return result;
    }

    public static HashSet<string> ReadDeviceNames(string hwConfPath)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var devices in ReadDevicesByCategory(hwConfPath).Values)
            foreach (var name in devices)
                names.Add(name);
        return names;
    }

    /// <summary>Ids of <c>&lt;stage operational="false"&gt;</c> entries; empty when the file is missing or unreadable.</summary>
    public static HashSet<string> ReadNonOperationalStages(string hwConfPath)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(hwConfPath))
            return ids;

        try
        {
            var doc = XDocument.Load(hwConfPath);
            foreach (var stage in doc.Descendants("stage"))
            {
                if (bool.TryParse(stage.Attribute("operational")?.Value, out bool operational) && !operational)
                    ids.Add(stage.Value.Trim());
            }
        }
        catch
        {
            return ids;
        }

        return ids;
    }

    /// <summary>Excludes the park/Home synonyms.</summary>
    public static HashSet<string> ReadRobotFaces(string hwConfPath)
    {
        var faces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(hwConfPath)) return faces;
        try
        {
            var hardware = XDocument.Load(hwConfPath).Element("hardware");
            if (hardware != null)
                foreach (var f in ReadRobotFaceNames(hardware)) faces.Add(f);
        }
        catch { /* unreadable -> empty */ }
        return faces;
    }

    private static List<string> ReadRobotFaceNames(XElement hardware)
    {
        var names = new List<string>();
        foreach (var robots in hardware.Elements("robots"))
            foreach (var robot in robots.Elements("robot"))
                foreach (var face in robot.Elements("face"))
                {
                    string? name = face.Attribute("name")?.Value?.Trim();
                    if (!string.IsNullOrEmpty(name)) names.Add(name);
                }
        return names;
    }
}
