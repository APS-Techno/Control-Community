using APS.SystemControl.Core.Production;

namespace CoreLogicTests;

public class TargetSubstrateDistanceTests
{
    [Fact]
    public void PureTarget_Fresh_SubtractsFullThickness()
    {
        // zero 92, lift 8, W 6mm monolithic, no erosion -> 92 + 8 - 6 = 94
        var d = TargetSubstrateDistance.Effective(92.0, 8.0, 6.0, 0.0, 0.0);
        Assert.Equal(94.0, d!.Value, 6);
    }

    [Fact]
    public void BondedTarget_UsesMaterialPlusBacking()
    {
        // Si 3mm on 3mm Cu -> stack 6mm -> 92 + 8 - 6 = 94
        var d = TargetSubstrateDistance.Effective(92.0, 8.0, 3.0, 3.0, 0.0);
        Assert.Equal(94.0, d!.Value, 6);
    }

    [Fact]
    public void Erosion_GrowsTheGap()
    {
        // 2mm of the Si eroded -> stack effectively 4mm -> 92 + 8 - (3+3-2) = 96
        var d = TargetSubstrateDistance.Effective(92.0, 8.0, 3.0, 3.0, 2.0);
        Assert.Equal(96.0, d!.Value, 6);
    }

    [Fact]
    public void RaisingLift_GrowsGap()
    {
        // The target hangs below the magnetron over the substrate, so lifting the
        // magnetron pulls the target away and the working distance grows.
        var low  = TargetSubstrateDistance.Effective(92.0, 0.0, 6.0, 0.0, 0.0);
        var high = TargetSubstrateDistance.Effective(92.0, 10.0, 6.0, 0.0, 0.0);
        Assert.True(high!.Value > low!.Value);
        Assert.Equal(10.0, high.Value - low.Value, 6);
    }

    [Theory]
    [InlineData(null, 8.0, 6.0)]   // no zero distance
    [InlineData(92.0, null, 6.0)]  // no lift
    [InlineData(92.0, 8.0, null)]  // no thickness (no mounted target)
    public void MissingInput_ReturnsNull(double? zero, double? lift, double? thickness)
    {
        Assert.Null(TargetSubstrateDistance.Effective(zero, lift, thickness, 0.0, 0.0));
    }
}
