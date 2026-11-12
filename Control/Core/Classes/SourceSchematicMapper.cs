using APS.RecipeModel.Validation;
using APS.SystemControl.Controls.General;

namespace APS.SystemControl.Core.Classes;

/// <summary>
/// Pure mapping from SourceManager state (+ a per-flange life lookup) to the schematic's
/// view-model. The host supplies the life lookup from its own (cached) production query.
/// </summary>
public static class SourceSchematicMapper
{
    public static IReadOnlyList<ChamberSourceSchematic.FlangeView> Build(
        IReadOnlyDictionary<int, Source> sources,
        IReadOnlyList<MagnetronInstance> instances,
        Func<int, (double Life, double Warn)?> lifeLookup)
    {
        // A PSU on more than one flange can't be matched to a single magnetron (the main
        // scheme's glow map drops it), so the schematic flags it.
        var sharedPsus = sources.Values
            .Where(s => !string.IsNullOrWhiteSpace(s.PowerSupplyId))
            .GroupBy(s => s.PowerSupplyId!, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);

        var list = new List<ChamberSourceSchematic.FlangeView>();
        foreach (var (flange, s) in sources.OrderBy(kv => kv.Key))
        {
            string? psu = string.IsNullOrWhiteSpace(s.PowerSupplyId) ? null : s.PowerSupplyId;
            bool occupied = !string.IsNullOrWhiteSpace(s.Material)
                            || !string.IsNullOrWhiteSpace(s.MagnetronInstanceId);
            var lk = lifeLookup(flange);
            string? magLabel = string.IsNullOrEmpty(s.MagnetronInstanceId)
                ? null
                : instances.FirstOrDefault(m => m.Id == s.MagnetronInstanceId)?.Label;

            string label = SourceNames.Effective(s.Name, s.Material);
            list.Add(new ChamberSourceSchematic.FlangeView(
                flange, s.Chamber, label, magLabel, occupied,
                lk?.Life, lk?.Warn ?? 0.8, s.AngleDegrees,
                psu, psu != null && sharedPsus.Contains(psu)));
        }
        return list;
    }
}
