namespace APS.ProcessPanel.Contracts;

/// <summary>
/// One sputter source in a cluster chamber, from <c>Sources.xml</c>: the target
/// material plus the ids linking it to its PLC shutter valve and power supply.
/// Static per config load — materials don't change while the app runs.
/// <see cref="ShutterId"/>/<see cref="PowerSupplyId"/> are null when the operator
/// has not assigned them; consumers skip the corresponding indicator.
/// </summary>
public sealed record ClusterSourceInfo(
    string Material,
    string? ShutterId,
    string? PowerSupplyId,
    string? Name = null)
{
    /// <summary>Operator-facing source label: the <c>name</c> attribute when set,
    /// else the material — the addressable handle recipes use. Mirrors
    /// RecipeModel's <c>SourceNames.Effective</c> (kept inline so this contracts
    /// assembly stays free of a RecipeModel dependency).</summary>
    public string Effective =>
        string.IsNullOrWhiteSpace(Name) ? Material : Name.Trim();
}
