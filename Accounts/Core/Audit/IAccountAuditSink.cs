using APS.Accounts.Domain;

namespace APS.Accounts.Audit;

/// <summary>
/// The accounts library raises auth-lifecycle events here; the host (Control) implements
/// this to fold them into its existing hash-chained audit log. Keeping the concrete
/// AuditEvent type on the Control side preserves the one-way Control → Accounts dependency.
/// </summary>
public interface IAccountAuditSink
{
    void LoginSucceeded(AuthenticatedPrincipal principal, bool wasOffline);
    void LoginFailed(string username, AuthFailure failure);
    void LoggedOut(AuthenticatedPrincipal principal, string reason);
    void SwitchedUser(AuthenticatedPrincipal from, AuthenticatedPrincipal to);
}

public sealed class NullAccountAuditSink : IAccountAuditSink
{
    public static readonly NullAccountAuditSink Instance = new();
    public void LoginSucceeded(AuthenticatedPrincipal principal, bool wasOffline) { }
    public void LoginFailed(string username, AuthFailure failure) { }
    public void LoggedOut(AuthenticatedPrincipal principal, string reason) { }
    public void SwitchedUser(AuthenticatedPrincipal from, AuthenticatedPrincipal to) { }
}
