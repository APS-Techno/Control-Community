namespace APS.SystemControl.Core.Evidence;

public enum DriftGrade { Info, Note, Warning }

public sealed record DriftItem(DriftGrade Grade, string Message);

public sealed record DriftReport(IReadOnlyList<DriftItem> Items)
{
    public bool HasWarnings => Items.Any(i => i.Grade == DriftGrade.Warning);
}

/// <summary>
/// Advisory comparison of a snapshot's captured conditions against the current setup,
/// used before re-running. Never blocks — produces graded items the dialog shows. See spec §Re-run.
/// </summary>
public static class DriftCheck
{
    public static DriftReport Compare(DepositionConditions captured, DepositionConditions current)
    {
        var items = new List<DriftItem>();
        var currentByFlange = (current.Sources ?? []).Where(s => s.Flange.HasValue).ToDictionary(s => s.Flange!.Value);

        foreach (var cap in (captured.Sources ?? []).Where(s => s.Flange.HasValue))
        {
            int flange = cap.Flange!.Value;
            if (!currentByFlange.TryGetValue(flange, out var now))
            {
                items.Add(new DriftItem(DriftGrade.Warning, $"flange {flange}: was '{cap.Material}', now empty/absent"));
                continue;
            }
            if (!string.Equals(cap.Material, now.Material, StringComparison.OrdinalIgnoreCase))
                items.Add(new DriftItem(DriftGrade.Warning, $"flange {flange}: material was '{cap.Material}', now '{now.Material}'"));
            else if (!string.Equals(cap.MagnetronModel, now.MagnetronModel, StringComparison.OrdinalIgnoreCase))
                items.Add(new DriftItem(DriftGrade.Warning, $"flange {flange}: magnetron model was '{cap.MagnetronModel}', now '{now.MagnetronModel}'"));
            else
            {
                if (!string.Equals(cap.MagnetronInstance, now.MagnetronInstance, StringComparison.OrdinalIgnoreCase))
                    items.Add(new DriftItem(DriftGrade.Info, $"flange {flange}: different magnetron instance (same model/material)"));
                if ((now.ErosionDepthMm ?? 0) > (cap.ErosionDepthMm ?? 0) + 1e-9)
                    items.Add(new DriftItem(DriftGrade.Info, $"flange {flange}: erosion advanced {cap.ErosionDepthMm ?? 0:0.###}→{now.ErosionDepthMm ?? 0:0.###} mm"));
                if ((cap.TargetToSubstrateDistanceMm.HasValue || now.TargetToSubstrateDistanceMm.HasValue)
                    && Math.Abs((cap.TargetToSubstrateDistanceMm ?? 0) - (now.TargetToSubstrateDistanceMm ?? 0)) > 1e-6)
                    items.Add(new DriftItem(DriftGrade.Info,
                        $"flange {flange}: target–substrate distance {cap.TargetToSubstrateDistanceMm ?? 0:0.###}→{now.TargetToSubstrateDistanceMm ?? 0:0.###} mm"));
            }
        }

        foreach (var kvp in (captured.Software ?? new Dictionary<string, string>()))
        {
            string? nowVal = null;
            current.Software?.TryGetValue(kvp.Key, out nowVal);
            if (!string.Equals(kvp.Value, nowVal, StringComparison.Ordinal))
                items.Add(new DriftItem(DriftGrade.Note, $"version '{kvp.Key}' was '{kvp.Value}', now '{nowVal ?? "(absent)"}'"));
        }

        return new DriftReport(items);
    }
}
