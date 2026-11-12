using APS.SystemControl.Core.Evidence;
using Xunit;

namespace APS.SystemControl.Tests.CoreLogic;

public class DriftCheckTests
{
    private static DepositionConditions Cond(params DepositionSource[] sources) => new()
    {
        Software = new() { ["control"] = "1.4.0" },
        Sources = sources.ToList()
    };
    private static DepositionSource Src(int flange, string mat, string model, string inst, double erosion) =>
        new() { Flange = flange, Material = mat, MagnetronModel = model, MagnetronInstance = inst, ErosionDepthMm = erosion };

    [Fact]
    public void Identical_NoItems()
    {
        var c = Cond(Src(2, "W", "MAG-4", "g1", 3.0));
        Assert.Empty(DriftCheck.Compare(c, Cond(Src(2, "W", "MAG-4", "g1", 3.0))).Items);
    }

    [Fact]
    public void MaterialSwap_IsWarning()
    {
        var captured = Cond(Src(2, "W", "MAG-4", "g1", 3.0));
        var current  = Cond(Src(2, "Si", "MAG-4", "g1", 0.0));
        var report = DriftCheck.Compare(captured, current);
        Assert.True(report.HasWarnings);
        Assert.Contains(report.Items, i => i.Grade == DriftGrade.Warning && i.Message.Contains("flange 2"));
    }

    [Fact]
    public void VersionDiff_IsNote_InstanceAndErosion_AreInfo()
    {
        var captured = Cond(Src(2, "W", "MAG-4", "g1", 3.0));
        var current  = Cond(Src(2, "W", "MAG-4", "g2", 4.5)); // different instance + advanced erosion
        current.Software["control"] = "1.5.0";
        var report = DriftCheck.Compare(captured, current);
        Assert.False(report.HasWarnings);
        Assert.Contains(report.Items, i => i.Grade == DriftGrade.Note  && i.Message.Contains("control"));
        Assert.Contains(report.Items, i => i.Grade == DriftGrade.Info);
    }

    [Fact]
    public void NullCollections_DoesNotThrow_ReturnsReport()
    {
        // Simulate an older/edited .depo.json where Sources and Software were not serialized
        var nullCond = new DepositionConditions { Sources = null!, Software = null! };
        var normal   = Cond(Src(2, "W", "MAG-4", "g1", 3.0));

        // Both directions must be advisory (non-throwing) and return a valid report
        var report1 = DriftCheck.Compare(nullCond, normal);
        var report2 = DriftCheck.Compare(normal, nullCond);

        Assert.NotNull(report1);
        Assert.NotNull(report2);
    }
}
