using System.Drawing;
using System.Linq;

namespace APS.SystemControl.Core.Classes;

/// <summary>
/// Pure geometry for the chamber source schematic. Places flanges on a ring around
/// the chamber center. Default = even spacing from <paramref name="startDegrees"/> in the
/// chosen direction, ordered by flange number; a flange with an explicit angle override
/// is pinned at that absolute angle and does not consume a ring slot.
/// 0° = 12 o'clock (top); angle increases clockwise when clockwise==true.
/// </summary>
public static class ChamberRingLayout
{
    public static IReadOnlyDictionary<int, PointF> Compute(
        IReadOnlyList<(int Flange, double? AngleOverride)> flanges,
        double startDegrees,
        bool clockwise,
        RectangleF bounds,
        float ringRadius)
    {
        var result = new Dictionary<int, PointF>();
        var center = new PointF(bounds.X + bounds.Width / 2f, bounds.Y + bounds.Height / 2f);

        var ordered = flanges.OrderBy(f => f.Flange).ToList();
        var ring = ordered.Where(f => f.AngleOverride is null).ToList();
        int m = ring.Count;
        double step = m > 0 ? 360.0 / m : 0.0;
        int dir = clockwise ? 1 : -1;

        for (int i = 0; i < m; i++)
            result[ring[i].Flange] = OnRing(center, ringRadius, startDegrees + dir * step * i);

        foreach (var f in ordered.Where(f => f.AngleOverride is not null))
            result[f.Flange] = OnRing(center, ringRadius, f.AngleOverride!.Value);

        return result;
    }

    // 0° = top; clockwise positive. Screen Y grows downward:
    //   x = cx + r*sin(θ),  y = cy - r*cos(θ)
    private static PointF OnRing(PointF c, float r, double angleDeg)
    {
        double rad = angleDeg * Math.PI / 180.0;
        return new PointF(c.X + (float)(r * Math.Sin(rad)), c.Y - (float)(r * Math.Cos(rad)));
    }
}
