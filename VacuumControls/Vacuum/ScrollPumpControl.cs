using System.ComponentModel;
using System.Drawing.Drawing2D;
using APS.Controls.Theme;
using APS.Controls.Core;

namespace APS.Controls.Vacuum;

[ToolboxItem(false)]
public class ScrollPumpControl : ThemedVacuumControl
{
    private const int DefaultWidth = 70;
    private const int DefaultHeight = 70;

    private bool _power;
    private string _direction = "up";
    private float _scale = 1.0f;
    private Font? _cachedFont;
    private int _cachedFontSize = -1;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float VScale
    {
        set
        {
            _scale = value;
            CalculateSize();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Power
    {
        get => _power;
        set
        {
            if (_power == value) return;
            _power = value;
            if (_power)
                PulseGlow(VacuumTheme.GlowGreen, 400);
            ReportActualState(value);
            Invalidate();
        }
    }

    /// <summary>Inlet direction: up (default), down, left, or right.</summary>
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

    public ScrollPumpControl(string id)
    {
        ID = id;
        this.Name = GUIConst.ControlName(DeviceType.Pump, id);
        CalculateSize();
    }

    #region [Anchors]

    protected override IReadOnlyList<AnchorDef> DefineAnchors()
    {
        // Anchor name follows the AirInlet convention — it is the client-coord
        // side the inlet faces. This preserves backward-compat with existing
        // schematics that use "SP1:top" for the default "up" direction.
        return _direction switch
        {
            "down" => [new("bottom", new Point(Width / 2, Height))],
            "left" => [new("left", new Point(0, Height / 2))],
            "right" => [new("right", new Point(Width, Height / 2))],
            _ => [new("top", new Point(Width / 2, 0))],
        };
    }

    #endregion

    #region [Sizing]

    protected override void CalculateSize()
    {
        int w = (int)Math.Round(DefaultWidth * BaseScale * _scale);
        int h = (int)Math.Round(DefaultHeight * BaseScale * _scale);

        if (_direction is "left" or "right")
            Size = new Size(h, w);
        else
            Size = new Size(w, h);
    }

    #endregion

    #region [Painting]

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;

        int clientW = ClientSize.Width;
        int clientH = ClientSize.Height;
        if (clientW < 10 || clientH < 10) return;

        // Draw in logical "up" coordinates; rotation transform maps to actual direction.
        int logicalW = _direction is "left" or "right" ? clientH : clientW;
        int logicalH = _direction is "left" or "right" ? clientW : clientH;
        int angle = _direction switch
        {
            "right" => 90,
            "down" => 180,
            "left" => 270,
            _ => 0,
        };

        var state = g.Save();
        if (angle != 0)
        {
            g.TranslateTransform(clientW / 2f, clientH / 2f);
            g.RotateTransform(angle);
            g.TranslateTransform(-logicalW / 2f, -logicalH / 2f);
        }

        DrawPump(g, logicalW, logicalH);

        g.Restore(state);
        DrawPendingOverlay(g);
    }

    private void DrawPump(Graphics g, int w, int h)
    {
        var housingRect = new Rectangle(0, 0, w - 1, h - 1);
        DrawMetalHousing(g, housingRect, 5);

        if (w > 30 && h > 30)
            DrawScrews(g, housingRect, 7);

        int cavInset = Math.Max(6, (int)(8 * BaseScale));
        int ledAreaH = Math.Max(14, (int)(18 * BaseScale));
        var cavityRect = new Rectangle(
            cavInset, cavInset,
            w - cavInset * 2,
            h - cavInset * 2 - ledAreaH);
        if (cavityRect.Width < 8 || cavityRect.Height < 8) return;
        DrawCavity(g, cavityRect, 4);

        DrawScrollSpirals(g, cavityRect);

        if (PulseIntensity > 0.01f)
        {
            var glowRect = new Rectangle(cavityRect.X - 4, cavityRect.Y - 4,
                cavityRect.Width + 8, cavityRect.Height + 8);
            DrawGlow(g, glowRect, PulseColor, PulseIntensity);
        }

        int ledW = Math.Max(10, (int)(14 * BaseScale));
        int ledH = Math.Max(6, (int)(8 * BaseScale));
        int ledX = cavInset;
        int ledY = cavityRect.Bottom + Math.Max(2, (int)(3 * BaseScale));
        var ledRect = new Rectangle(ledX, ledY, ledW, ledH);
        DrawLedIndicator(g, ledRect, VacuumTheme.GlowGreen, _power, PulseIntensity);

        DrawPumpLabel(g, ledRect, ledY, ledH, w);
    }

    #endregion

    #region [Drawing Helpers]

    private void DrawScrollSpirals(Graphics g, Rectangle cavity)
    {
        float cx = cavity.X + cavity.Width / 2f;
        float cy = cavity.Y + cavity.Height / 2f;
        float maxR = Math.Min(cavity.Width, cavity.Height) / 2f - 4;

        if (maxR < 6) return;

        Color scrollColor = _power ? Color.FromArgb(0, 220, 255) : VacuumTheme.GlowBlue;
        using var scrollPen = new Pen(Color.FromArgb(_power ? 180 : 80, scrollColor), Math.Max(1.5f, maxR / 10f));

        // Draw two interleaving Archimedean spirals (180 degrees offset)
        DrawSingleSpiral(g, scrollPen, cx, cy, maxR, 0);
        DrawSingleSpiral(g, scrollPen, cx, cy, maxR, 180);

        int dotR = Math.Max(2, (int)(maxR / 8));
        using var dotBrush = new SolidBrush(Color.FromArgb(160, scrollColor));
        g.FillEllipse(dotBrush, cx - dotR, cy - dotR, dotR * 2, dotR * 2);
    }

    private static void DrawSingleSpiral(Graphics g, Pen pen, float cx, float cy, float maxR, float offsetDeg)
    {
        const int segments = 80;
        const float totalTurns = 2.5f;
        var points = new List<PointF>();

        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            float angle = t * totalTurns * 360f + offsetDeg;
            float r = t * maxR;
            float rad = angle * MathF.PI / 180f;
            points.Add(new PointF(cx + r * MathF.Cos(rad), cy + r * MathF.Sin(rad)));
        }

        if (points.Count > 1)
            g.DrawLines(pen, points.ToArray());
    }

