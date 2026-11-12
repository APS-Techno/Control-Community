namespace APS.Accounts.Domain;

/// <summary>
/// Captured at authentication time: pure identity + effective permissions, safe to hand
/// around the app. Idle timeout and quota live in the session manager.
/// </summary>
public sealed class AuthenticatedPrincipal
{
    public Guid UserId { get; }
    public string Username { get; }
    public string DisplayName { get; }
    public IReadOnlyList<string> Roles { get; }

    /// <summary>True when authenticated against the offline cache (DB unreachable),
    /// or for the built-in local/standalone admin.</summary>
    public bool IsOffline { get; }

    public Guid SessionId { get; }

    private readonly HashSet<Permission> _permissions;

    public AuthenticatedPrincipal(
        Guid userId,
        string username,
        string displayName,
        IEnumerable<string> roles,
        IEnumerable<Permission> permissions,
        bool isOffline,
        Guid sessionId)
    {
        UserId = userId;
        Username = username;
        DisplayName = displayName;
        Roles = roles.ToArray();
        _permissions = new HashSet<Permission>(permissions);
        IsOffline = isOffline;
        SessionId = sessionId;
    }

    public bool Can(Permission permission) => _permissions.Contains(permission);

    public IReadOnlyCollection<Permission> Permissions => _permissions;
}
