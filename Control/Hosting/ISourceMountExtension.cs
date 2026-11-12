using CoreClass = APS.SystemControl.Core.Core;

namespace APS.SystemControl.Hosting;

internal interface ISourceMountExtension
{
    ISourceMountPanel? CreatePanel(CoreClass? core, bool readOnly);

    IReadOnlyDictionary<Guid, MountedSourceStatus> GetMountStatus();
}

internal interface ISourceMountPanel
{
    Control View { get; }

    MountedTargetGeometry? CurrentTarget { get; }

    event EventHandler? MountChanged;

    void ShowFor(SourceMountSelection? selection);

    void RefreshLive();

    double? TakeLastMountZeroDistanceMm();
}

internal sealed record SourceMountSelection(
    Guid MagnetronInstanceId, string Label, string Format, int FlangeNumber, string PowerSupplyId, string? PhysicsJson);

internal sealed record MountedTargetGeometry(double ThicknessMm, double BackingThicknessMm, double CurrentErosionDepthMm);

internal sealed record MountedSourceStatus(string Material, double LifeFraction, double WarnFraction);
