using APS.Accounts.Audit;
using APS.Accounts.Domain;
using APS.SystemControl.Core.Classes;

namespace APS.SystemControl.Accounts;

/// <summary>
/// Maps account auth-lifecycle events to <see cref="AuditEvent"/> records and routes them
/// through <see cref="AccountsAuditBus"/> into the existing hash-chained audit log. Reuses
/// only existing AuditEvent fields (User / Event / Reason / Message) so the schema and the
/// verifier are untouched.
/// </summary>
internal sealed class AccountsAuditSink : IAccountAuditSink
{
    public void LoginSucceeded(AuthenticatedPrincipal principal, bool wasOffline) =>
        Emit(wasOffline ? "auth.offline_login" : "auth.login", principal.Username,
            reason: $"roles={string.Join(',', principal.Roles)}");

    public void LoginFailed(string username, AuthFailure failure) =>
        Emit("auth.login_failed", username, message: failure.ToString());

    public void LoggedOut(AuthenticatedPrincipal principal, string reason) =>
        Emit("auth.logout", principal.Username, reason: reason);

    public void SwitchedUser(AuthenticatedPrincipal from, AuthenticatedPrincipal to) =>
        Emit("auth.switch_user", to.Username, message: $"from={from.Username}");

    private static void Emit(string @event, string user, string reason = "", string? message = null) =>
        AccountsAuditBus.Write(new AuditEvent { Event = @event, User = user, Reason = reason, Message = message });
}
