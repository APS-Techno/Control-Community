namespace APS.SystemControl.Hosting;

internal sealed class AllFeaturesPolicy : IFeaturePolicy
{
    public bool Has(string featureName) => true;

    public bool EnsureOrShow(string feature, IWin32Window? owner) => true;
}