    private void DrawPumpLabel(Graphics g, Rectangle ledRect, int ledY, int ledH, int w)
    {
        // Integer point size computed exactly as master ((int) truncation, 7pt floor, 11pt cap),
        // then rendered as a pixel-unit font (pt × 4/3) to stay DPI-neutral.
        int pt = Math.Max(7, (int)(8 * BaseScale));
        if (pt > 11) pt = 11;

        if (_cachedFont == null || _cachedFontSize != pt)
        {
            _cachedFont?.Dispose();
            _cachedFont = new Font("Consolas", pt * 4f / 3f, FontStyle.Regular, GraphicsUnit.Pixel);
            _cachedFontSize = pt;
        }

        if (string.IsNullOrEmpty(ID)) return;

        var textSize = TextRenderer.MeasureText(ID, _cachedFont);
        int textX = ledRect.Right + Math.Max(3, (int)(4 * BaseScale));
        int textY = ledY + (ledH - textSize.Height) / 2;

        if (IsDark)
        {
            using var glowBrush = new SolidBrush(Color.FromArgb(30, VacuumTheme.GlowBlue));
            g.FillRectangle(glowBrush, textX - 2, textY - 1, textSize.Width + 4, textSize.Height + 2);
        }

        Color textColor = IsDark ? VacuumTheme.TextPrimary : Color.Black;
        TextRenderer.DrawText(g, ID, _cachedFont, new Point(textX, textY), textColor);
    }

    #endregion

    #region [Events]

    protected override void OnDoubleClick(EventArgs e)
    {
        base.OnDoubleClick(e);
        Power = !Power;
    }

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
            _cachedFont?.Dispose();
            _cachedFont = null;
        }
        base.Dispose(disposing);
    }

    #endregion
}
