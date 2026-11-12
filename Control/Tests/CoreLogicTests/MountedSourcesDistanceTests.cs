using APS.SystemControl.Core.Evidence;

namespace CoreLogicTests;

public class MountedSourcesDistanceTests
{
    private static readonly Guid Inst = Guid.NewGuid();

    [Fact]
    public void Build_WithMountAndGeometry_ComputesDistance()
    {
        var placements = new[]
        {
            new MagnetronPlacement(Inst.ToString(), "MAG-4", "4\" round", 1, ZeroDistanceMm: 92.0, LiftMm: 8.0),
        };
        var mounted = new Dictionary<Guid, MountedTarget>
        {
            [Inst] = new MountedTarget("Si", "4\" round", CurrentErosionDepthMm: 2.0, ThicknessMm: 3.0, BackingThicknessMm: 3.0),
        };

        var src = MountedSourcesReader.Build(placements, mounted).Single();

        Assert.Equal(92.0, src.ZeroDistanceMm!.Value, 6);
        Assert.Equal(8.0, src.LiftMm!.Value, 6);
        Assert.Equal(3.0, src.TargetThicknessMm!.Value, 6);
        Assert.Equal(3.0, src.TargetBackingMm!.Value, 6);
        Assert.Equal(96.0, src.TargetToSubstrateDistanceMm!.Value, 6); // 92 + 8 - (3+3-2)
    }

    [Fact]
    public void Build_NoZeroDistance_LeavesDistanceNull()
    {
        var placements = new[] { new MagnetronPlacement(Inst.ToString(), "MAG-4", null, 1) };
        var mounted = new Dictionary<Guid, MountedTarget>
        {
            [Inst] = new MountedTarget("Si", null, 0.0, 3.0, 3.0),
        };

        var src = MountedSourcesReader.Build(placements, mounted).Single();

        Assert.Null(src.TargetToSubstrateDistanceMm);
        Assert.Null(src.ZeroDistanceMm);
    }

    [Fact]
    public void Snapshot_RoundTrips_DistanceFields()
    {
        var snap = new DepositionSnapshot();
        snap.Conditions.Sources.Add(new DepositionSource
        {
            Flange = 1, Material = "Si",
            ZeroDistanceMm = 92.0, LiftMm = 8.0,
            TargetThicknessMm = 3.0, TargetBackingMm = 3.0, ErosionDepthMm = 2.0,
            TargetToSubstrateDistanceMm = 80.0,
        });

        var json = DepositionSnapshotSerializer.ToJson(snap);
        Assert.Contains("targetToSubstrateDistanceMm", json);
        var back = DepositionSnapshotSerializer.FromJson(json);

        Assert.Equal(80.0, back.Conditions.Sources.Single().TargetToSubstrateDistanceMm!.Value, 6);
    }

    [Fact]
    public void Snapshot_OmitsDistance_WhenNull()
    {
        var snap = new DepositionSnapshot();
        snap.Conditions.Sources.Add(new DepositionSource { Flange = 1, Material = "W" });
        var json = DepositionSnapshotSerializer.ToJson(snap);
        Assert.DoesNotContain("targetToSubstrateDistanceMm", json);
    }
}
