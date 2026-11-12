using System.Xml.Linq;
using APS.ProcessPanel.Contracts;

namespace APS.SystemControl.Core.Classes;

public static class SourcesMapLoader
{
    /// <summary>
    /// Reads <c>Sources.xml</c> into a flange-number → <see cref="FlangeSourceInfo"/> map.
    /// Entries are kept when a valid integer flange attribute is present and any of
    /// material, powerSupplyId or a magnetron id is populated. Malformed XML logs to
    /// stderr and returns whatever was accumulated; this runs on a background
    /// dispatcher task and a parse crash there would take the dispatcher down.
    /// </summary>
    public static IReadOnlyDictionary<int, FlangeSourceInfo> LoadFlangeSourceMap(string configDir)
    {
        var map = new Dictionary<int, FlangeSourceInfo>();
        string path = Path.Combine(configDir, "Sources.xml");
        if (!File.Exists(path)) return map;

        try
        {
            var doc = XDocument.Load(path);
            foreach (var el in doc.Descendants("source"))
            {
                string? flangeStr = el.Attribute("flange")?.Value;
                if (!int.TryParse(flangeStr, out int flange)) continue;

                string material = el.Element("material")?.Value ?? "";
                string? name    = NullIfEmpty(el.Attribute("name")?.Value);
                string? supplyId = NullIfEmpty(el.Element("powerSupplyId")?.Value);
                string? xmlType  = NullIfEmpty(el.Element("powerType")?.Value);
                string? magInstId = NullIfEmpty(el.Element("magnetronInstanceId")?.Value);
                string? magId     = NullIfEmpty(el.Element("magnetronId")?.Value);

                bool hasAnything = !string.IsNullOrWhiteSpace(material) || supplyId != null
                                   || magInstId != null || magId != null;
                if (!hasAnything) continue;

                string? powerType = xmlType ?? PowerSupplyClassifier.Classify(supplyId);
                map[flange] = new FlangeSourceInfo(material, supplyId, powerType, magInstId, magId, name);
            }
        }
        catch (Exception ex)
        {
            System.Console.Error.WriteLine(
                $"SourcesMapLoader: failed to parse '{path}': {ex.Message}");
        }
        return map;
    }

    /// <summary>
    /// Reads the <c>&lt;powersources&gt;</c> list from <c>Sources.xml</c> — the set of
    /// magnetron-capable supplies the operator can assign to a flange. Bias/other
    /// supplies are deliberately absent. Trimmed, empties dropped;
    /// missing file or element returns an empty list.
    /// </summary>
    public static IReadOnlyList<string> LoadPowerSources(string configDir)
    {
        string path = Path.Combine(configDir, "Sources.xml");
        if (!File.Exists(path)) return Array.Empty<string>();

        try
        {
            var doc = XDocument.Load(path);
            // Scoped to the <powersources> root child on purpose (not Descendants) so a
            // stray nested <powersource> elsewhere in the document can't leak into the list.
            return (doc.Root?.Element("powersources")?.Elements("powersource")
                    ?? Enumerable.Empty<XElement>())
                .Select(e => e.Value.Trim())
                .Where(s => s.Length > 0)
                .ToList();
        }
        catch (Exception ex)
        {
            System.Console.Error.WriteLine($"SourcesMapLoader: failed to parse '{path}': {ex.Message}");
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Per-chamber view of <c>Sources.xml</c> for the cluster panel: chamber id →
    /// its sputter sources (target material + shutter/supply ids), ordered by
    /// flange number so display order is stable. Entries without a
    /// <c>chamber</c> attribute, or with no material/shutter/supply at all, are
    /// skipped. Best-effort like <see cref="LoadFlangeSourceMap"/>: missing file
    /// or malformed XML returns an empty map.
    /// </summary>
    public static IReadOnlyDictionary<string, ClusterSourceInfo[]> LoadChamberSources(string configDir)
    {
        var byChamber = new Dictionary<string, List<(int Flange, ClusterSourceInfo Src)>>(
            StringComparer.OrdinalIgnoreCase);
        string path = Path.Combine(configDir, "Sources.xml");
        if (!File.Exists(path)) return ToArrays(byChamber);

        try
        {
            var doc = XDocument.Load(path);
            foreach (var el in doc.Descendants("source"))
            {
                string? chamber = NullIfEmpty(el.Attribute("chamber")?.Value);
                if (chamber == null) continue;

                string material   = el.Element("material")?.Value.Trim() ?? "";
                string? name      = NullIfEmpty(el.Attribute("name")?.Value);
                string? shutterId = NullIfEmpty(el.Attribute("shutterId")?.Value);
                string? supplyId  = NullIfEmpty(el.Element("powerSupplyId")?.Value);
                if (material.Length == 0 && shutterId == null && supplyId == null) continue;

                int flange = int.TryParse(el.Attribute("flange")?.Value, out int f)
                    ? f : int.MaxValue;   // flange only orders the list; missing sorts last
                if (!byChamber.TryGetValue(chamber, out var list))
                    byChamber[chamber] = list = new List<(int, ClusterSourceInfo)>();
                list.Add((flange, new ClusterSourceInfo(material, shutterId, supplyId, name)));
            }
        }
        catch (Exception ex)
        {
            System.Console.Error.WriteLine(
                $"SourcesMapLoader: failed to parse '{path}': {ex.Message}");
        }
        return ToArrays(byChamber);

        static IReadOnlyDictionary<string, ClusterSourceInfo[]> ToArrays(
            Dictionary<string, List<(int Flange, ClusterSourceInfo Src)>> src)
        {
            var result = new Dictionary<string, ClusterSourceInfo[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var (chamber, list) in src)
                result[chamber] = list.OrderBy(t => t.Flange).Select(t => t.Src).ToArray();
            return result;
        }
    }

    private static string? NullIfEmpty(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
