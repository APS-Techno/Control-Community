using APS.Accounts.Domain;
using APS.Accounts.Services;

namespace APS.SystemControl.Accounts;

/// <summary>
/// App-wide access to the logged-in principal.
/// Reads live from the session manager so it reflects switch-user/logout immediately.
/// </summary>
public static class CurrentUser
{
    public static SessionManager? Sessions { get; set; }

    public static AuthenticatedPrincipal? Session => Sessions?.Current;

    public static bool Can(Permission permission) => Session?.Can(permission) ?? false;

    /// <summary>The verified username for audit attribution. Falls back to the OS user when
    /// no one is logged in (e.g. Standalone boot before a session is seated).</summary>
    public static string Name => Session?.Username ?? Environment.UserName;
}
