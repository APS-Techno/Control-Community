using APS.Accounts.Services;

namespace APS.SystemControl.Hosting;

internal interface IStartupGate
{
    bool AuthorizeProduct();

    SessionManager? OpenSession();
}
