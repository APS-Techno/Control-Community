namespace APS.SystemControl.Core.Evidence;

public sealed class DepositionSnapshot
{
    /// <summary>Schema 2 adds <see cref="DepositionOutcome.RunRecord"/>. Additive and OUTSIDE
    /// the fingerprint (<see cref="Outcome"/> is appended at run end), so a schema-1 snapshot
    /// still round-trips and no fingerprint moves — the same discipline
    /// <c>RunRecord.CurrentSchema</c> follows for its own additive 2 to 3 bump.</summary>
    public int Schema { get; set; } = 2;
    public string Kind { get; set; } = "deposition-snapshot";
    /// <summary>Source fingerprint when this run was started from a prior snapshot; else null.</summary>
    public string? BasedOn { get; set; }
    public DepositionConditions Conditions { get; set; } = new();
    /// <summary>Appended at run end; OUTSIDE the fingerprint and signature. Null until finalized.</summary>
    public DepositionOutcome? Outcome { get; set; }
}

public sealed class DepositionConditions
{
    public string Fingerprint { get; set; } = "";
    public string? Specimen { get; set; }
    public DepositionSignature Signature { get; set; } = new();
    public Dictionary<string, string> Software { get; set; } = new();
    public string[] PlcSerials { get; set; } = [];
    public List<DepositionSource> Sources { get; set; } = new();
    public DepositionRecipe Recipe { get; set; } = new();
}

/// <summary>L1 signature: a stamp of the authenticated operator + intent. No crypto, no re-auth.</summary>
public sealed class DepositionSignature
{
    public string? UserId { get; set; }
    public string? Name { get; set; }
    public string? Role { get; set; }
    public string SignedAtUtc { get; set; } = "";
    public string Intent { get; set; } = "authorized"; // or "unauthenticated"
}

public sealed class DepositionSource
{
    public int? Flange { get; set; }
    public string Material { get; set; } = "unknown";
    public string? Format { get; set; }
    public string? MagnetronModel { get; set; }
    public string? MagnetronInstance { get; set; }
    /// <summary>ELN (lab-notebook) page for this magnetron; provenance only, NOT fingerprinted.</summary>
    public string? MagnetronElnLink { get; set; }
    public double? ErosionDepthMm { get; set; }
    public DepositionPsu? PowerSupply { get; set; }
    /// <summary>Per-flange zero distance (bare magnetron face to substrate at lift=0), mm. Operator-set. Provenance only.</summary>
    public double? ZeroDistanceMm { get; set; }
    /// <summary>Lift gauge reading at run start, mm (lifting the magnetron raises the target away
    /// from the substrate, so it grows the gap). Operator-set. Provenance only.</summary>
    public double? LiftMm { get; set; }
    /// <summary>Mounted target's sputterable material thickness, mm.</summary>
    public double? TargetThicknessMm { get; set; }
    /// <summary>Mounted target's Cu bond-plate thickness, mm (0 = monolithic).</summary>
    public double? TargetBackingMm { get; set; }
    /// <summary>Mounted target's manufacturing lot. Identifies which physical target produced
    /// this film. Provenance only, NOT fingerprinted — a fresh target of the same material and
    /// format is the same reproducible condition, exactly as for the magnetron instance.</summary>
    public string? TargetLot { get; set; }
    /// <summary>Mounted target's serial number — the individual physical target. Provenance
    /// only, NOT fingerprinted (see <see cref="TargetLot"/>).</summary>
    public string? TargetSerial { get; set; }
    /// <summary>Effective target-to-substrate distance, mm = zero + lift - (thickness + backing - erosion). Provenance only, NOT fingerprinted.</summary>
    public double? TargetToSubstrateDistanceMm { get; set; }
}

/// <summary>A power supply connected at run start. Id+Model are the reproducible identity
/// (fingerprinted); Responding is the transient link state and ElnLink is provenance
/// metadata — both are NOT fingerprinted.</summary>
public sealed class DepositionPsu
{
    public string Id { get; set; } = "";
    public string Model { get; set; } = ""; // driver type name
    public string? Description { get; set; }
    public bool Responding { get; set; }
    /// <summary>ELN (lab-notebook) page for this power supply; provenance only.</summary>
    public string? ElnLink { get; set; }
}

public sealed class DepositionRecipe
{
    public string Name { get; set; } = "";
    public string Format { get; set; } = "rcpx"; // rcpx | recipe
    public string Content { get; set; } = "";
}

public sealed class DepositionOutcome
{
    public string Result { get; set; } = ""; // completed | aborted | failed
    /// <summary>Bare file name of this run's TSV data log in the same folder;
    /// null when recipe logging was off or the TSV failed to open.</summary>
    public string? DataFile { get; set; }
    /// <summary>Schema 2. Bare file name of this run's <c>.run.json</c> record in the same
    /// folder — the recipe's outcome, every fault pause, and everything the operator hand-drove
    /// during a hold. Named EXPLICITLY rather than left to be reconstructed from
    /// <see cref="DataFile"/>'s stem, so the snapshot is self-describing, and because a run with
    /// recipe logging off has a run record but no <see cref="DataFile"/> at all. Null only when no record
    /// is written for this run.</summary>
    public string? RunRecord { get; set; }
    public DepositionWindow Window { get; set; } = new();
    public Dictionary<string, double> Means { get; set; } = new();
    public BasePressure? BasePressure { get; set; }
}

public sealed class DepositionWindow
{
    public string StartedUtc { get; set; } = "";
    public string EndedUtc { get; set; } = "";
}

public sealed class BasePressure
{
    public string Channel { get; set; } = "";
    public double Min { get; set; }
    public string? Unit { get; set; }
}
