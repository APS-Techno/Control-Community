using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using APS.Controls.Theme;

namespace APS.Controls.Vacuum.Rendering;

public readonly record struct PipeOverlayStyle(
    float GlowThickness,
    int GlowAlpha,
    float StripeThickness,
    int StripeAlpha);

public static class PipeRenderer
{
    public static void DrawPipe(
        Graphics g,
        IReadOnlyList<PointF> points,
        float thickness,
        float highlightOffset,
        float highlightThickness,
        Color? overlayColor,
        PipeOverlayStyle overlayStyle)
    {
        if (points.Count < 2) return;

        var elbows = BuildElbows(points, thickness);

        DrawBody(g, points, elbows, thickness);

        DrawHighlight(g, points, elbows, highlightOffset, highlightThickness);

        if (overlayColor is { } col)
            DrawOverlay(g, points, elbows, col, overlayStyle);
    }

    private readonly record struct Elbow(
        PointF Vertex,
        PointF UIn,         // unit direction of incoming segment
        PointF UOut,        // unit direction of outgoing segment
        float TurnAngle,    // signed radians
        float Radius,
        float TruncDist,    // r·tan(|θ|/2)
        PointF TruncIn,     // point on incoming segment where straight body ends
        PointF TruncOut,    // point on outgoing segment where straight body resumes
        PointF Center);     // arc center

    private static Elbow[] BuildElbows(IReadOnlyList<PointF> pts, float thickness)
    {
        int n = pts.Count;
        if (n < 3) return [];

        var result = new Elbow[n - 2];
        for (int i = 1; i < n - 1; i++)
        {
            var a = pts[i - 1];
            var v = pts[i];
            var b = pts[i + 1];

            float dxA = v.X - a.X, dyA = v.Y - a.Y;
            float lenA = MathF.Sqrt(dxA * dxA + dyA * dyA);
            float dxB = b.X - v.X, dyB = b.Y - v.Y;
            float lenB = MathF.Sqrt(dxB * dxB + dyB * dyB);

            if (lenA < 0.5f || lenB < 0.5f)
            {
                // Degenerate: emit a zero-radius "elbow" that effectively does nothing.
                result[i - 1] = new Elbow(v, default, default, 0, 0, 0, v, v, v);
                continue;
            }

            var uIn = new PointF(dxA / lenA, dyA / lenA);
            var uOut = new PointF(dxB / lenB, dyB / lenB);

            float θ = PipeGeometry.TurnAngleRadians(uIn, uOut);
            // Default elbow centerline radius is 0.7×thickness — inner rim ≈ 0.2×thickness
            // (small visible curve, not a sharp point), outer rim ≈ 1.2×thickness.
            float r = PipeGeometry.AdaptiveRadius(thickness * 0.7f, lenA, lenB);
            float d = PipeGeometry.TruncationDistance(θ, r);
            d = MathF.Min(d, MathF.Min(lenA, lenB) / 2f - 0.5f);
            d = MathF.Max(0f, d);

            var truncIn = new PointF(v.X - uIn.X * d, v.Y - uIn.Y * d);
            var truncOut = new PointF(v.X + uOut.X * d, v.Y + uOut.Y * d);

            // Center of the elbow's arc: from the incoming-side truncation point,
            // step perpendicular to the incoming segment toward the inside of the bend, by r.
            // perp(uIn) rotated 90° CCW = (-uIn.Y, uIn.X); times sign picks the inside.
            float cross = uIn.X * uOut.Y - uIn.Y * uOut.X;
            float sign = cross >= 0 ? 1f : -1f;
            float px = -uIn.Y * sign;
            float py = uIn.X * sign;
            var center = new PointF(truncIn.X + px * r, truncIn.Y + py * r);

            result[i - 1] = new Elbow(v, uIn, uOut, θ, r, d, truncIn, truncOut, center);
        }
        return result;
    }

    private static readonly Color BodyEdgeDark = Color.FromArgb(120, 120, 140);

