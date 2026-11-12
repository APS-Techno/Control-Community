using APS.SystemControl.Hosting;

namespace CoreLogicTests;

public class FeaturePolicyTests
{
    [Theory]
    [InlineData("Any.Plugin.dll")]
    [InlineData("")]
    public void FeaturePolicy_bundledLibrary_defaultsToFalse(string dllFileName)
    {
        IFeaturePolicy policy = new AllFeaturesPolicy();

        Assert.False(policy.IsBundledLibrary(dllFileName));
    }
}
