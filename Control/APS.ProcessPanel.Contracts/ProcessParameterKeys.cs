namespace APS.ProcessPanel.Contracts;

/// <summary>
/// Well-known keys used in <see cref="ProcessStepInfo.Parameters"/> so producers
/// (Control) and consumers (process panels) share a single source of truth.
/// </summary>
public static class ProcessParameterKeys
{
    /// <summary>Flange number (1..6) for the active deposit step.</summary>
    public const string DepositFlange = "DepositFlange";

    /// <summary>Scheduled duration in seconds for the active deposit step.</summary>
    public const string DepositSeconds = "DepositSeconds";
}
