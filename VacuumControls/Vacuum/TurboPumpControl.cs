using System.ComponentModel;
using System.Drawing.Drawing2D;
using APS.Controls.Theme;
using APS.Controls.Core;

namespace APS.Controls.Vacuum;

[ToolboxItem(false)]
public class TurboPumpControl : ThemedVacuumControl
{
    private const int PanelW = 70;
    private const int PanelH = 110;
    private const int TubeW = 120;
    private const int TubeH = 64;
    private const int FlangeThick = 6;
    private const int Gap = 2;
    private const int BladeStages = 8;

    private static readonly string[] LedLabels = ["ACCL", "NORM", "ROT", "ALRM"];
    private static readonly Color[] LedColors =
    [
        VacuumTheme.GlowYellow,
        VacuumTheme.GlowGreen,
        VacuumTheme.GlowGreen,
        VacuumTheme.GlowRed
    ];

    private readonly bool[] _states = new bool[4];
    private bool _alarmWasOn;
    private string _direction = "right";
    private float _scale = 1.0f;
    private Font? _cachedFont;
    private int _cachedFontSize = -1;
    private Font? _cachedIdFont;
    private int _cachedIdFontSize = -1;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Direction
    {
        set
        {
            _direction = value;
            CalculateSize();
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

    public TurboPumpControl(string id)
    {
        ID = id;
        this.Name = GUIConst.ControlName(DeviceType.Pump, id);
        CalculateSize();
    }

    #region [State Management]

    /// <summary>
    /// True when the pump is accelerating (ACCL) or at full speed (NORM).
    /// </summary>
    public bool IsRunning => _states[0] || _states[1];

    public void SetState(bool[] states)
    {
        if (states == null || states.Length != _states.Length)
            throw new ArgumentException($"The states array must have exactly {_states.Length} elements.");

        Array.Copy(states, _states, _states.Length);

        bool alarmOn = _states[3];
        if (alarmOn && !_alarmWasOn)
            PulseGlow(VacuumTheme.GlowRed, 800, loop: true);
        else if (!alarmOn && _alarmWasOn)
            StopPulse();

        _alarmWasOn = alarmOn;
        ReportActualState(IsRunning);
        Invalidate();
    }

    #endregion

    #region [Layout]

    /// <summary>
    /// Layout is always computed in "right" logical coordinates (panel on the
    /// left, tube extending right, inlet flange on the far right). The
    /// rotation transform applied in <see cref="OnPaint"/> maps this onto the
    /// actual <see cref="Direction"/>. LogicalSize is the bounding box of the
    /// logical layout; the client Size is this rotated (W↔H swapped for
    /// up/down).
    /// </summary>
    private new struct Layout
    {
        public Rectangle Panel;
        public Rectangle Tube;
        public Rectangle InletFlange;
        public int FlangeH;
        public Size LogicalSize;
    }

    private Layout ComputeLayout()
    {
        float sf = BaseScale * _scale;
        var L = new Layout();

        int panelW = (int)(PanelW * sf);
        int panelH = (int)(PanelH * sf);
        int tubeLen = (int)(TubeW * sf);   // length along tube axis
        int tubeDia = (int)(TubeH * sf);   // diameter (cross-section)
        L.FlangeH = Math.Max(5, (int)(FlangeThick * sf));
        int flangeSize = Math.Max(10, tubeDia / 2);

        int tubeY = panelH / 2 - tubeDia / 2;
        if (tubeY < 0) tubeY = 0;
        L.Panel = new Rectangle(0, 0, panelW, panelH);
        L.Tube = new Rectangle(panelW, tubeY, tubeLen, tubeDia);
        L.InletFlange = new Rectangle(panelW + tubeLen, tubeY + tubeDia / 2 - flangeSize / 2, L.FlangeH, flangeSize);

        int logicalW = Math.Max(L.Panel.Right, Math.Max(L.Tube.Right, L.InletFlange.Right));
        int logicalH = Math.Max(L.Panel.Bottom, Math.Max(L.Tube.Bottom, L.InletFlange.Bottom));
        L.LogicalSize = new Size(logicalW, logicalH);

        return L;
    }

    /// <summary>Rotation applied to the logical layout, in degrees CW.</summary>
    private int RotationAngle() => _direction switch
    {
        "down" => 90,
        "left" => 180,
        "up" => 270,
        _ => 0,
    };

    /// <summary>Maps a logical-coordinate point to client coordinates by
    /// applying the same rotation that <see cref="OnPaint"/> applies.</summary>
    private Point LogicalToClient(int lx, int ly, int logicalW, int logicalH)
    {
        int clientW = Width;
        int clientH = Height;
        int dx = lx - logicalW / 2;
        int dy = ly - logicalH / 2;
        int rx, ry;
        switch (RotationAngle())
        {
            case 90: rx = -dy; ry = dx; break;
            case 180: rx = -dx; ry = -dy; break;
            case 270: rx = dy; ry = -dx; break;
            default: rx = dx; ry = dy; break;
        }
        return new Point(clientW / 2 + rx, clientH / 2 + ry);
    }

    /// <summary>Names a client-coordinate edge point by which side of the
    /// client rectangle it lies on. Used so rotated panel anchors carry the
    /// correct client-side name (e.g. logical panel-left becomes client "top"
    /// when Direction == "down").</summary>
    private static string ClientSideName(Point p, int clientW, int clientH)
    {
        const int tol = 2;
        if (p.X <= tol) return "left";
        if (p.X >= clientW - tol) return "right";
        if (p.Y <= tol) return "top";
        if (p.Y >= clientH - tol) return "bottom";
        return "center";
    }

    #endregion

    #region [Anchors]

    protected override IReadOnlyList<AnchorDef> DefineAnchors()
    {
        var L = ComputeLayout();
        int logicalW = L.LogicalSize.Width;
        int logicalH = L.LogicalSize.Height;

        // Logical anchor positions (always "right" mode).
        var inletLogical = new Point(L.InletFlange.Right, L.Tube.Y + L.Tube.Height / 2);
        var panelLeftLogical = new Point(L.Panel.X, L.Panel.Y + L.Panel.Height / 2);
        var panelTopLogical = new Point(L.Panel.X + L.Panel.Width / 2, L.Panel.Y);
        var panelBottomLogical = new Point(L.Panel.X + L.Panel.Width / 2, L.Panel.Bottom);

        var anchors = new List<AnchorDef>
        {
            new("inlet", LogicalToClient(inletLogical.X, inletLogical.Y, logicalW, logicalH)),
        };

        int clientW = Width;
        int clientH = Height;
        foreach (var logical in new[] { panelLeftLogical, panelTopLogical, panelBottomLogical })
        {
            var client = LogicalToClient(logical.X, logical.Y, logicalW, logicalH);
            anchors.Add(new(ClientSideName(client, clientW, clientH), client));
        }

        return anchors;
    }

    #endregion

    #region [Sizing]

    protected override void CalculateSize()
    {
        var L = ComputeLayout();
        int w = L.LogicalSize.Width;
        int h = L.LogicalSize.Height;
        // For up/down directions, swap so the rotated content fits the client.
        Size = _direction is "up" or "down" ? new Size(h, w) : new Size(w, h);
    }

    #endregion

    #region [Painting]

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        var L = ComputeLayout();

        int angle = RotationAngle();
        var state = g.Save();
        if (angle != 0)
        {
            g.TranslateTransform(Width / 2f, Height / 2f);
            g.RotateTransform(angle);
            g.TranslateTransform(-L.LogicalSize.Width / 2f, -L.LogicalSize.Height / 2f);
        }

        PaintPanel(g, L, angle);

        DrawMetalHousing(g, L.InletFlange, 2);

        PaintTube(g, L);

        if (PulseIntensity > 0.01f)
        {
            var glowRect = new Rectangle(L.Tube.X - 6, L.Tube.Y - 6,
                L.Tube.Width + 12, L.Tube.Height + 12);
            DrawGlow(g, glowRect, PulseColor, PulseIntensity);
        }

        g.Restore(state);
        DrawPendingOverlay(g);
    }

    private void PaintPanel(Graphics g, Layout L, int angle)
    {
        var r = L.Panel;
        if (r.Width < 10 || r.Height < 20) return;

        DrawMetalHousing(g, r, 5);
        if (r.Width > 30 && r.Height > 40)
            DrawScrews(g, r, 7);

        DrawIdLabel(g, r, angle);

        float sf = BaseScale * _scale;
        int idAreaH = Math.Max(9, (int)Math.Round(15 * sf));
        int topMargin = r.Y + idAreaH + (int)(4 * sf);
        int sideMargin = Math.Max(3, (int)Math.Round(6 * sf));
        int bottomMargin = Math.Max(2, (int)Math.Round(4 * sf));
        int availableH = r.Bottom - topMargin - bottomMargin;
        int slotH = availableH / 4;

        int ledW = Math.Max(5, (int)(10 * sf));
        int ledH = Math.Max(4, (int)(8 * sf));
        int ledX = r.X + sideMargin;

        EnsureLabelFont();

        for (int i = 0; i < 4; i++)
        {
            int slotY = topMargin + i * slotH;
            int ledY = slotY + (slotH - ledH) / 2;

            var ledRect = new Rectangle(ledX, ledY, ledW, ledH);
            float glowIntensity = (i == 3 && _alarmWasOn) ? PulseIntensity : 0f;
            DrawLedIndicator(g, ledRect, LedColors[i], _states[i], glowIntensity);

            Color textColor = _states[i] ? LedColors[i] : VacuumTheme.TextPrimary;
            int textX = ledX + ledW + Math.Max(3, (int)(4 * sf));
            // Draw the label counter-rotated so it stays screen-upright and
            // readable regardless of Direction (the body still rotates).
            var textSize = g.MeasureString(LedLabels[i], _cachedFont!);
            float labelCx = textX + textSize.Width / 2f;
            float labelCy = slotY + slotH / 2f;
            using var textBrush = new SolidBrush(textColor);
            DrawUprightString(g, LedLabels[i], _cachedFont!, textBrush, labelCx, labelCy, angle);
        }
    }

    private void PaintTube(Graphics g, Layout L)
    {
        var tube = L.Tube;

        // At very small scales the tube itself can be too small to paint safely.
        // DrawTubeHousing creates LinearGradientBrushes whose extent must be > 0.
        if (tube.Width <= 0 || tube.Height <= 0) return;

        // Tube is always drawn in logical "right" form (panel-side flat,
        // inlet-side rounded); rotation transform handles other directions.
        DrawTubeHousing(g, tube, 6);

        int crossSection = tube.Height;
        int inset = Math.Max(3, crossSection / 8);
        var cavityRect = new Rectangle(tube.X + inset, tube.Y + inset,
            tube.Width - inset * 2, tube.Height - inset * 2);

        // At very small scales the inset can meet or exceed the tube, leaving no room
        // for the cavity. Skip the interior detail (housing alone is enough) rather than
        // build a degenerate rectangle that DrawCavity/AddArc would reject.
        if (cavityRect.Width <= 0 || cavityRect.Height <= 0)
            return;

        DrawCavity(g, cavityRect, 4);

        DrawBladeStages(g, cavityRect);

        float shaftThick = Math.Max(2f, crossSection * 0.08f);
        using var shaftPen = new Pen(VacuumTheme.MetalEdge, shaftThick);
        int shaftY = cavityRect.Y + cavityRect.Height / 2;
        g.DrawLine(shaftPen, cavityRect.X + 2, shaftY, cavityRect.Right - 2, shaftY);

        DrawTubeBolts(g, tube);
    }

    private void DrawBladeStages(Graphics g, Rectangle cavity)
    {
        Color bladeColor = GetRotorColor();
        Color statorColor = IsDark ? Color.FromArgb(70, 75, 90) : Color.FromArgb(140, 140, 155);

        int stageCount = BladeStages;
        float stageW = cavity.Width / (float)(stageCount * 2);
        int bladesPerStage = Math.Max(2, cavity.Height / 8);
        for (int s = 0; s < stageCount; s++)
        {
            float rx = cavity.X + s * stageW * 2;
            float sx = rx + stageW;
            DrawBladeRowH(g, rx, cavity.Y, stageW, cavity.Height, bladeColor, true, bladesPerStage);
            if (sx + stageW <= cavity.Right)
                DrawBladeRowH(g, sx, cavity.Y, stageW, cavity.Height, statorColor, false, bladesPerStage);
        }
    }

    /// <summary>Horizontal blade row: blades are vertical lines angled along the tube axis.</summary>
    private static void DrawBladeRowH(Graphics g, float x, float y, float w, float h,
        Color color, bool forward, int count)
    {
        using var pen = new Pen(Color.FromArgb(200, color), Math.Max(1f, w * 0.12f));
        float spacing = h / (count + 1);
        for (int i = 1; i <= count; i++)
        {
            float by = y + i * spacing;
            if (forward)
                g.DrawLine(pen, x, by + h * 0.08f, x + w, by - h * 0.08f);
            else
                g.DrawLine(pen, x, by - h * 0.08f, x + w, by + h * 0.08f);
        }
    }

    /// <summary>Vertical blade row: blades are horizontal lines angled along the tube axis.</summary>
    private static void DrawBladeRowV(Graphics g, float x, float y, float w, float h,
        Color color, bool forward, int count)
    {
        using var pen = new Pen(Color.FromArgb(200, color), Math.Max(1f, h * 0.12f));
        float spacing = w / (count + 1);
        for (int i = 1; i <= count; i++)
        {
            float bx = x + i * spacing;
            if (forward)
                g.DrawLine(pen, bx - w * 0.08f, y, bx + w * 0.08f, y + h);
            else
                g.DrawLine(pen, bx + w * 0.08f, y, bx - w * 0.08f, y + h);
        }
    }

    private static void DrawTubeBolts(Graphics g, Rectangle tube)
    {
        float r = 1.5f;
        int margin = 5;
        using var fill = new SolidBrush(VacuumTheme.ScrewFill);
        using var border = new Pen(VacuumTheme.ScrewBorder, 0.5f);

        int count = Math.Max(2, tube.Height / 10);
        float spacing = (tube.Height - margin * 2f) / Math.Max(1, count - 1);
        for (int i = 0; i < count; i++)
        {
            float by = tube.Y + margin + i * spacing;
            g.FillEllipse(fill, tube.X + 4 - r, by - r, r * 2, r * 2);
            g.DrawEllipse(border, tube.X + 4 - r, by - r, r * 2, r * 2);
            g.FillEllipse(fill, tube.Right - 4 - r, by - r, r * 2, r * 2);
            g.DrawEllipse(border, tube.Right - 4 - r, by - r, r * 2, r * 2);
        }
    }

    /// <summary>
    /// Draws the tube housing in logical "right" form: flat-left (panel side),
    /// rounded-right (inlet side). Rotation transform handles other directions.
    /// </summary>
    private static void DrawTubeHousing(Graphics g, Rectangle b, int radius)
    {
        using var path = new GraphicsPath();
        int d = Math.Min(radius * 2, Math.Min(b.Width, b.Height));

        path.AddLine(b.X, b.Y, b.Right - d, b.Y);
        path.AddArc(b.Right - d, b.Y, d, d, 270, 90);
        path.AddArc(b.Right - d, b.Bottom - d, d, d, 0, 90);
        path.AddLine(b.Right - d, b.Bottom, b.X, b.Bottom);
        path.CloseFigure();

        using var metalBrush = VacuumTheme.CreateMetalGradient(b);
        g.FillPath(metalBrush, path);

        var hlRect = new Rectangle(b.X + 1, b.Y + 1, b.Width - 2, Math.Min(10, b.Height / 4));
        if (hlRect.Width > 0 && hlRect.Height > 0)
        {
            using var edgeBrush = VacuumTheme.CreateEdgeHighlight(hlRect);
            g.FillRectangle(edgeBrush, hlRect);
        }

        using var borderPen = new Pen(VacuumTheme.MetalEdge, 1f);
        g.DrawPath(borderPen, path);
    }

    private Color GetRotorColor()
    {
        if (_alarmWasOn)
            return IsDark ? VacuumTheme.GlowRed : Color.Red;
        if (_states[2]) // ROT
            return IsDark ? VacuumTheme.GlowGreen : Color.LimeGreen;
        if (_states[0]) // ACCL
            return IsDark ? VacuumTheme.GlowYellow : Color.Gold;
        return IsDark ? Color.FromArgb(80, 80, 100) : Color.Gray;
    }

    #endregion

    #region [Drawing Helpers]

    private void DrawIdLabel(Graphics g, Rectangle panel, int angle)
    {
        if (string.IsNullOrEmpty(ID)) return;
        EnsureIdFont();

        var textSize = g.MeasureString(ID, _cachedIdFont!);
        // Position at the top-center of the panel header, then counter-rotate so
        // the label stays screen-upright and readable regardless of Direction.
        float centerX = panel.X + panel.Width / 2f;
        float topY = panel.Y + Math.Max(3, (int)(4 * BaseScale * _scale));
        float centerY = topY + textSize.Height / 2f;

        var st = g.Save();
        g.TranslateTransform(centerX, centerY);
        if (angle != 0) g.RotateTransform(-angle);
        float lx = -textSize.Width / 2f;
        float ly = -textSize.Height / 2f;

        if (IsDark)
        {
            using var glowBrush = new SolidBrush(Color.FromArgb(30, VacuumTheme.GlowBlue));
            g.FillRectangle(glowBrush, lx - 2, ly - 1, textSize.Width + 4, textSize.Height + 2);
        }

        using var idBrush = new SolidBrush(VacuumTheme.GlowBlue);
        g.DrawString(ID, _cachedIdFont!, idBrush, lx, ly);
        g.Restore(st);
    }

    /// <summary>
    /// Draws <paramref name="text"/> centered on (<paramref name="centerX"/>,
    /// <paramref name="centerY"/>) in the current (rotated) coordinate space,
    /// counter-rotated by <c>-angle</c> so the glyphs stay screen-upright and
    /// readable regardless of the control's Direction.
    /// </summary>
    private static void DrawUprightString(Graphics g, string text, Font font, Brush brush,
        float centerX, float centerY, int angle)
    {
        var st = g.Save();
        g.TranslateTransform(centerX, centerY);
        if (angle != 0) g.RotateTransform(-angle);
        var sz = g.MeasureString(text, font);
        g.DrawString(text, font, brush, -sz.Width / 2f, -sz.Height / 2f);
        g.Restore(st);
    }

    private void EnsureIdFont()
    {
        // Integer point size rendered as a pixel-unit font (pt × 4/3) to stay DPI-neutral.
        int pt = Math.Max(5, (int)(9 * BaseScale * _scale));
        pt = Math.Min(pt, 12);
        if (_cachedIdFont == null || _cachedIdFontSize != pt)
        {
            _cachedIdFont?.Dispose();
            _cachedIdFont = new Font("Consolas", pt * 4f / 3f, FontStyle.Bold, GraphicsUnit.Pixel);
            _cachedIdFontSize = pt;
        }
    }

    private void EnsureLabelFont()
    {
        // Integer point size rendered as a pixel-unit font (pt × 4/3) to stay DPI-neutral.
        int pt = Math.Max(5, (int)(7.5f * BaseScale * _scale));
        pt = Math.Min(pt, 10);
        if (_cachedFont == null || _cachedFontSize != pt)
        {
            _cachedFont?.Dispose();
            _cachedFont = new Font("Consolas", pt * 4f / 3f, FontStyle.Regular, GraphicsUnit.Pixel);
            _cachedFontSize = pt;
        }
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
            _cachedFont?.Dispose();
            _cachedFont = null;
            _cachedIdFont?.Dispose();
            _cachedIdFont = null;
        }
        base.Dispose(disposing);
    }

    #endregion
}
