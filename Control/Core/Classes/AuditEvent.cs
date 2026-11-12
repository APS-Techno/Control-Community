using System.Text.Json.Serialization;

namespace APS.SystemControl.Core.Classes;

/// <summary>
/// Structured event record for the tamper-evident safety-override audit log
/// (M10). Serialized one-record-per-line as JSON into a daily file under
/// the AuditLog directory, with each line hash-chained to the previous.
///
/// Field order is declared explicitly via <see cref="JsonPropertyOrderAttribute"/>
/// so serialization is deterministic across runs and platforms — the
/// verifier must reproduce byte-for-byte the same JSON to confirm the
/// chain. Do not add / reorder / rename fields without bumping a schema
/// version; existing audit files must remain verifiable forever.
/// </summary>
public sealed record AuditEvent
{
    /// <summary>Monotonic sequence number within the file (1-based).</summary>
    [JsonPropertyOrder(0)]
    public long Seq { get; init; }

    /// <summary>UTC timestamp in ISO-8601 format ("yyyy-MM-ddTHH:mm:ss.fffZ").</summary>
    [JsonPropertyOrder(1)]
    public string Ts { get; init; } = "";

    [JsonPropertyOrder(2)]
    public string User { get; init; } = "";

    [JsonPropertyOrder(3)]
    public string Event { get; init; } = "";

    /// <summary>Override kind: <c>alarm</c> or <c>interlock</c>. Null for bulk / error events.</summary>
    [JsonPropertyOrder(4)]
    public string? Kind { get; init; }

    /// <summary>Override rule ID. Null for bulk / error events.</summary>
    [JsonPropertyOrder(5)]
    public string? Id { get; init; }

    /// <summary>
    /// Resulting state of the rule: <c>disabled</c> (rule bypassed) or <c>enabled</c>
    /// (rule active). Null for events that don't toggle state.
    /// </summary>
    [JsonPropertyOrder(6)]
    public string? State { get; init; }

    /// <summary>Whether the operator asked to persist this override across restarts. Null when not applicable.</summary>
    [JsonPropertyOrder(7)]
    public bool? Persist { get; init; }

    /// <summary>Operator-supplied reason / comment. Empty when not applicable.</summary>
    [JsonPropertyOrder(8)]
    public string Reason { get; init; } = "";

    /// <summary>
    /// Freeform message for suppressed / orphan / error events. Null for state
    /// changes where the remaining fields carry the information.
    /// </summary>
    [JsonPropertyOrder(9)]
    public string? Message { get; init; }

    /// <summary>SHA-256 (lower-case hex) of the previous record's hash. 64 zeros for the first record in the chain.</summary>
    [JsonPropertyOrder(10)]
    public string Prev { get; init; } = new string('0', 64);

    /// <summary>SHA-256 (lower-case hex) of the canonical JSON of this record with <see cref="Hash"/> set to empty.</summary>
    [JsonPropertyOrder(11)]
    public string Hash { get; init; } = "";
}
