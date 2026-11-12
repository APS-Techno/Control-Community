namespace APS.SystemControl.Core.Evidence;

/// <summary>A magnetron instance placed on a flange (from Sources.xml / SourceManager).</summary>
public readonly record struct MagnetronPlacement(
    string InstanceId, string ModelId, string? Format, int? Flange, string? ElnLink = null,
    double? ZeroDistanceMm = null, double? LiftMm = null);

/// <summary>The target currently mounted on a magnetron (from the Production DB).
/// Lot and Serial are the inventory identity of the individual physical target —
/// optional because a target may be seeded without them.</summary>
public readonly record struct MountedTarget(
    string Material, string? Format, double CurrentErosionDepthMm,
    double ThicknessMm, double BackingThicknessMm,
    string? Lot = null, string? Serial = null);

/// <summary>
/// Joins magnetron placements to their mounted targets, producing the snapshot's
/// <see cref="DepositionSource"/> rows. Pure: takes already-fetched data so it is unit-testable
/// without a database. Mirrors the live join in frmTargets.ReloadSources.
/// </summary>
public static class MountedSourcesReader
{
    public static List<DepositionSource> Build(
        IEnumerable<MagnetronPlacement> magnetrons,
        IReadOnlyDictionary<Guid, MountedTarget> targetByMagnetronInstance)
    {
        var list = new List<DepositionSource>();
        foreach (var m in magnetrons)
        {
            MountedTarget? t = Guid.TryParse(m.InstanceId, out var g)
                && targetByMagnetronInstance.TryGetValue(g, out var mt) ? mt : null;

            double? distance = APS.SystemControl.Core.Production.TargetSubstrateDistance.Effective(
                m.ZeroDistanceMm, m.LiftMm, t?.ThicknessMm, t?.BackingThicknessMm ?? 0, t?.CurrentErosionDepthMm ?? 0);

            list.Add(new DepositionSource
            {
                Flange = m.Flange,
                Material = t?.Material ?? "unknown",
                Format = t?.Format ?? m.Format,
                MagnetronModel = m.ModelId,
                MagnetronInstance = m.InstanceId,
                MagnetronElnLink = m.ElnLink,
                ErosionDepthMm = t?.CurrentErosionDepthMm,
                ZeroDistanceMm = m.ZeroDistanceMm,
                LiftMm = m.LiftMm,
                TargetThicknessMm = t?.ThicknessMm,
                TargetBackingMm = t?.BackingThicknessMm,
                TargetLot = t?.Lot,
                TargetSerial = t?.Serial,
                TargetToSubstrateDistanceMm = distance,
            });
        }
        return list;
    }
}