    private static void DrawBody(Graphics g, IReadOnlyList<PointF> pts, Elbow[] elbows, float thickness)
    {
        for (int i = 0; i < pts.Count - 1; i++)
        {
            PointF p1 = i == 0 ? pts[i] : elbows[i - 1].TruncOut;
            PointF p2 = i == pts.Count - 2 ? pts[i + 1] : elbows[i].TruncIn;
            // Ends that abut an elbow get flat caps so the round half-circle doesn't bulge into the
            // elbow patch and cause a brightness seam at the truncation point.  Ends that are true
            // pipe endpoints (first point of first segment, last point of last segment) keep round
            // caps for a natural pipe-end appearance.
            bool flatStart = i > 0;
            bool flatEnd   = i < pts.Count - 2;
            DrawStraightBody(g, p1, p2, thickness, flatStart, flatEnd);
        }

        foreach (var e in elbows)
        {
            if (e.Radius <= 0.01f) continue;
            DrawElbowBody(g, e, thickness);
        }
    }

    private static void DrawStraightBody(Graphics g, PointF p1, PointF p2, float thickness, bool flatStart, bool flatEnd)
    {
        float dx = p2.X - p1.X, dy = p2.Y - p1.Y;
        float length = MathF.Sqrt(dx * dx + dy * dy);
        if (length < 0.5f) return;

        float nx = -dy / length, ny = dx / length;
        float halfThick = thickness / 2f + 2f;
        var gradStart = new PointF((p1.X + p2.X) / 2 + nx * halfThick, (p1.Y + p2.Y) / 2 + ny * halfThick);
        var gradEnd = new PointF((p1.X + p2.X) / 2 - nx * halfThick, (p1.Y + p2.Y) / 2 - ny * halfThick);
        if (MathF.Abs(gradStart.X - gradEnd.X) < 0.1f && MathF.Abs(gradStart.Y - gradEnd.Y) < 0.1f)
            gradEnd = new PointF(gradEnd.X + 1f, gradEnd.Y + 1f);

        using var brush = new LinearGradientBrush(gradStart, gradEnd,
            VacuumTheme.MetalHighlight, VacuumTheme.MetalDark)
        {
            InterpolationColors = new ColorBlend(5)
            {
                Colors = [BodyEdgeDark, VacuumTheme.MetalHighlight, VacuumTheme.MetalMid, VacuumTheme.MetalMid, VacuumTheme.MetalDark],
                Positions = [0f, 0.15f, 0.4f, 0.7f, 1f]
            }
        };
        // Flat caps at truncation points so the cap doesn't bulge into (and color-mismatch with) the elbow patch.
        using var pen = new Pen(brush, thickness)
        {
            StartCap = flatStart ? LineCap.Flat : LineCap.Round,
            EndCap   = flatEnd   ? LineCap.Flat : LineCap.Round
        };
        g.DrawLine(pen, p1, p2);
    }

