using APS.SystemControl.Controls.General;
using Xunit;

namespace CoreLogicTests;

public class ChamberSourceSchematicTests
{
    private static ChamberSourceSchematic.FlangeView FV(int flange, string? chamber) =>
        new(flange, chamber, "", null, false, null, 0.8, null);

    [Fact]
    public void Single_chamber_config_has_no_chamber_tabs()
        => Assert.Empty(ChamberSourceSchematic.DistinctChambers([FV(1, null), FV(2, null)]));

    [Fact]
    public void Cluster_chambers_are_distinct_in_flange_order()
    {
        var chambers = ChamberSourceSchematic.DistinctChambers(
            [FV(5, "C1"), FV(1, "C1"), FV(2, "C2"), FV(3, "C2")]);
        Assert.Equal(new[] { "C1", "C2" }, chambers);
    }
}
