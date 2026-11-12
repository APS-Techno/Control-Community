using System.ComponentModel;
using System.Drawing.Drawing2D;
using APS.Controls.Theme;
using APS.Controls.Core;

namespace APS.Controls.Vacuum;

[ToolboxItem(false)]
public class PumpControl : ThemedVacuumControl
{
    private const int DefaultControlSize = 100;

    private bool _power;
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
    public Color BorderColor { get; set; } = VacuumTheme.MetalEdge;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int BorderThickness { get; set; } = 2;

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

    public PumpControl(string name)
    {
        ID = name;
        this.Name = GUIConst.ControlName(DeviceType.Pump, name);
        CalculateSize();
    }

    #region [Anchors]

    protected override IReadOnlyList<AnchorDef> DefineAnchors()
    {
        return [new("top", new Point(Width / 2, 0))];
    }

    #endregion

    #region [Sizing]

    protected override void CalculateSize()
    {
        int size = (int)Math.Round(DefaultControlSize * BaseScale * _scale);
        Size = new Size(size, size);
    }

    #endregion

    #region [Painting]

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;

        int w = ClientSize.Width;
        int h = ClientSize.Height;
        if (w < 4 || h < 4) return;

        var housingRect = new Rectangle(0, 0, w - 1, h - 1);
        DrawMetalHousing(g, housingRect, 6);

        if (w > 30 && h > 30)
            DrawScrews(g, housingRect, 8);

        int cavInset = Math.Max(6, (int)(8 * BaseScale));
        int ledAreaH = Math.Max(14, (int)(18 * BaseScale));
        int labelAreaH = Math.Max(12, (int)(14 * BaseScale));
        var cavityRect = new Rectangle(
            cavInset, cavInset,
            w - cavInset * 2,
            h - cavInset * 2 - ledAreaH - labelAreaH);
        if (cavityRect.Width < 4 || cavityRect.Height < 4) return;
        DrawCavity(g, cavityRect, 4);

        int rotorPad = Math.Max(4, (int)(6 * BaseScale));
        int rotorDiam = Math.Min(cavityRect.Width, cavityRect.Height) - rotorPad * 2;
        if (rotorDiam < 6) rotorDiam = 6;
        int rotorX = cavityRect.X + (cavityRect.Width - rotorDiam) / 2;
        int rotorY = cavityRect.Y + (cavityRect.Height - rotorDiam) / 2;
        var rotorRect = new Rectangle(rotorX, rotorY, rotorDiam, rotorDiam);

        using (var metalBrush = VacuumTheme.CreateMetalGradient(rotorRect))
            g.FillEllipse(metalBrush, rotorRect);

        using (var glowPen = new Pen(Color.FromArgb(_power ? 140 : 50, VacuumTheme.GlowBlue), 2f))
            g.DrawEllipse(glowPen, rotorRect);

        DrawImpellerBlades(g, rotorRect);

        DrawCenterBolt(g, rotorRect);

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

        DrawPumpLabel(g, ledRect, ledY, ledH, w, cavInset);
        DrawPendingOverlay(g);
    }

    #endregion

    #region [Drawing Helpers]

    private static void DrawImpellerBlades(Graphics g, Rectangle rotorRect)
    {
        float cx = rotorRect.X + rotorRect.Width / 2f;
        float cy = rotorRect.Y + rotorRect.Height / 2f;
        float outerR = rotorRect.Width / 2f - 4;
        float innerR = outerR * 0.25f;

        if (outerR < 4) return;

        int bladeCount = 3;
        float angleStep = 360f / bladeCount;

        Color bladeColor = VacuumTheme.GlowBlue;
        using var bladeBrush = new SolidBrush(Color.FromArgb(100, bladeColor));
        using var bladeEdgePen = new Pen(Color.FromArgb(160, bladeColor), 1f);

        for (int i = 0; i < bladeCount; i++)
        {
            float startAngle = i * angleStep - 90; // start from top
            float endAngle = startAngle + angleStep * 0.6f;

            double startRad = startAngle * Math.PI / 180.0;
            double endRad = endAngle * Math.PI / 180.0;
            double midRad = (startAngle + angleStep * 0.3f) * Math.PI / 180.0;

            var p1 = new PointF(cx + innerR * (float)Math.Cos(startRad), cy + innerR * (float)Math.Sin(startRad));
            var p2 = new PointF(cx + outerR * (float)Math.Cos(midRad), cy + outerR * (float)Math.Sin(midRad));
            var p3 = new PointF(cx + innerR * (float)Math.Cos(endRad), cy + innerR * (float)Math.Sin(endRad));

            using var path = new GraphicsPath();
            path.AddPolygon([p1, p2, p3]);
            g.FillPath(bladeBrush, path);
            g.DrawPath(bladeEdgePen, path);
        }
    }

    private static void DrawCenterBolt(Graphics g, Rectangle rotorRect)
    {
        int boltDiam = Math.Max(6, rotorRect.Width / 5);
        int boltX = rotorRect.X + (rotorRect.Width - boltDiam) / 2;
        int boltY = rotorRect.Y + (rotorRect.Height - boltDiam) / 2;
        var boltRect = new Rectangle(boltX, boltY, boltDiam, boltDiam);

        if (boltRect.Width <= 0 || boltRect.Height <= 0) return;

        using var metalBrush = VacuumTheme.CreateMetalGradient(boltRect);
        g.FillEllipse(metalBrush, boltRect);

        using var borderPen = new Pen(VacuumTheme.MetalEdge, 1f);
        g.DrawEllipse(borderPen, boltRect);

        if (boltDiam > 4)
        {
            var specRect = new Rectangle(boltX + boltDiam / 4, boltY + boltDiam / 6,
                boltDiam / 2, boltDiam / 3);
            if (specRect.Width > 0 && specRect.Height > 0)
            {
                using var specBrush = new SolidBrush(Color.FromArgb(60, 255, 255, 255));
                g.FillEllipse(specBrush, specRect);
            }
        }
    }

    private void DrawPumpLabel(Graphics g, Rectangle ledRect, int ledY, int ledH, int w, int cavInset)
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
