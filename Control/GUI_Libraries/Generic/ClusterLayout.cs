using System.Drawing;

namespace APS.Controls.Generic;

public sealed class ClusterLayout
{
    public Point Center { get; }
    public Rectangle HexRect { get; }

    private readonly Dictionary<string, Point> _chamberCenters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Rectangle> _tiles = new(StringComparer.OrdinalIgnoreCase);

    public Size TileSize { get; }

    public ClusterLayout(Size client, IReadOnlyList<(string Key, int FaceAngle)> chambers,
        float scale = 1f)
    {
        if (scale <= 0f) scale = 1f;
        Center = new Point(client.Width / 2, client.Height / 2);

        int tw = Math.Clamp(client.Width  / 5, (int)(90 * scale), (int)(320 * scale));
        int th = Math.Clamp(client.Height / 5, (int)(84 * scale), (int)(264 * scale));
        TileSize = new Size(tw, th);

        int hex = (int)(tw * 1.2);
        HexRect = new Rectangle(Center.X - hex / 2, Center.Y - hex / 2, hex, hex);
        double rHex = hex / 2.0;

        int maxByWidth  = (client.Width  - tw) / 2 - 4;
        int maxByHeight = (client.Height - th) / 2 - 4;
        double baseRadius = Math.Max(hex, Math.Min(maxByWidth, maxByHeight));

        // Every chamber gets the same visible pipe length: radius = hexRadius + L + tile coverage at that angle, capped to stay on screen.
        double edgeMargin = 16.0 * scale;
        double l0 = 0.8 * baseRadius - rHex - th / 2.0;
        double desiredPipe = 2.0 * l0;

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
