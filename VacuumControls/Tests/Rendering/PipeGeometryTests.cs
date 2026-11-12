using System.Drawing;
using APS.Controls.Vacuum.Rendering;
using Xunit;

namespace APS.Controls.Tests.Rendering;

public class PipeGeometryTests
{
    // 90° bend: tan(45°) = 1 → truncation distance == radius
    [Fact]
    public void TruncationDistance_NinetyDegrees_EqualsRadius()
    {
        var d = PipeGeometry.TruncationDistance(turnAngleRadians: MathF.PI / 2, radius: 8f);
        Assert.Equal(8f, d, precision: 4);
    }

    // 45° bend: tan(22.5°) ≈ 0.4142
    [Fact]
    public void TruncationDistance_FortyFiveDegrees_MatchesTanHalfAngle()
    {
        var d = PipeGeometry.TruncationDistance(turnAngleRadians: MathF.PI / 4, radius: 10f);
        Assert.Equal(10f * MathF.Tan(MathF.PI / 8), d, precision: 4);
    }

    [Fact]
    public void AdaptiveRadius_ClampsToHalfShorterSegmentMinusOne()
    {
        var r = PipeGeometry.AdaptiveRadius(requested: 8f, segLenA: 5f, segLenB: 100f);
        Assert.Equal(MathF.Min(8f, 5f / 2 - 1), r);
    }

    [Fact]
    public void AdaptiveRadius_NoClampWhenBothSegmentsLongEnough()
    {
        var r = PipeGeometry.AdaptiveRadius(requested: 8f, segLenA: 100f, segLenB: 50f);
        Assert.Equal(8f, r);
    }

    [Fact]
    public void AdaptiveRadius_NeverNegative()
    {
        var r = PipeGeometry.AdaptiveRadius(requested: 8f, segLenA: 0.1f, segLenB: 100f);
        Assert.True(r >= 0);
    }

    // Signed turn angle: uOut=(0,+1) is DOWN in screen coords (+Y-down), but cross-product gives +90°.
    // uOut=(0,-1) is UP in screen coords, and gives -90°.
    [Fact]
    public void TurnAngle_RightThenDownInScreenCoords_IsPositiveNinety()
    {
        var θ = PipeGeometry.TurnAngleRadians(uIn: new PointF(1, 0), uOut: new PointF(0, 1));
        Assert.Equal(MathF.PI / 2, θ, precision: 4);
    }

    [Fact]
    public void TurnAngle_RightThenUpInScreenCoords_IsNegativeNinety()
    {
        var θ = PipeGeometry.TurnAngleRadians(uIn: new PointF(1, 0), uOut: new PointF(0, -1));
        Assert.Equal(-MathF.PI / 2, θ, precision: 4);
    }
}
