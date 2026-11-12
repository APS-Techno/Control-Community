namespace APS.SystemControl.GUIControls.Process;

/// <summary>
/// Separated from the control so the math can be unit-tested without instantiating WinForms.
/// </summary>
public static class FloatingPanelLayout
{
    public static Rectangle Clamp(Rectangle current, Rectangle parent, Size minSize)
    {
        int w = Math.Min(Math.Max(current.Width,  minSize.Width),  parent.Width);
        int h = Math.Min(Math.Max(current.Height, minSize.Height), parent.Height);
        int x = Math.Min(Math.Max(current.X, 0), parent.Width  - w);
        int y = Math.Min(Math.Max(current.Y, 0), parent.Height - h);
        return new Rectangle(x, y, w, h);
    }

    public static int SnapAxis(int value, int parentEdge, int threshold)
        => Math.Abs(value - parentEdge) <= threshold ? parentEdge : value;
}
