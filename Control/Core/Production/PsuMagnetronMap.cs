using APS.ProcessPanel.Contracts;

namespace APS.SystemControl.Core.Production;

/// <summary>
/// Builds a PSU-id → magnetron-instance-id lookup from the per-flange source map. A PSU wired to
/// more than one magnetron (shared/switched supply, e.g. one supply on two flanges) is AMBIGUOUS at
/// runtime — we can't tell which cathode it's currently driving — so it is excluded rather than
/// risk attributing erosion to the wrong target. The connection is fixed during a deposition, so
/// the unambiguous case is stable across a run.
/// </summary>
public static class PsuMagnetronMap
{
    public static IReadOnlyDictionary<string, string> Build(
        IReadOnlyDictionary<int, FlangeSourceInfo> flangeMap)
    {
        var byPsu = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var info in flangeMap.Values)
        {
            if (string.IsNullOrEmpty(info.PowerSupplyId) || string.IsNullOrEmpty(info.MagnetronInstanceId))
                continue;
            string psu = info.PowerSupplyId;
            if (byPsu.TryGetValue(psu, out var existing))
            {
                if (!string.Equals(existing, info.MagnetronInstanceId, StringComparison.Ordinal))
                    ambiguous.Add(psu);
            }
            else
            {
                byPsu[psu] = info.MagnetronInstanceId;
            }
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (psu, mag) in byPsu)
            if (mag is not null && !ambiguous.Contains(psu))
                result[psu] = mag;
        return result;
    }
}
