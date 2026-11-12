namespace APS.SystemControl.Plugin;

/// <summary>
/// Feature-flag / license predicate used by IsVisible callbacks to gate
/// items on licensed features. The host edition supplies the implementation.
/// </summary>
public interface IFlagService
{
    /// <summary>
    /// True iff the named feature is enabled (e.g. "custom-process-panel").
    /// </summary>
    bool Has(string featureName);
}
