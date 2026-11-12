using System.Drawing.Drawing2D;
using APS.Controls.Theme;

namespace APS.SystemControl.Controls.General;

public class GraphPanel : ThemedVacuumControl
{
    private const int Window = 250;
    private const int LeftMargin = 60;
    private const int TopMargin = 30;
    private const int BottomMargin = 10;
    private const int RightMargin = 10;
    private const int BezelInset = 4;
    private const float UpperScale = 1.05f;
    private const float LowerScale = 0.95f;

    private readonly List<float> data = [];
    private float minY, maxY;
    private float miny, maxy;
    private int plotW, plotH;

    public Color FrameColor { get; set; }
    public float FrameThickness { get; set; } = 2f;

    public string Title { get; set; } = "Title";
    public Font TitleFont { get; set; } = new Font("Arial", 12, FontStyle.Bold);
    public Color TitleColor { get; set; }
    public Color LineColor { get; set; } = Color.Blue;
    public bool LogScale { get; set; } = true;
    public float LineThickness { get; set; } = 3f;

    public GraphPanel()
    {
        FrameColor = IsDark ? VacuumTheme.TextPrimary : Color.Black;
        TitleColor = IsDark ? VacuumTheme.TextPrimary : Color.Black;
        ResetScale();
    }

    private void ResetScale()
    {
        minY = 9999;
        maxY = -9999;

        foreach (float Y in data)
        {
            if (Y < minY) minY = Y;
            if (Y > maxY) maxY = Y;
        }
    }

    public void AddPoint(float Y)
    {
        if (LogScale)
        {
            Y = (float)Math.Log10(Y);
            if (Y == 0)
                Y = 0.0001f;
        }

        data.Add(Y);

        while (data.Count > Window)
        {
            data.RemoveAt(0);
        }

        UpdateScaleWithPoint(Y);
        Invalidate();
    }

    public void ClearPoints()
    {
        data.Clear();
        ResetScale();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);

        DrawMetalHousing(e.Graphics, bounds, 5);

        var cavityRect = new Rectangle(
            LeftMargin - BezelInset,
            TopMargin - BezelInset,
            Width - LeftMargin - RightMargin + BezelInset * 2,
            Height - TopMargin - BottomMargin + BezelInset * 2);
        DrawCavity(e.Graphics, cavityRect, 3);

        plotW = Width - LeftMargin - RightMargin;
        plotH = Height - TopMargin - BottomMargin;

        if (LogScale)
        {
            miny = minY * UpperScale;
            maxy = maxY * LowerScale;
        }
        else
        {
            miny = minY * LowerScale;
            maxy = maxY * UpperScale;
        }

        DrawGridLines(e.Graphics);
        DrawAxisLabels(e.Graphics);
        DrawGraph(e.Graphics);
        DrawTitle(e.Graphics);

        using var accentPen = new Pen(FrameColor, FrameThickness);
        using var accentPath = CreateRoundedRect(bounds, 5);
        e.Graphics.DrawPath(accentPen, accentPath);
    }

    private void UpdateScaleWithPoint(float Y)
    {
        if (Y < minY) minY = Y;
        if (Y > maxY) maxY = Y;
    }

    private void DrawGraph(Graphics g)
    {
        if (data.Count < 2) return;

        try
        {
            float scaleX = (float)plotW / Window;
            float scaleY = plotH / (maxy - miny);

            using Pen linePen = new(LineColor, LineThickness);
            linePen.LineJoin = LineJoin.Round;

            for (int i = 0; i < data.Count - 1; i++)
            {
                float screenXS = i * scaleX + LeftMargin;
                float screenYS = plotH - (data[i] - miny) * scaleY + TopMargin;

                float screenXE = (i + 1) * scaleX + LeftMargin;
                float screenYE = plotH - (data[i + 1] - miny) * scaleY + TopMargin;

                screenYS = Math.Clamp(screenYS, TopMargin + 1, TopMargin + plotH - 1);
                screenYE = Math.Clamp(screenYE, TopMargin + 1, TopMargin + plotH - 1);

                g.DrawLine(linePen, screenXS, screenYS, screenXE, screenYE);
            }
        }
        catch { }
    }

    private void DrawGridLines(Graphics g)
    {
        int numberOfYLabels = 5;
        Color gridColor = IsDark ? VacuumTheme.TextDim : Color.FromArgb(200, 200, 200);

        using Pen gridPen = new(Color.FromArgb(60, gridColor), 1f);
        gridPen.DashStyle = DashStyle.Dot;

        for (int i = 1; i < numberOfYLabels; i++)
        {
            int yPos = TopMargin + plotH - (i * (plotH / numberOfYLabels));
            g.DrawLine(gridPen, LeftMargin, yPos, LeftMargin + plotW, yPos);
        }
    }

    private void DrawTitle(Graphics g)
    {
        SizeF titleSize = g.MeasureString(Title, TitleFont);
        PointF titlePosition = new((Width - titleSize.Width) / 2, 5);

        using Brush titleBrush = new SolidBrush(TitleColor);
        g.DrawString(Title, TitleFont, titleBrush, titlePosition);
    }

    private void DrawAxisLabels(Graphics g)
    {
        int numberOfYLabels = 5;
        float yInterval = (maxy - miny) / numberOfYLabels;
        Color labelColor = IsDark ? VacuumTheme.TextPrimary : Color.Black;

        for (int i = 0; i <= numberOfYLabels; i++)
        {
            string label;

            if (LogScale)
                label = Math.Pow(10, (miny + (yInterval * i))).ToString("0.##e-0");
            else
                label = (miny + (yInterval * i)).ToString("0.#");

            SizeF labelSize = g.MeasureString(label, this.Font);
            int yPos = TopMargin + plotH - (i * (plotH / numberOfYLabels));
            PointF labelPosition = new(5, yPos - (labelSize.Height / 2));

            using Brush labelBrush = new SolidBrush(labelColor);
            g.DrawString(label, this.Font, labelBrush, labelPosition);
        }
    }
}
