using System.Drawing;
using APS.SystemControl.Core.Classes;
using Xunit;

namespace CoreLogicTests;

public class ChamberRingLayoutTests
{
    private static readonly RectangleF Bounds = new(0, 0, 200, 200);   // center (100,100)
    private const float R = 50f;

    private static void AssertNear(PointF expected, PointF actual)
    {
        Assert.True(Math.Abs(expected.X - actual.X) < 0.5f, $"X {actual.X} != {expected.X}");
        Assert.True(Math.Abs(expected.Y - actual.Y) < 0.5f, $"Y {actual.Y} != {expected.Y}");
    }

    [Fact]
    public void Four_flanges_even_clockwise_from_top()
    {
        var p = ChamberRingLayout.Compute(
            [(1, null), (2, null), (3, null), (4, null)], 0, clockwise: true, Bounds, R);
        AssertNear(new PointF(100, 50), p[1]);   // top
        AssertNear(new PointF(150, 100), p[2]);  // right
        AssertNear(new PointF(100, 150), p[3]);  // bottom
        AssertNear(new PointF(50, 100), p[4]);   // left
    }

    [Fact]
    public void Counter_clockwise_reverses_sweep()
    {
        var p = ChamberRingLayout.Compute([(1, null), (2, null)], 0, clockwise: false, Bounds, R);
        AssertNear(new PointF(100, 50), p[1]);   // top
        AssertNear(new PointF(100, 150), p[2]);  // 180° regardless of direction
    }

    [Fact]
    public void Start_offset_rotates_first_flange()
    {
        var p = ChamberRingLayout.Compute([(1, null)], 90, clockwise: true, Bounds, R);
        AssertNear(new PointF(150, 100), p[1]);  // 90° cw from top = right
    }

    [Fact]
    public void Pinned_flange_uses_override_and_does_not_consume_a_slot()
    {
        // 3 flanges, flange 2 pinned at 180°; the other two split the even ring (360/2 = 180).
        var p = ChamberRingLayout.Compute(
            [(1, null), (2, 180), (3, null)], 0, clockwise: true, Bounds, R);
        AssertNear(new PointF(100, 50), p[1]);   // ring slot 0 = top
        AssertNear(new PointF(100, 150), p[2]);  // pinned 180° = bottom
        AssertNear(new PointF(100, 150), p[3]);  // ring slot 1 = 180° = bottom
    }

    [Fact]
    public void Counter_clockwise_three_flanges_sweep()
    {
        var p = ChamberRingLayout.Compute([(1,null),(2,null),(3,null)], 0, clockwise: false, Bounds, R);
        AssertNear(new PointF(100, 50),    p[1]);   // 0°   = top
        AssertNear(new PointF(56.7f, 125f), p[2]);  // -120° → (100-43.3, 100+25)
        AssertNear(new PointF(143.3f, 125f), p[3]); // -240° → (100+43.3, 100+25)
    }

    [Fact]
    public void Pinned_off_ring_angle_gives_each_flange_a_distinct_point()
    {
        // flange 2 pinned at 90° (right); free flanges 1 and 3 split the ring (step=180°)
        var p = ChamberRingLayout.Compute([(1,null),(2,90),(3,null)], 0, clockwise: true, Bounds, R);
        AssertNear(new PointF(100, 50),  p[1]);   // ring slot 0 = top
        AssertNear(new PointF(150, 100), p[2]);   // pinned 90° = right
        AssertNear(new PointF(100, 150), p[3]);   // ring slot 1 = 180° = bottom
    }

    [Fact]
    public void Empty_list_returns_empty()
        => Assert.Empty(ChamberRingLayout.Compute([], 0, true, Bounds, R));
}
