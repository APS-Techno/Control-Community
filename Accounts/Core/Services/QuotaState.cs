namespace APS.Accounts.Services;

public sealed record QuotaState(bool Metered, long RemainingSeconds, long WarnThresholdSeconds)
{
    public static readonly QuotaState Unmetered = new(false, 0, 0);
}

public sealed record ReconcileResult(bool Online, long CentralRemainingSeconds)
{
    public static readonly ReconcileResult Offline = new(false, 0);
}
