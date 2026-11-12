using System.Drawing;

namespace APS.Controls.Vacuum;

/// <summary>
/// Origin is the hex centre; face 1 is at the top (angle 0° = up).
/// </summary>
public static class TransferModuleHexGeometry
{
    public const int FaceCount = 6;

    /// <summary>
    /// Vertex 0 is "top-right"; face #1 ("top") is the edge between vertices 0 and 5 (going CCW).
    /// </summary>
    public static PointF[] Vertices(PointF centre, float radius)
    {
        var v = new PointF[FaceCount];
        for (int i = 0; i < FaceCount; i++)
        {
            // Vertices at angles -60°, -120°, ... so face #1 sits at the very top.
            double rad = (-60 - 60 * i) * Math.PI / 180.0;
            v[i] = new PointF(
                centre.X + radius * (float)Math.Cos(rad),
                centre.Y + radius * (float)Math.Sin(rad));
        }
        return v;
    }

    /// <summary>
    /// Face 1 = top edge; faces 2..6 advance clockwise at 60° intervals.
    /// </summary>
    /// <param name="radius">Circumradius (vertex-to-centre distance).</param>
    public static PointF FaceMidpoint(PointF centre, float radius, int faceIdx)
    {
        if (faceIdx < 1 || faceIdx > FaceCount)
            throw new ArgumentOutOfRangeException(nameof(faceIdx),
                $"faceIdx must be 1..{FaceCount}, got {faceIdx}.");

        // For a flat-top hex the apothem (centre-to-edge distance) = radius * cos(30°).
        // Face 1 = top → apothem direction = 270° (straight up, −Y in screen coords).
        // Each subsequent face is +60° clockwise.
        double rad = (-90.0 + 60.0 * (faceIdx - 1)) * Math.PI / 180.0;
        float apothem = radius * (float)Math.Cos(Math.PI / 6.0); // = radius * √3/2
        return new PointF(
            centre.X + apothem * (float)Math.Cos(rad),
            centre.Y + apothem * (float)Math.Sin(rad));
    }

    /// <param name="hexRadius">Circumradius of the hex; defines the slit plane.</param>
    /// <param name="armRetractedLen">Length of the arm when fully retracted (should be &lt; hexRadius).</param>
    /// <param name="angleDeg">Rotation in degrees: 0 = pointing up, increasing clockwise.</param>
    /// <param name="extendNorm">Normalised extend: 0 = fully retracted, 1 = fully extended through slit.</param>
    public static PointF ArmTip(PointF centre, float hexRadius, float armRetractedLen,
                                float angleDeg, float extendNorm)
    {
        extendNorm = Math.Clamp(extendNorm, 0f, 1f);
        float len = armRetractedLen + (hexRadius - armRetractedLen + 20f) * extendNorm;
        double rad = (-90.0 + angleDeg) * Math.PI / 180.0;
        return new PointF(
            centre.X + len * (float)Math.Cos(rad),
            centre.Y + len * (float)Math.Sin(rad));
    }
}
