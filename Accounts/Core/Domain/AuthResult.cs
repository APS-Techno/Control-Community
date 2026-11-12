namespace APS.Accounts.Domain;

public enum AuthFailure
{
    None,
    UnknownUser,
    BadPassword,
    Disabled,
    Locked,
    NoOfflineCache,     // DB unreachable and nothing cached for this user on this station
    OfflineCacheStale,  // cached credentials older than MaxOfflineCacheAge
    Error,              // unexpected error (e.g. DB connection threw)
}

public sealed class AuthResult
{
    public bool Success { get; }
    public AuthenticatedPrincipal? Principal { get; }
    public AuthFailure Failure { get; }
    public bool WasOffline { get; }
    public string? Message { get; }
    public bool MustChangePassword { get; }

    private AuthResult(bool success, AuthenticatedPrincipal? principal, AuthFailure failure,
        bool wasOffline, string? message, bool mustChangePassword)
    {
        Success = success;
        Principal = principal;
        Failure = failure;
        WasOffline = wasOffline;
        Message = message;
        MustChangePassword = mustChangePassword;
    }

    public static AuthResult Ok(AuthenticatedPrincipal principal, bool wasOffline,
        bool mustChangePassword = false) =>
        new(true, principal, AuthFailure.None, wasOffline, null, mustChangePassword);

    public static AuthResult Fail(AuthFailure failure, bool wasOffline = false, string? message = null) =>
        new(false, null, failure, wasOffline, message, false);
}
