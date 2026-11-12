using System.ComponentModel;
using System.Drawing.Drawing2D;
using APS.Controls.Theme;

namespace APS.Controls.Vacuum;

[ToolboxItem(false)]
public class CrossConnectorControl : ThemedVacuumControl
{
    private const int BaseSize = 28;
    private const int PipeWidth = 8;

    private float _scale = 1.0f;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float VScale
    {
        set
        {
            _scale = value;
            CalculateSize();
        }
    }

    public CrossConnectorControl(string id)
    {
        ID = id;
        Name = $"XCON_{id}";
        CalculateSize();
    }

    protected override void CalculateSize()
    {
        int s = (int)Math.Round(BaseSize * _scale * BaseScale);
        Size = new Size(s, s);
    }

    protected override IReadOnlyList<AnchorDef> DefineAnchors()
    {
        int cx = Width / 2;
        int cy = Height / 2;
        return [
            new("left", new Point(0, cy)),
            new("right", new Point(Width, cy)),
            new("top", new Point(cx, 0)),
            new("bottom", new Point(cx, Height))
        ];
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;

        int w = Width;
        int h = Height;
        int cx = w / 2;
        int cy = h / 2;
        int pw = Math.Max(4, (int)(PipeWidth * _scale));
        int half = pw / 2;

        var hBar = new Rectangle(0, cy - half, w, pw);
        DrawPipeSegment(g, hBar, horizontal: true);

        var vBar = new Rectangle(cx - half, 0, pw, h);
        DrawPipeSegment(g, vBar, horizontal: false);

        int hubR = half + 2;
        var hubRect = new Rectangle(cx - hubR, cy - hubR, hubR * 2, hubR * 2);
        using (var metalBrush = VacuumTheme.CreateMetalGradient(hubRect))
            g.FillEllipse(metalBrush, hubRect);
        using (var borderPen = new Pen(VacuumTheme.MetalEdge, 1.5f))
            g.DrawEllipse(borderPen, hubRect);

        if (hubR > 4)
        {
            var specRect = new Rectangle(cx - hubR / 2, cy - hubR + 1, hubR, hubR / 2);
            using var specBrush = new SolidBrush(Color.FromArgb(40, 255, 255, 255));
            g.FillEllipse(specBrush, specRect);
        }

        int flangeExtra = 2;
        DrawFlange(g, 0, cy - half - flangeExtra, pw + flangeExtra * 2, vertical: true);
        DrawFlange(g, w - 3, cy - half - flangeExtra, pw + flangeExtra * 2, vertical: true);
        DrawFlange(g, cx - half - flangeExtra, 0, pw + flangeExtra * 2, vertical: false);
        DrawFlange(g, cx - half - flangeExtra, h - 3, pw + flangeExtra * 2, vertical: false);
    }

    private static void DrawPipeSegment(Graphics g, Rectangle rect, bool horizontal)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;

        LinearGradientMode mode = horizontal ? LinearGradientMode.Vertical : LinearGradientMode.Horizontal;
        using var brush = new LinearGradientBrush(rect, VacuumTheme.MetalHighlight, VacuumTheme.MetalDark, mode);

        var blend = new ColorBlend(5)
        {
            Colors = [VacuumTheme.MetalDark, VacuumTheme.MetalHighlight, VacuumTheme.MetalMid, VacuumTheme.MetalHighlight, VacuumTheme.MetalDark],
            Positions = [0f, 0.25f, 0.5f, 0.75f, 1f]
        };
        brush.InterpolationColors = blend;
        g.FillRectangle(brush, rect);

        using var borderPen = new Pen(VacuumTheme.MetalEdge, 0.5f);
        g.DrawRectangle(borderPen, rect);
    }

    private static void DrawFlange(Graphics g, int x, int y, int size, bool vertical)
    {
        Rectangle rect = vertical
            ? new Rectangle(x, y, 3, size)
            : new Rectangle(x, y, size, 3);

        using var brush = new SolidBrush(VacuumTheme.MetalHighlight);
        g.FillRectangle(brush, rect);
        using var pen = new Pen(VacuumTheme.MetalEdge, 0.5f);
        g.DrawRectangle(pen, rect);
    }
}
