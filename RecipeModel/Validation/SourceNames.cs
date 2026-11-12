namespace APS.RecipeModel.Validation;

/// <summary>
/// Pure helpers for a source's addressable handle. The effective name is the
/// operator's <c>name</c> attribute when present, else the material — recipes
/// (deposit/holder/shutter) resolve against these. Effective names must be
/// globally unique per config; <see cref="Duplicates"/> finds violations.
/// <see cref="Resolvable"/> mirrors the workers' ambiguity rule so callers
/// (dropdowns, validators) never offer a handle the runtime would reject.
/// </summary>
public static class SourceNames
{
    public static string Effective(string? name, string? material)
    {
        var n = name?.Trim();
        if (!string.IsNullOrEmpty(n)) return n;
        return material?.Trim() ?? "";
    }

    public static IReadOnlyList<string> Duplicates(IEnumerable<string> effectiveNames)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        foreach (var raw in effectiveNames)
        {
            var e = raw?.Trim() ?? "";
            if (e.Length == 0) continue;
            if (!counts.TryGetValue(e, out int c)) { counts[e] = 1; order.Add(e); }
            else counts[e] = c + 1;
        }
        return order.Where(e => counts[e] > 1).ToList();
    }

    /// <summary>
    /// Distinct effective names in first-seen (document) order, EXCLUDING any
    /// handle a recipe worker would treat as ambiguous — i.e. effective names
    /// that collide across sources, or trimmed non-empty materials shared by
    /// more than one source (mirrors the recipe worker's
    /// <c>LoadSourceMap</c> ambiguity rule so gate-approved configs can never
    /// offer a handle that later throws at <c>ResolvePosition</c>). All
    /// comparisons are OrdinalIgnoreCase.
    /// </summary>
    public static IReadOnlyList<string> Resolvable(IEnumerable<(string? name, string? material)> sources)
    {
        var list = sources as IReadOnlyCollection<(string? name, string? material)> ?? sources.ToList();

        var effectiveNames = new List<string>();
        var nameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var materialCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, material) in list)
        {
            string eff = Effective(name, material);
            if (eff.Length > 0)
            {
                effectiveNames.Add(eff);
                nameCounts[eff] = nameCounts.TryGetValue(eff, out int nc) ? nc + 1 : 1;
            }

            string m = material?.Trim() ?? "";
            if (m.Length > 0)
                materialCounts[m] = materialCounts.TryGetValue(m, out int mc) ? mc + 1 : 1;
        }

        var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in nameCounts) if (kv.Value > 1) ambiguous.Add(kv.Key);
        foreach (var kv in materialCounts) if (kv.Value > 1) ambiguous.Add(kv.Key);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var eff in effectiveNames)
        {
            if (ambiguous.Contains(eff)) continue;
            if (seen.Add(eff)) result.Add(eff);
        }
        return result;
    }
}
