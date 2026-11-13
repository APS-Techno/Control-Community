using System.Drawing;

namespace APS.Controls.Generic;

/// <summary>
/// Pure radial geometry for the cluster process panel. Given the client size and
/// the chambers (key + face angle in degrees clockwise from 12 o'clock), computes
/// the centre hex rect, each chamber's tile rect, and spoke/slit-marker points.
/// </summary>
public sealed class ClusterLayout
{
    public Point Center { get; }
    public Rectangle HexRect { get; }

    private readonly Dictionary<string, Point> _chamberCenters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Rectangle> _tiles = new(StringComparer.OrdinalIgnoreCase);

    public Size TileSize { get; }

    /// <param name="scale">Monitor DPI factor (DpiY / 96). The clamp bounds and
    /// margins below are 96-DPI pixel values; scaling them keeps tile geometry in
    /// step with the point-size fonts GDI+ enlarges on high-DPI monitors. The
    /// client-proportional terms need no scaling — the client itself is bigger.</param>
    public ClusterLayout(Size client, IReadOnlyList<(string Key, int FaceAngle)> chambers,
        float scale = 1f)
    {
        if (scale <= 0f) scale = 1f;
        Center = new Point(client.Width / 2, client.Height / 2);

        // Tile size scales with the panel; clamped so tiny windows stay legible.
        // Upper caps are generous so tiles can grow to ~2x on a roomy window to
        // hold the scaled-up tile content (see ClusterProcessPanel.UiScale).
        int tw = Math.Clamp(client.Width  / 5, (int)(90 * scale), (int)(320 * scale));
        int th = Math.Clamp(client.Height / 5, (int)(84 * scale), (int)(264 * scale));
        TileSize = new Size(tw, th);

        // Hex is ~1.2x a tile width so the central indication chip
        // (TM / pressure / state>face) has room.
        int hex = (int)(tw * 1.2);
        HexRect = new Rectangle(Center.X - hex / 2, Center.Y - hex / 2, hex, hex);
        double rHex = hex / 2.0;

        // Base ring radius: the largest circle on which a tile still clears the
        // panel edge — limited by the vertical chambers at 12/6 o'clock.
        int maxByWidth  = (client.Width  - tw) / 2 - 4;
        int maxByHeight = (client.Height - th) / 2 - 4;
        double baseRadius = Math.Max(hex, Math.Min(maxByWidth, maxByHeight));

        // Every chamber gets the SAME *visible* pipe length L (hub edge -> tile
        // edge). A single ring radius made the verticals look longer because a
        // square tile covers different amounts of its radial spoke depending on
        // the angle (th/2 straight up, more toward a corner on the diagonals), so
        // each chamber's radius = hexRadius + L + that coverage.
        //
        // Target L is double the old 0.8-pull-in length, then capped so the
        // tightest chamber's tile still clears the panel edge by EdgeMargin. The
        // vertical chambers (12/6 o'clock) bind first, so a literal 2x isn't
        // always reachable — the cap pushes LL/C3 out to a slim margin instead of
        // off-screen, keeping all pipes equal.
        double edgeMargin = 16.0 * scale;
        double l0 = 0.8 * baseRadius - rHex - th / 2.0;
        double desiredPipe = 2.0 * l0;

        // Coverage along the radial, and the max radius that keeps the tile in
        // bounds, for a chamber at the given angle (axis-aligned slabs).
        double Cover(double sx, double sy)
        {
            double c = double.PositiveInfinity;
            if (sx > 1e-9) c = Math.Min(c, (tw / 2.0) / sx);
            if (sy > 1e-9) c = Math.Min(c, (th / 2.0) / sy);
            return c;
        }
        double MaxRadius(double sx, double sy)
        {
            double r = double.PositiveInfinity;
            if (sx > 1e-9) r = Math.Min(r, (client.Width  / 2.0 - tw / 2.0 - edgeMargin) / sx);
            if (sy > 1e-9) r = Math.Min(r, (client.Height / 2.0 - th / 2.0 - edgeMargin) / sy);
            return r;
        }

        double fitCap = double.PositiveInfinity;
        foreach (var (_, angle) in chambers)
        {
            double rad = angle * Math.PI / 180.0;
            double sx = Math.Abs(Math.Sin(rad)), sy = Math.Abs(Math.Cos(rad));
            fitCap = Math.Min(fitCap, MaxRadius(sx, sy) - rHex - Cover(sx, sy));
        }
        double targetPipe = Math.Max(Math.Min(desiredPipe, fitCap), 12.0 * scale);

        foreach (var (key, angle) in chambers)
        {
            double rad = angle * Math.PI / 180.0;
            double sx = Math.Abs(Math.Sin(rad)), sy = Math.Abs(Math.Cos(rad));
            double radius = rHex + targetPipe + Cover(sx, sy);

            int cx = Center.X + (int)Math.Round(radius * Math.Sin(rad));
            int cy = Center.Y - (int)Math.Round(radius * Math.Cos(rad));
            _chamberCenters[key] = new Point(cx, cy);
            _tiles[key] = new Rectangle(cx - tw / 2, cy - th / 2, tw, th);
        }
    }

    public Rectangle TileFor(string key) => _tiles[key];

    public Point HexEdgeToward(string key)
    {
        var p = _chamberCenters[key];
        double dx = p.X - Center.X, dy = p.Y - Center.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1) return Center;
        double r = HexRect.Width / 2.0;
        return new Point(
            Center.X + (int)Math.Round(dx / len * r),
            Center.Y + (int)Math.Round(dy / len * r));
    }
}
