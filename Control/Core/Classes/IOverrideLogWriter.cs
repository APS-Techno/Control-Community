namespace APS.SystemControl.Core.Classes;

/// <summary>
/// <see cref="Write(string)"/> carries human-readable lines for the
/// operator log pane; <see cref="WriteAudit(AuditEvent)"/> carries the
/// structured, hash-chained record for the tamper-evident M10 audit trail.
/// The default <see cref="WriteAudit"/> is a no-op.
/// </summary>
internal interface IOverrideLogWriter
{
    void Write(string line);

    void WriteAudit(AuditEvent evt) { }
}
