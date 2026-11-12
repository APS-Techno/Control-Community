using System.ComponentModel;
using System.Drawing.Drawing2D;
using APS.Controls.Theme;
using APS.Controls.Core;

namespace APS.Controls.Vacuum;

[ToolboxItem(false)]
public class AirInletControl : ThemedVacuumControl
{
    private const int DefaultWidth = 32;
    private const int DefaultHeight = 32;

    private string _direction = "right";
    private float _scale = 1.0f;
    private Font? _idFont;
    private float _cachedBaseScale = -1f;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Direction
    {
        get => _direction;
        set
        {
            _direction = value;
            CalculateSize();
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float VScale
    {
        set
        {
            _scale = value;
            CalculateSize();
        }
    }

    public AirInletControl(string id)
    {
        ID = id;
        Name = GUIConst.AIRINLET_PREFIX + id;
        CalculateSize();
    }

    #region [Anchors]

    protected override IReadOnlyList<AnchorDef> DefineAnchors()
    {
        return _direction switch
        {
            "left" => [new("left", new Point(0, Height / 2))],
            "up" => [new("top", new Point(Width / 2, 0))],
            "down" => [new("bottom", new Point(Width / 2, Height))],
            _ => [new("right", new Point(Width, Height / 2))],
        };
    }

    #endregion

    #region [Sizing]

    protected override void CalculateSize()
    {
        int w = (int)Math.Round(DefaultWidth * _scale * BaseScale);
        int h = (int)Math.Round(DefaultHeight * _scale * BaseScale);

        if (_direction is "up" or "down")
            Size = new Size(w, h);
        else
            Size = new Size(h, w); // wider than tall for horizontal
    }

    #endregion

    #region [Painting]

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int w = ClientSize.Width;
        int h = ClientSize.Height;
        if (w < 6 || h < 6) return;

        EnsureFonts();

        switch (_direction)
        {
            case "left": PaintHorizontal(g, w, h, funnelOnRight: true); break;
            case "up": PaintVertical(g, w, h, funnelOnBottom: true); break;
            case "down": PaintVertical(g, w, h, funnelOnBottom: false); break;
            default: PaintHorizontal(g, w, h, funnelOnRight: false); break; // "right"
        }
    }

    private void PaintHorizontal(Graphics g, int w, int h, bool funnelOnRight)
    {
        int cy = h / 2;
        int pw = Math.Max(4, (int)(8 * _scale * BaseScale));
        int half = pw / 2;
        int funnelW = w / 3;

        int funnelOpenH = Math.Min(h - 2, (int)(pw * 2.5f));

        if (!funnelOnRight)
        {
            // Funnel on left, anchor on right (direction "right")
            int stubX = funnelW;
            int stubW = w - funnelW;
            var stubRect = new Rectangle(stubX, cy - half, stubW, pw);
            DrawPipeSegment(g, stubRect, horizontal: true);

            var funnelPath = new GraphicsPath();
            funnelPath.AddLine(stubX, cy - half, 0, cy - funnelOpenH / 2);
            funnelPath.AddLine(0, cy - funnelOpenH / 2, 0, cy + funnelOpenH / 2);
            funnelPath.AddLine(0, cy + funnelOpenH / 2, stubX, cy + half);
            funnelPath.CloseFigure();

            using (var funnelBrush = VacuumTheme.CreateMetalGradient(new Rectangle(0, cy - funnelOpenH / 2, funnelW, funnelOpenH)))
                g.FillPath(funnelBrush, funnelPath);
            using (var borderPen = new Pen(VacuumTheme.MetalEdge, 1f))
                g.DrawPath(borderPen, funnelPath);

            DrawFlange(g, stubX - 1, cy - half - 2, pw + 4, vertical: true);
        }
        else
        {
            // Funnel on right, anchor on left (direction "left")
            int stubW = w - funnelW;
            int funnelX = stubW;
            var stubRect = new Rectangle(0, cy - half, stubW, pw);
            DrawPipeSegment(g, stubRect, horizontal: true);

            var funnelPath = new GraphicsPath();
            funnelPath.AddLine(funnelX, cy - half, w, cy - funnelOpenH / 2);
            funnelPath.AddLine(w, cy - funnelOpenH / 2, w, cy + funnelOpenH / 2);
            funnelPath.AddLine(w, cy + funnelOpenH / 2, funnelX, cy + half);
            funnelPath.CloseFigure();

            using (var funnelBrush = VacuumTheme.CreateMetalGradient(new Rectangle(funnelX, cy - funnelOpenH / 2, funnelW, funnelOpenH)))
                g.FillPath(funnelBrush, funnelPath);
            using (var borderPen = new Pen(VacuumTheme.MetalEdge, 1f))
                g.DrawPath(borderPen, funnelPath);

            DrawFlange(g, funnelX - 1, cy - half - 2, pw + 4, vertical: true);
        }

        DrawIdLabel(g, w, h);
    }

    private void PaintVertical(Graphics g, int w, int h, bool funnelOnBottom)
    {
        int cx = w / 2;
        int pw = Math.Max(4, (int)(8 * _scale * BaseScale));
        int half = pw / 2;
        int funnelH = h / 3;

        int funnelOpenW = Math.Min(w - 2, (int)(pw * 2.5f));

        if (!funnelOnBottom)
        {
            // Funnel on top, anchor on bottom (direction "down")
            int stubY = funnelH;
            int stubH = h - funnelH;
            var stubRect = new Rectangle(cx - half, stubY, pw, stubH);
            DrawPipeSegment(g, stubRect, horizontal: false);

            var funnelPath = new GraphicsPath();
            funnelPath.AddLine(cx - half, stubY, cx - funnelOpenW / 2, 0);
            funnelPath.AddLine(cx - funnelOpenW / 2, 0, cx + funnelOpenW / 2, 0);
            funnelPath.AddLine(cx + funnelOpenW / 2, 0, cx + half, stubY);
            funnelPath.CloseFigure();

            using (var funnelBrush = VacuumTheme.CreateMetalGradient(new Rectangle(cx - funnelOpenW / 2, 0, funnelOpenW, funnelH)))
                g.FillPath(funnelBrush, funnelPath);
            using (var borderPen = new Pen(VacuumTheme.MetalEdge, 1f))
                g.DrawPath(borderPen, funnelPath);

            DrawFlange(g, cx - half - 2, stubY - 1, pw + 4, vertical: false);
        }
        else
        {
            // Funnel on bottom, anchor on top (direction "up")
            int stubH = h - funnelH;
            int funnelY = stubH;
            var stubRect = new Rectangle(cx - half, 0, pw, stubH);
            DrawPipeSegment(g, stubRect, horizontal: false);

            var funnelPath = new GraphicsPath();
            funnelPath.AddLine(cx - half, funnelY, cx - funnelOpenW / 2, h);
            funnelPath.AddLine(cx - funnelOpenW / 2, h, cx + funnelOpenW / 2, h);
            funnelPath.AddLine(cx + funnelOpenW / 2, h, cx + half, funnelY);
            funnelPath.CloseFigure();

            using (var funnelBrush = VacuumTheme.CreateMetalGradient(new Rectangle(cx - funnelOpenW / 2, funnelY, funnelOpenW, funnelH)))
                g.FillPath(funnelBrush, funnelPath);
            using (var borderPen = new Pen(VacuumTheme.MetalEdge, 1f))
                g.DrawPath(borderPen, funnelPath);

            DrawFlange(g, cx - half - 2, funnelY - 1, pw + 4, vertical: false);
        }

        DrawIdLabel(g, w, h);
    }

    #endregion

    #region [Drawing Helpers]

    private static void DrawPipeSegment(Graphics g, Rectangle rect, bool horizontal)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;

        var mode = horizontal ? LinearGradientMode.Vertical : LinearGradientMode.Horizontal;
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
        var rect = vertical
            ? new Rectangle(x, y, 3, size)
            : new Rectangle(x, y, size, 3);

        using var brush = new SolidBrush(VacuumTheme.MetalHighlight);
        g.FillRectangle(brush, rect);
        using var pen = new Pen(VacuumTheme.MetalEdge, 0.5f);
        g.DrawRectangle(pen, rect);
    }