    private static void DrawElbowBody(Graphics g, in Elbow e, float thickness)
    {
        float rOuter = e.Radius + thickness / 2f;
        float rInner = MathF.Max(0.1f, e.Radius - thickness / 2f);

        float startAngleDeg = MathF.Atan2(e.TruncIn.Y - e.Center.Y, e.TruncIn.X - e.Center.X) * 180f / MathF.PI;
        float endAngleDeg = MathF.Atan2(e.TruncOut.Y - e.Center.Y, e.TruncOut.X - e.Center.X) * 180f / MathF.PI;
        float totalSweepDeg = NormalizeSweep(endAngleDeg - startAngleDeg, e.TurnAngle);

        // Subdivide the elbow into N sub-arcs so each can have its own LinearGradientBrush
        // aligned to the local radial direction at the sub-arc's midpoint. This approximates a
        // true radial gradient (which GDI+ doesn't directly support) and eliminates the visible
        // brightness seam at the truncation points where the bisector-aligned single-brush approach
        // diverges from the straight-segment perpendicular gradients.
        const int SubArcs = 3;
        float subSweep = totalSweepDeg / SubArcs;

        var outerRect = new RectangleF(e.Center.X - rOuter, e.Center.Y - rOuter, rOuter * 2, rOuter * 2);
        var innerRect = new RectangleF(e.Center.X - rInner, e.Center.Y - rInner, rInner * 2, rInner * 2);

        for (int i = 0; i < SubArcs; i++)
        {
            float subStart = startAngleDeg + subSweep * i;
            float subMid = subStart + subSweep / 2f;

            using var path = new GraphicsPath();
            path.AddArc(outerRect, subStart, subSweep);
            path.AddArc(innerRect, subStart + subSweep, -subSweep);
            path.CloseFigure();

            // Gradient axis along the LOCAL radial direction at this sub-arc's midpoint,
            // from inner rim (BodyEdgeDark stop) out to outer rim (MetalDark stop).
            var rimInner = PointAtAngle(e.Center, rInner, subMid);
            var rimOuter = PointAtAngle(e.Center, rOuter, subMid);
            if (Distance(rimInner, rimOuter) < 0.1f)
                rimOuter = new PointF(rimOuter.X + 1f, rimOuter.Y + 1f);

            using var brush = new LinearGradientBrush(rimInner, rimOuter,
                VacuumTheme.MetalHighlight, VacuumTheme.MetalDark)
            {
                InterpolationColors = new ColorBlend(5)
                {
                    Colors = [BodyEdgeDark, VacuumTheme.MetalHighlight, VacuumTheme.MetalMid, VacuumTheme.MetalMid, VacuumTheme.MetalDark],
                    Positions = [0f, 0.15f, 0.4f, 0.7f, 1f]
                }
            };
            g.FillPath(brush, path);
        }
    }

    // Normalize a raw (endAngle - startAngle) into the same sign as the turn so the arc draws the right way.
    private static float NormalizeSweep(float rawSweepDeg, float turnAngle)
    {
        while (rawSweepDeg > 180) rawSweepDeg -= 360;
        while (rawSweepDeg < -180) rawSweepDeg += 360;
        if (turnAngle > 0 && rawSweepDeg < 0) rawSweepDeg += 360;
        if (turnAngle < 0 && rawSweepDeg > 0) rawSweepDeg -= 360;
        return rawSweepDeg;
    }

    private static PointF PointAtAngle(PointF center, float radius, float angleDeg)
    {
        float r = angleDeg * MathF.PI / 180f;
        return new PointF(center.X + radius * MathF.Cos(r), center.Y + radius * MathF.Sin(r));
    }

