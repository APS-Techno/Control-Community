using System.Drawing;

namespace APS.Controls.Vacuum.Rendering;

/// <summary>
/// Public so VacuumControls.Tests can exercise them directly.
/// </summary>
public static class PipeGeometry
{
    /// <summary>
    /// Distance from the bend vertex along each adjoining segment at which the
    /// straight body ends and the elbow arc begins. For a turn angle θ and centerline
    /// radius r, this is r·tan(|θ|/2).
    /// </summary>
    public static float TruncationDistance(float turnAngleRadians, float radius)
        => radius * MathF.Tan(MathF.Abs(turnAngleRadians) / 2f);

    /// <summary>
    /// Clamps a requested elbow radius so a bend never consumes more than half
    /// of either adjoining segment (minus a 1 px margin to keep the straight
    /// body visible). Never returns a negative value.
    /// </summary>
    public static float AdaptiveRadius(float requested, float segLenA, float segLenB)
    {
        float cap = MathF.Min(segLenA, segLenB) / 2f - 1f;
        return MathF.Max(0f, MathF.Min(requested, cap));
    }

    /// <summary>
    /// Signed turn angle in radians from incoming unit direction to outgoing
    /// unit direction. Positive = counter-clockwise (left turn) in screen
    /// coordinates where +Y is down. For the call sites here, +Y is screen-down
    /// so the sign visually flips, which matches "left vs right turn on screen."
    /// </summary>
    public static float TurnAngleRadians(PointF uIn, PointF uOut)
    {
        float cross = uIn.X * uOut.Y - uIn.Y * uOut.X;
        float dot = uIn.X * uOut.X + uIn.Y * uOut.Y;
        return MathF.Atan2(cross, dot);
    }

}
