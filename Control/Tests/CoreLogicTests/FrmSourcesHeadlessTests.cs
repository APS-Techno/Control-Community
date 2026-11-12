using APS.SystemControl.Core.Classes;
using APS.SystemControl.Forms;
using Xunit;

namespace CoreLogicTests;

public class FrmSourcesHeadlessTests
{
    [Fact]
    public void WithoutMountExtension_DistanceShowsDash()
    {
        var sm = new SourceManager(6);
        var s1 = sm.GetSource(1)!;
        s1.ZeroDistanceMm = 80;
        s1.LiftPositionMm = 10;
        using var f = new frmSources(sm);
        Assert.Equal("—", f.DistanceLabelTextForTest);
    }
}
