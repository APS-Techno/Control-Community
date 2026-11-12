using APS.Accounts.Domain;
using APS.SystemControl.Plugin;

namespace APS.SystemControl.Accounts;

/// <summary>Bridges the Plugin assembly's string-keyed <see cref="IAuthorization"/> to the
/// current user's permission set. Unknown permission names deny.</summary>
internal sealed class AuthorizationAdapter : IAuthorization
{
    public bool Can(string permission) =>
        Enum.TryParse<Permission>(permission, ignoreCase: true, out var parsed) && CurrentUser.Can(parsed);
}