    private void DrawIdLabel(Graphics g, int w, int h)
    {
        if (string.IsNullOrEmpty(ID) || _idFont == null) return;

        var idSize = TextRenderer.MeasureText(ID, _idFont);
        int idX = (w - idSize.Width) / 2;
        int idY = h - idSize.Height;

        if (IsDark)
        {
            using var glowBrush = new SolidBrush(Color.FromArgb(30, VacuumTheme.GlowBlue));
            g.FillRectangle(glowBrush, idX - 2, idY - 1, idSize.Width + 4, idSize.Height + 2);
        }

        Color textColor = IsDark ? VacuumTheme.TextPrimary : Color.Black;
        TextRenderer.DrawText(g, ID, _idFont, new Point(idX, idY), textColor);
    }

    private void EnsureFonts()
    {
        // Keyed on BaseScale only. Pixel-unit font: pt × 4/3 so 96-DPI rendering is unchanged.
        if (_idFont != null && Math.Abs(_cachedBaseScale - BaseScale) <= 0.001f) return;

        _idFont?.Dispose();
        _idFont = new Font("Consolas", 7f * 4f / 3f * BaseScale, FontStyle.Regular, GraphicsUnit.Pixel);
        _cachedBaseScale = BaseScale;
    }

    #endregion

    #region [Events]

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }

    #endregion

    #region [Dispose]

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _idFont?.Dispose();
            _idFont = null;
        }
        base.Dispose(disposing);
    }

    #endregion
}
