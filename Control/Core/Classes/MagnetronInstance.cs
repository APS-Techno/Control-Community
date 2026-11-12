namespace APS.SystemControl.Core.Classes;

/// <summary>
/// A serialized physical magnetron. Authoritative in Sources.xml (managed in the Sources form);
/// mirrored to the production DB. <see cref="ModelId"/> references a <see cref="MagnetronDef"/> and
/// is what gets written to the flange's model id for the emulator. <see cref="PhysicsJson"/> is
/// opaque (reserved for the digital twin).
/// </summary>
public record MagnetronInstance(
    string Id, string Label, string? Serial, string ModelId,
    string? Format, string? PhysicsJson, string? Comment, string State,
    string? ElnLink = null);
