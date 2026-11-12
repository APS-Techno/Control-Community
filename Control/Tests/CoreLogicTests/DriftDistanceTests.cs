using APS.SystemControl.Core.Evidence;

namespace CoreLogicTests;

public class DriftDistanceTests
{
    private static DepositionConditions One(double? dist) => new()
    {
        Sources = { new DepositionSource { Flange = 1, Material = "Si", MagnetronModel = "MAG-4",
            MagnetronInstance = "i1", TargetToSubstrateDistanceMm = dist } },
    };

    [Fact]
    public void DistanceChange_ReportsInfo()
    {
        var report = DriftCheck.Compare(One(78.0), One(80.0));
        Assert.Contains(report.Items, i => i.Grade == DriftGrade.Info && i.Message.Contains("distance"));
        Assert.False(report.HasWarnings);
    }

    [Fact]
    public void SameDistance_NoDriftItem()
    {
        var report = DriftCheck.Compare(One(78.0), One(78.0));
        Assert.DoesNotContain(report.Items, i => i.Message.Contains("distance"));
    }

    [Fact]
    public void BothNull_NoDriftItem()
    {
        var report = DriftCheck.Compare(One(null), One(null));
        Assert.DoesNotContain(report.Items, i => i.Message.Contains("distance"));
    }
}
