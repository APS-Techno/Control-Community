namespace APS.SystemControl.Core;

public static class WindowPlacement
{
    public const int MinWidth = 600;
    public const int MinHeight = 400;
    public const int MinVisibleWidth = 120;
    public const int MinVisibleHeight = 60;

    public static Rectangle Fit(Rectangle saved, IEnumerable<Rectangle> workingAreas, Rectangle primaryWorkingArea)
    {
        foreach (var wa in workingAreas)
        {
            var overlap = Rectangle.Intersect(saved, wa);
            if (overlap.Width >= MinVisibleWidth && overlap.Height >= MinVisibleHeight)
                return saved;
        }

        int w = Math.Min(saved.Width, primaryWorkingArea.Width);
        int h = Math.Min(saved.Height, primaryWorkingArea.Height);
        return new Rectangle(
            primaryWorkingArea.X + (primaryWorkingArea.Width - w) / 2,
            primaryWorkingArea.Y + (primaryWorkingArea.Height - h) / 2,
            w, h);
    }
}
