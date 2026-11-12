using APS.SystemControl.GUIControls.Process;

namespace CoreLogicTests;

public class FloatingPanelLayoutTests
{
    private static readonly Rectangle Parent = new(0, 0, 800, 600);
    private static readonly Size Min = new(200, 100);

    [Fact]
    public void Clamp_LeavesInBoundsRectUntouched()
    {
        var r = new Rectangle(50, 50, 300, 200);
        Assert.Equal(r, FloatingPanelLayout.Clamp(r, Parent, Min));
    }

    [Fact]
    public void Clamp_PushesXBackInsideWhenNegative()
    {
        var r = new Rectangle(-30, 50, 300, 200);
        Assert.Equal(new Rectangle(0, 50, 300, 200), FloatingPanelLayout.Clamp(r, Parent, Min));
    }

    [Fact]
    public void Clamp_PushesXBackInsideWhenPastRightEdge()
    {
        var r = new Rectangle(700, 50, 300, 200);   // 700+300=1000 > 800
        Assert.Equal(new Rectangle(500, 50, 300, 200), FloatingPanelLayout.Clamp(r, Parent, Min));
    }

    [Fact]
    public void Clamp_PushesYBackInsideWhenPastBottomEdge()
    {
        var r = new Rectangle(50, 500, 300, 200);   // 500+200=700 > 600
        Assert.Equal(new Rectangle(50, 400, 300, 200), FloatingPanelLayout.Clamp(r, Parent, Min));
    }

    [Fact]
    public void Clamp_EnforcesMinWidth()
    {
        var r = new Rectangle(50, 50, 100, 200);    // width below min
        Assert.Equal(new Rectangle(50, 50, 200, 200), FloatingPanelLayout.Clamp(r, Parent, Min));
    }

    [Fact]
    public void Clamp_EnforcesMinHeight()
    {
        var r = new Rectangle(50, 50, 300, 50);     // height below min
        Assert.Equal(new Rectangle(50, 50, 300, 100), FloatingPanelLayout.Clamp(r, Parent, Min));
    }

    [Fact]
    public void Clamp_ShrinksWidthIfBiggerThanParent()
    {
        var r = new Rectangle(0, 0, 900, 200);      // wider than parent
        Assert.Equal(new Rectangle(0, 0, 800, 200), FloatingPanelLayout.Clamp(r, Parent, Min));
    }

    [Fact]
    public void Clamp_RepositionsAfterParentShrinks()
    {
        // Old bounds: x=600,w=200 fits in 800-wide parent. Parent shrinks to 700.
        var shrunk = new Rectangle(0, 0, 700, 600);
        var r = new Rectangle(600, 0, 200, 200);
        Assert.Equal(new Rectangle(500, 0, 200, 200), FloatingPanelLayout.Clamp(r, shrunk, Min));
    }

    [Theory]
    [InlineData(8,    0)]    // within 10 px of left → snap to 0
    [InlineData(10,   0)]    // boundary value: snaps
    [InlineData(11,  11)]    // beyond 10 px → no snap
    public void SnapLeft_SnapsWithinThreshold(int x, int expected)
    {
        Assert.Equal(expected, FloatingPanelLayout.SnapAxis(x, parentEdge: 0, threshold: 10));
    }

    [Theory]
    [InlineData(595, 600)]   // 5 px below parentEdge → snap up to 600
    [InlineData(590, 600)]   // 10 px → snap
    [InlineData(589, 589)]   // 11 px → no snap
    public void SnapRight_SnapsWithinThreshold(int x, int expected)
    {
        Assert.Equal(expected, FloatingPanelLayout.SnapAxis(x, parentEdge: 600, threshold: 10));
    }
}
