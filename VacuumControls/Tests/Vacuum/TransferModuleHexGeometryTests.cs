using System.Drawing;
using APS.Controls.Vacuum;
using Xunit;

namespace APS.Controls.Tests.Vacuum;

public class TransferModuleHexGeometryTests
{
    [Fact]
    public void Vertices_ReturnsExactlySixPoints()
    {
        var v = TransferModuleHexGeometry.Vertices(new PointF(100, 100), 50);
        Assert.Equal(6, v.Length);
    }

    [Fact]
    public void Vertices_AllDistinct()
    {
        var v = TransferModuleHexGeometry.Vertices(new PointF(100, 100), 50);
        Assert.Equal(6, v.Distinct().Count());
    }

    [Fact]
    public void Vertices_AllAtCorrectRadius()
    {
        var centre = new PointF(100, 100);
        float radius = 50f;
        var v = TransferModuleHexGeometry.Vertices(centre, radius);
        foreach (var p in v)
        {
            float dx = p.X - centre.X;
            float dy = p.Y - centre.Y;
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            Assert.Equal(radius, dist, precision: 3);
        }
    }

    [Fact]
    public void FaceMidpoint_Face1_IsAboveCentre()
    {
        var p = TransferModuleHexGeometry.FaceMidpoint(new PointF(100, 100), 50, faceIdx: 1);
        Assert.True(p.Y < 100, $"Face 1 midpoint Y={p.Y} should be above centre Y=100");
    }

    [Fact]
    public void FaceMidpoint_Face1_IsHorizontallyCentred()
    {
        var p = TransferModuleHexGeometry.FaceMidpoint(new PointF(100, 100), 50, faceIdx: 1);
        Assert.InRange(p.X, 99f, 101f);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [InlineData(3, 4)]
    [InlineData(4, 5)]
    [InlineData(5, 6)]
    [InlineData(6, 1)]
    public void FaceMidpoint_AdjacentFaces_AreNotEqual(int a, int b)
    {
        var pa = TransferModuleHexGeometry.FaceMidpoint(new PointF(100, 100), 50, a);
        var pb = TransferModuleHexGeometry.FaceMidpoint(new PointF(100, 100), 50, b);
        Assert.NotEqual(pa, pb);
    }

    [Fact]
    public void FaceMidpoint_AllAtApothemDistance()
    {
        var centre = new PointF(0, 0);
        float radius = 60f;
        float expectedApothem = radius * MathF.Cos(MathF.PI / 6f); // = radius * √3/2
        for (int f = 1; f <= 6; f++)
        {
            var p = TransferModuleHexGeometry.FaceMidpoint(centre, radius, f);
            float dist = MathF.Sqrt(p.X * p.X + p.Y * p.Y);
            Assert.Equal(expectedApothem, dist, precision: 3);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(-1)]
    public void FaceMidpoint_OutOfRange_Throws(int faceIdx)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TransferModuleHexGeometry.FaceMidpoint(new PointF(0, 0), 50, faceIdx));
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(0f, 1f)]
    public void ArmTip_AngleZero_PointsUp(float angleDeg, float extendNorm)
    {
        var centre = new PointF(100, 100);
        var tip = TransferModuleHexGeometry.ArmTip(centre, hexRadius: 50, armRetractedLen: 10,
                                                    angleDeg: angleDeg, extendNorm: extendNorm);
        Assert.InRange(tip.X, 99f, 101f);
        Assert.True(tip.Y < 100, $"Arm at 0° should point up; tip.Y={tip.Y}");
    }

    [Fact]
    public void ArmTip_Retracted_ShorterThanExtended()
    {
        var centre = new PointF(0, 0);
        var retracted = TransferModuleHexGeometry.ArmTip(centre, 50, 10, 0f, 0f);
        var extended  = TransferModuleHexGeometry.ArmTip(centre, 50, 10, 0f, 1f);

        float lenRetracted = MathF.Sqrt(retracted.X * retracted.X + retracted.Y * retracted.Y);
        float lenExtended  = MathF.Sqrt(extended.X  * extended.X  + extended.Y  * extended.Y);
        Assert.True(lenRetracted < lenExtended,
            $"Retracted arm ({lenRetracted}) should be shorter than extended ({lenExtended})");
    }

    [Fact]
    public void ArmTip_ExtendClampedAt1()
    {
        var centre = new PointF(0, 0);
        var atOne   = TransferModuleHexGeometry.ArmTip(centre, 50, 10, 90f, 1f);
        var atTwo   = TransferModuleHexGeometry.ArmTip(centre, 50, 10, 90f, 2f);
        Assert.Equal(atOne.X, atTwo.X, precision: 3);
        Assert.Equal(atOne.Y, atTwo.Y, precision: 3);
    }

    [Fact]
    public void ArmTip_Angle90_PointsRight()
    {
        var centre = new PointF(100, 100);
        var tip = TransferModuleHexGeometry.ArmTip(centre, 50, 10, angleDeg: 90f, extendNorm: 0f);
        Assert.True(tip.X > 100, $"Arm at 90° should point right; tip.X={tip.X}");
        Assert.InRange(tip.Y, 99f, 101f);
    }
}
