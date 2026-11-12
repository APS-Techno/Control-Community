namespace APS.SystemControl.Core.Production;

/// <summary>
/// Effective target-to-substrate (working) distance, mm. Composed from the per-flange
/// zero distance plus the lift (operator-set), minus the mounted target's proud stack
/// (material + Cu backing) plus the erosion that has receded the material surface.
/// The target hangs below the magnetron over the substrate, so lifting the magnetron
/// moves the target away and *increases* the gap, while the proud stack reduces it.
/// See the 2026-07-07 design doc.
/// </summary>
public static class TargetSubstrateDistance
{
    /// <summary>Returns the effective distance, or null when any operator/target input is missing.</summary>
    public static double? Effective(
        double? zeroDistanceMm, double? liftMm,
        double? materialThicknessMm, double backingThicknessMm, double erosionDepthMm)
    {
        if (zeroDistanceMm is not double zero) return null;
        if (liftMm is not double lift) return null;
        if (materialThicknessMm is not double material) return null;

        double proudStack = material + backingThicknessMm - erosionDepthMm;
        return zero + lift - proudStack;
    }
}
