namespace APS.ProcessPanel.Contracts;

/// <summary>
/// Snapshot of a single flange's source assignment for the Process panel.
/// Pushed once at startup and again after any Sources.xml reload. All fields
/// except <see cref="Material"/> may be null/empty when the operator has not
/// assigned a magnetron or power supply to the flange.
/// </summary>
public sealed record FlangeSourceInfo(
    string Material,
    string? PowerSupplyId,
    string? PowerType,
    string? MagnetronInstanceId = null,
    string? MagnetronId = null,
    string? Name = null)
{
    /// <summary>Operator-facing source label: the <c>name</c> attribute when set,
    /// else the material — the addressable handle recipes use. Mirrors
    /// RecipeModel's <c>SourceNames.Effective</c> (kept inline so this contracts
    /// assembly stays free of a RecipeModel dependency).</summary>
    public string Effective =>
        string.IsNullOrWhiteSpace(Name) ? Material : Name.Trim();
}