    private static float Distance(PointF a, PointF b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private static void DrawHighlight(Graphics g, IReadOnlyList<PointF> pts, Elbow[] elbows,
        float offset, float thickness)
    {
        var prevSmoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var pen = new Pen(Color.FromArgb(110, VacuumTheme.MetalHighlight), thickness)
        { StartCap = LineCap.Round, EndCap = LineCap.Round };

        for (int i = 0; i < pts.Count - 1; i++)
        {
            PointF p1 = i == 0 ? pts[i] : elbows[i - 1].TruncOut;
            PointF p2 = i == pts.Count - 2 ? pts[i + 1] : elbows[i].TruncIn;
            DrawStraightHighlight(g, pen, p1, p2, offset);
        }

        foreach (var e in elbows)
        {
            if (e.Radius <= 0.01f) continue;
            DrawElbowHighlight(g, pen, e, offset);
        }

        g.SmoothingMode = prevSmoothing;
    }

    private static void DrawStraightHighlight(Graphics g, Pen pen, PointF p1, PointF p2, float offset)
    {
        float dx = p2.X - p1.X, dy = p2.Y - p1.Y;
        float length = MathF.Sqrt(dx * dx + dy * dy);
        if (length < 0.5f) return;
        float nx = -dy / length, ny = dx / length;
        var h1 = new PointF(p1.X + nx * offset, p1.Y + ny * offset);
        var h2 = new PointF(p2.X + nx * offset, p2.Y + ny * offset);
        g.DrawLine(pen, h1, h2);
    }

    private static void DrawElbowHighlight(Graphics g, Pen pen, in Elbow e, float offset)
    {
        // Highlight runs along a constant-radius arc near the centerline. The straight-segment
        // highlight is offset perpendicular to the segment toward one side; the elbow arc continues
        // at radius = elbow centerline radius + offset relative to the elbow center.
        float rArc = e.Radius + offset;
        var rect = new RectangleF(e.Center.X - rArc, e.Center.Y - rArc, rArc * 2, rArc * 2);
        float startAngleDeg = MathF.Atan2(e.TruncIn.Y - e.Center.Y, e.TruncIn.X - e.Center.X) * 180f / MathF.PI;
        float endAngleDeg = MathF.Atan2(e.TruncOut.Y - e.Center.Y, e.TruncOut.X - e.Center.X) * 180f / MathF.PI;
        float sweepDeg = NormalizeSweep(endAngleDeg - startAngleDeg, e.TurnAngle);
        g.DrawArc(pen, rect, startAngleDeg, sweepDeg);
    }

    private static void DrawOverlay(Graphics g, IReadOnlyList<PointF> pts, Elbow[] elbows,
        Color color, PipeOverlayStyle style)
    {
        var prev = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var glowPen = new Pen(Color.FromArgb(style.GlowAlpha, color), style.GlowThickness)
        { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var stripePen = new Pen(Color.FromArgb(style.StripeAlpha, color), style.StripeThickness)
        { StartCap = LineCap.Round, EndCap = LineCap.Round };

        for (int i = 0; i < pts.Count - 1; i++)
        {
            PointF p1 = i == 0 ? pts[i] : elbows[i - 1].TruncOut;
            PointF p2 = i == pts.Count - 2 ? pts[i + 1] : elbows[i].TruncIn;
            g.DrawLine(glowPen, p1, p2);
        }
        foreach (var e in elbows)
        {
            if (e.Radius <= 0.01f) continue;
            var rect = new RectangleF(e.Center.X - e.Radius, e.Center.Y - e.Radius, e.Radius * 2, e.Radius * 2);
            float startAngleDeg = MathF.Atan2(e.TruncIn.Y - e.Center.Y, e.TruncIn.X - e.Center.X) * 180f / MathF.PI;
            float endAngleDeg = MathF.Atan2(e.TruncOut.Y - e.Center.Y, e.TruncOut.X - e.Center.X) * 180f / MathF.PI;
            float sweepDeg = NormalizeSweep(endAngleDeg - startAngleDeg, e.TurnAngle);
            g.DrawArc(glowPen, rect, startAngleDeg, sweepDeg);
        }

        // Center stripes after all halos so glow can't bleed over stripe endpoints.
        for (int i = 0; i < pts.Count - 1; i++)
        {
            PointF p1 = i == 0 ? pts[i] : elbows[i - 1].TruncOut;
            PointF p2 = i == pts.Count - 2 ? pts[i + 1] : elbows[i].TruncIn;
            g.DrawLine(stripePen, p1, p2);
        }
        foreach (var e in elbows)
        {
            if (e.Radius <= 0.01f) continue;
            var rect = new RectangleF(e.Center.X - e.Radius, e.Center.Y - e.Radius, e.Radius * 2, e.Radius * 2);
            float startAngleDeg = MathF.Atan2(e.TruncIn.Y - e.Center.Y, e.TruncIn.X - e.Center.X) * 180f / MathF.PI;
            float endAngleDeg = MathF.Atan2(e.TruncOut.Y - e.Center.Y, e.TruncOut.X - e.Center.X) * 180f / MathF.PI;
            float sweepDeg = NormalizeSweep(endAngleDeg - startAngleDeg, e.TurnAngle);
            g.DrawArc(stripePen, rect, startAngleDeg, sweepDeg);
        }

        g.SmoothingMode = prev;
    }
}
