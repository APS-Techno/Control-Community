using APS.SystemControl.Core.Evidence;

namespace APS.SystemControl.Hosting;

internal interface IMountedTargetSource
{
    IReadOnlyDictionary<Guid, MountedTarget> GetMountedTargets();
}
