using APS.RecipeModel.Validation;

namespace APS.SystemControl.Core.Classes;

public sealed record SourceNode(int Flange, string Label);
public sealed record ChamberGroup(string? Chamber, IReadOnlyList<SourceNode> Sources);

/// <summary>
/// Turns SourceManager.Sources into the Deposition Sources tree model. Single-chamber
/// systems (no source carries a Chamber) collapse to one flat group (Chamber == null);
/// cluster systems group by chamber in first-seen (ascending-flange) order.
/// </summary>
public static class SourceGrouping
{
    /// <summary>
    /// "FL n — &lt;handle&gt;", where the handle is the effective source name (the
    /// <c>name</c> when set, else the material) — the same handle recipes address.
    /// "(empty)" when the flange carries neither.
    /// </summary>
    public static string NodeLabel(int flange, string? name, string? material)
    {
        string label = SourceNames.Effective(name, material);
        return $"FL {flange} — {(label.Length == 0 ? "(empty)" : label)}";
    }

    public static IReadOnlyList<ChamberGroup> GroupByChamber(IReadOnlyDictionary<int, Source> sources)
    {
        var ordered = sources.Values.OrderBy(s => s.FlangeNumber).ToList();
        SourceNode Node(Source s) => new(s.FlangeNumber, NodeLabel(s.FlangeNumber, s.Name, s.Material));

        bool anyChamber = ordered.Any(s => !string.IsNullOrWhiteSpace(s.Chamber));
        if (!anyChamber)
            return new[] { new ChamberGroup(null, ordered.Select(Node).ToList()) };

        var groups = new List<ChamberGroup>();
        var index = new Dictionary<string, List<SourceNode>>(StringComparer.Ordinal);
        foreach (var s in ordered)
        {
            string chamber = string.IsNullOrWhiteSpace(s.Chamber) ? "(unassigned)" : s.Chamber!;
            if (!index.TryGetValue(chamber, out var list))
            {
                index[chamber] = list = new List<SourceNode>();
                groups.Add(new ChamberGroup(chamber, list));   // record keeps the live list reference
            }
            list.Add(Node(s));
        }
        return groups;
    }
}
