using System.Linq;
using APS.ProcessPanel.Contracts;
using APS.SystemControl.Core.Cluster;
using Xunit;

namespace CoreLogicTests;

public class ClusterSnapshotBuilderTests
{
    private static readonly ClusterRobotInfo NoRobot =
        new(RobotStateCode.Idle, 0, 0, 0, false, false, null, false, false);

    private static ClusterSnapshot Build(
        string[] ids,
        string? skip = null,
        System.Func<string, int?>? faceAngle = null,
        System.Func<string, double?>? pressure = null) =>
        ClusterSnapshotBuilder.Build(
            ids, skip,
            stateOf:    _ => SystemStateCode.Idle,
            pressureOf: pressure ?? (_ => 1e-6),
            slitOpenOf: _ => false,
            faceAngleOf: faceAngle ?? (k => k switch
            {
                "LL" => 0, "C1" => 60, "C2" => 120, "C3" => 180, "C4" => 240, _ => (int?)null
            }),
            robot: NoRobot);

    [Fact]
    public void Maps_each_chamber_with_face_angle()
    {
        var snap = Build(new[] { "LL", "C1", "C2", "C3", "C4" });
        Assert.Equal(5, snap.Chambers.Length);
        Assert.Equal(60, snap.Chambers.Single(c => c.Key == "C1").FaceAngle);
    }

    [Fact]
    public void Skips_main_fallback()
    {
        var snap = Build(new[] { "Main", "C1" }, skip: "Main",
            faceAngle: k => k == "C1" ? 60 : (int?)null);
        Assert.DoesNotContain(snap.Chambers, c => c.Key == "Main");
        Assert.Contains(snap.Chambers, c => c.Key == "C1");
    }

    [Fact]
    public void Drops_chamber_without_face_angle()
    {
        var snap = Build(new[] { "TM", "C1" }, faceAngle: k => k == "C1" ? 60 : (int?)null);
        Assert.DoesNotContain(snap.Chambers, c => c.Key == "TM");
    }

    [Fact]
    public void Null_pressure_is_preserved()
    {
        var snap = Build(new[] { "C1" }, pressure: _ => null);
        Assert.Null(snap.Chambers.Single().Pressure);
    }

    [Fact]
    public void Robot_is_passed_through()
    {
        var robot = new ClusterRobotInfo(RobotStateCode.Transferring, 2, 128, 200, true, false, 5e-7, true, true);
        var snap = ClusterSnapshotBuilder.Build(
            new[] { "C1" }, null,
            _ => SystemStateCode.Idle, _ => 1e-6, _ => false, _ => 60, robot);
        Assert.True(snap.Robot.HasWafer);
        Assert.Equal(2, snap.Robot.CurrentFace);
    }

    [Fact]
    public void Maps_extended_per_chamber_fields()
    {
        var snap = ClusterSnapshotBuilder.Build(
            new[] { "C1" }, null,
            stateOf: _ => SystemStateCode.Processing,
            pressureOf: _ => 1e-3,
            slitOpenOf: _ => true,
            faceAngleOf: _ => 60,
            robot: NoRobot,
            temperatureOf: _ => 25,
            flowsOf: _ => new[] { new ClusterReadingInfo("C1_Ar", "Ar", 12), new ClusterReadingInfo("C1_N2", "N2", 3) },
            powersOf: _ => new[] { new ClusterReadingInfo("C1_RF", "RF", 240), new ClusterReadingInfo("C1_RF_B", "RF B", 80) },
            valvesOf: _ => new[] { new ClusterValveInfo("C1_V", "gas", true) },
            drivePositionOf: _ => 100,
            pumpsOf: _ => (true, false));

        var c1 = snap.Chambers.Single();
        Assert.Equal(25, c1.Temperature);
        // All MFCs and power supplies pass through (primary first), not just one.
        Assert.Equal(new[] { "Ar", "N2" }, c1.Flows!.Select(f => f.Label));
        Assert.Equal(12, c1.Flows![0].Value);
        Assert.Equal(new[] { "RF", "RF B" }, c1.Powers!.Select(p => p.Label));
        Assert.Equal(240, c1.Powers![0].Value);
        Assert.Single(c1.Valves!);
        Assert.Equal(100, c1.DrivePosition);
        Assert.True(c1.TurboOn);
        Assert.False(c1.ScrollOn);
    }

    [Fact]
    public void Sources_pass_through()
    {
        var src = new ClusterSourceInfo("Cu", "C1_Sh", "C1_RF");
        var snap = ClusterSnapshotBuilder.Build(
            new[] { "C1" }, null,
            _ => SystemStateCode.Idle, _ => 1e-6, _ => false, _ => 60, NoRobot,
            sourcesOf: _ => new[] { src });
        Assert.Equal(new[] { src }, snap.Chambers.Single().Sources);
    }

    [Fact]
    public void Sources_default_to_empty_when_lookup_omitted()
    {
        var snap = Build(new[] { "C1" });
        Assert.NotNull(snap.Chambers.Single().Sources);
        Assert.Empty(snap.Chambers.Single().Sources!);
    }
}
