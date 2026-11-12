namespace APS.Accounts.Domain;

/// <summary>Disabled/Locked accounts cannot authenticate.</summary>
public enum UserStatus
{
    Active,
    Disabled,
    Locked,     // too many failed logins; see UserAccount.LockedUntilUtc
}
