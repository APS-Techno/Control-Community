using EmulatorCore.Interfaces;
using EmulatorCore.Models;

namespace APS.SystemEmulator.Engine;

/// <summary>
/// Resolves which vacuum chamber a transfer robot physically lives in, from the
/// slit-valve topology: the chamber common to every slit-valve connects= pair of
/// the robot's faces. A robot in a transfer module reaching N chambers through N
/// slit valves shares exactly one chamber across all of them — the module itself.
/// </summary>
internal static class RobotChamberResolver
{
    /// <summary>
    /// Returns the id of the robot's home chamber, or null if it can't be
    /// determined. <paramref name="chamberTypes"/> is consulted only for the
    /// single-Transfer-chamber fall-back when the topology is ambiguous.
    /// </summary>
    public static string? Resolve(
        IReadOnlyCollection<string> robotSlitValveIds,
        IReadOnlyList<SlitConnection> slitConnections,
        IReadOnlyDictionary<string, ChamberType> chamberTypes)
    {
        var pairs = slitConnections
            .Where(s => robotSlitValveIds.Contains(s.ValveId, StringComparer.OrdinalIgnoreCase))
            .Select(s => new[] { s.ChamberA, s.ChamberB })
            .ToList();

        if (pairs.Count > 0)
        {
            var common = new HashSet<string>(pairs[0], StringComparer.OrdinalIgnoreCase);
            foreach (var pair in pairs.Skip(1))
                common.IntersectWith(pair);
            if (common.Count == 1)
                return common.First();
        }

        // Fall-back: a single Transfer-typed chamber.
        var transfers = chamberTypes
            .Where(kv => kv.Value == ChamberType.Transfer)
            .Select(kv => kv.Key)
            .ToList();
        return transfers.Count == 1 ? transfers[0] : null;
    }
}
