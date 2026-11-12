using APS.SystemControl.Plugin;

namespace APS.SystemControl.Hosting;

internal interface IFeaturePolicy : IFlagService
{
    bool EnsureOrShow(string feature, IWin32Window? owner);

    bool IsBundledLibrary(string dllFileName) => false;
}
