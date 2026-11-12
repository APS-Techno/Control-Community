using APS.Accounts.Domain;
using APS.Accounts.Services;
using APS.SystemControl.Accounts;

namespace APS.SystemControl.Hosting;

internal sealed class StandaloneStartupGate : IStartupGate
{
    public bool AuthorizeProduct() => true;

    public SessionManager? OpenSession()
    {
        var sessions = new SessionManager(new SystemClock(), new AccountsAuditSink());
        sessions.BeginSession(BuiltInRoles.CreateLocalAdmin());
        return sessions;
    }
}
