using System.Globalization;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;

namespace APS.SystemControl.GUIControls.Process;

public sealed record ValveSpec(string Id, string Description);

public sealed record ValveGroupSpec(string Header, IReadOnlyList<ValveSpec> Valves);

public sealed record DriveSpec(string Id, string Label);

public sealed record ProcessPanelSpec(
    IReadOnlyList<ReadingSpec> Readings,
    IReadOnlyList<ValveGroupSpec> ValveGroups,
    IReadOnlyList<DriveSpec> Drives,
    IReadOnlyList<string> Errors)
{
    public static ProcessPanelSpec Empty { get; } = new([], [], [], []);

    public static ProcessPanelSpec Current { get; set; } = Empty;

    private static readonly Dictionary<string, Color> Accents = typeof(ReadingPalette)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(Color))
        .ToDictionary(f => f.Name, f => (Color)f.GetValue(null)!, StringComparer.OrdinalIgnoreCase);

    public static ProcessPanelSpec Load(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return Empty;

        XDocument doc;
        try { doc = XDocument.Load(path); }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            return Empty with { Errors = [$"ProcessPanel.xml: {ex.Message}"] };
        }

        var errors = new List<string>();
        var root = doc.Root!;
        if (root.Name.LocalName != "processPanel")
            return Empty with { Errors = [$"ProcessPanel.xml: unexpected root element '{root.Name.LocalName}'"] };

        return new ProcessPanelSpec(
            ParseReadings(root, errors),
            ParseValveGroups(root, errors),
            ParseIdLabel(root, "drives", "drive", (id, label) => new DriveSpec(id, label), errors),
            errors);
    }

    private static string? Attr(XElement e, string name)
    {
        var v = (string?)e.Attribute(name);
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    private static List<ReadingSpec> ParseReadings(XElement root, List<string> errors)
    {
        var list = new List<ReadingSpec>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in root.Elements("readings").Elements("reading"))
        {
            var id = Attr(e, "id");
            if (id is null) { errors.Add("reading without id skipped"); continue; }

            if (!Enum.TryParse(Attr(e, "group"), true, out ReadingGroup group))
            { errors.Add($"reading '{id}': unknown group '{Attr(e, "group")}'"); continue; }

            var scale = ReadingScale.Linear;
            if (Attr(e, "scale") is { } scaleText && !Enum.TryParse(scaleText, true, out scale))
            { errors.Add($"reading '{id}': unknown scale '{scaleText}'"); continue; }

            Color? accent = null;
            if (Attr(e, "accent") is { } accentText)
            {
                if (!Accents.TryGetValue(accentText, out var c))
                { errors.Add($"reading '{id}': unknown accent '{accentText}'"); continue; }
                accent = c;
            }
            var format = Attr(e, "format") ?? "F1";
            try { _ = 0d.ToString(format, CultureInfo.InvariantCulture); }
            catch (FormatException)
            { errors.Add($"reading '{id}': invalid format '{format}'"); continue; }

            if (!seen.Add(id)) { errors.Add($"reading '{id}': duplicate id skipped"); continue; }

            var sparkline = !string.Equals(Attr(e, "sparkline"), "false", StringComparison.OrdinalIgnoreCase);

            list.Add(new ReadingSpec(
                id, group,
                Attr(e, "label") ?? id,
                format,
                Attr(e, "unit") ?? "",
                sparkline, scale, accent,
                Attr(e, "section"),
                Attr(e, "device")));
        }
        return list;
    }

    private static List<ValveGroupSpec> ParseValveGroups(XElement root, List<string> errors)
    {
        var groups = new List<ValveGroupSpec>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var g in root.Elements("valveGroups").Elements("group"))
        {
            var valves = new List<ValveSpec>();
            foreach (var v in g.Elements("valve"))
            {
                var id = Attr(v, "id");
                if (id is null) { errors.Add("valve without id skipped"); continue; }
                if (!seen.Add(id)) { errors.Add($"valve '{id}': duplicate id skipped"); continue; }
                valves.Add(new ValveSpec(id, Attr(v, "descr") ?? ""));
            }
            groups.Add(new ValveGroupSpec(Attr(g, "header") ?? "", valves));
        }
        return groups;
    }

    private static List<T> ParseIdLabel<T>(XElement root, string container, string item,
        Func<string, string, T> make, List<string> errors)
    {
        var list = new List<T>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in root.Elements(container).Elements(item))
        {
            var id = Attr(e, "id");
            if (id is null) { errors.Add($"{item} without id skipped"); continue; }
            if (!seen.Add(id)) { errors.Add($"{item} '{id}': duplicate id skipped"); continue; }
            list.Add(make(id, Attr(e, "label") ?? id));
        }
        return list;
    }
}
