using APS.SystemControl.Core.Classes;

namespace APS.SystemControl.Accounts;

/// <summary>
/// Routes account auth events into Control's single hash-chained audit writer. The writer
/// is built by Core after login has already happened, so events raised before
/// <see cref="Attach"/> are buffered and flushed in order once the writer is available.
/// This keeps ONE tamper-evident chain (login + override + run events) without a second
/// writer fighting over the same files.
/// </summary>
internal static class AccountsAuditBus
{
    private static readonly object Gate = new();
    private static IOverrideLogWriter? _target;
    private static readonly Queue<AuditEvent> Pending = new();

    public static void Write(AuditEvent evt)
    {
        lock (Gate)
        {
            if (_target is not null) _target.WriteAudit(evt);
            else Pending.Enqueue(evt);
        }
    }

    /// <summary>Connect Core's audit writer and flush anything buffered before now. Idempotent:
    /// a second attach (e.g. a Core re-init) is ignored so events keep flowing to one writer.</summary>
    public static void Attach(IOverrideLogWriter target)
    {
        lock (Gate)
        {
            if (_target is not null) return;
            _target = target;
            while (Pending.Count > 0)
                target.WriteAudit(Pending.Dequeue());
        }
    }
}
