using APS.ProcessPanel.Contracts;

namespace APS.SystemControl.Core.Classes;

/// <summary>
/// Decides which magnetron-source PSU panels should be visible. A supply listed in
/// <c>Sources.xml &lt;powersources&gt;</c> is visible iff it is wired to at least one
/// flange that is an <em>active source</em> (has a magnetron — model or instance — or a
/// material). Supplies NOT in <paramref name="magnetronSourcePsus"/> (bias/other) are
/// absent from the result, so the caller leaves them visible. Unlike
/// <see cref="APS.SystemControl.Core.Production.PsuMagnetronMap"/>, a supply shared
/// across cathodes stays visible — it is clearly in use even if ambiguous.
/// </summary>
public static class PsuPanelVisibilityPolicy
{
    public static IReadOnlyDictionary<string, bool> Compute(
        IReadOnlyDictionary<int, FlangeSourceInfo> flangeMap,
        IReadOnlyCollection<string> magnetronSourcePsus)
    {
        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var psu in magnetronSourcePsus)
        {
            if (string.IsNullOrWhiteSpace(psu) || result.ContainsKey(psu)) continue;
            bool visible = false;
            foreach (var f in flangeMap.Values)
            {
                if (string.Equals(f.PowerSupplyId, psu, StringComparison.OrdinalIgnoreCase)
                    && IsActiveSource(f))
                {
                    visible = true;
                    break;
                }
            }
            result[psu] = visible;
        }
        return result;
    }

    private static bool IsActiveSource(FlangeSourceInfo f) =>
        !string.IsNullOrWhiteSpace(f.Material)
        || !string.IsNullOrWhiteSpace(f.MagnetronId)
        || !string.IsNullOrWhiteSpace(f.MagnetronInstanceId);
}
