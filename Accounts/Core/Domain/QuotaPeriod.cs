namespace APS.Accounts.Domain;

public enum QuotaPeriod
{
    /// <summary>A one-time lifetime pool (prepaid hours). Never auto-renews.</summary>
    Total,
    Daily,
    Weekly,
    Monthly,
}
