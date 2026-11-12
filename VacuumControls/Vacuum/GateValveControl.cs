using System.ComponentModel;
using System.Drawing.Drawing2D;
using APS.Controls.Theme;
using APS.Controls.Core;

namespace APS.Controls.Vacuum;

/// <summary>
/// Horizontal: flow left-right, bonnet extends upward.
/// Vertical: flow top-bottom, bonnet extends to the right.
/// </summary>
[ToolboxItem(false)]
public class GateValveControl : ThemedVacuumControl
{
    private const int DefaultBodyW = 70;
    private const int DefaultBodyH = 45;
    private const int DefaultFontSize = 10;

    private Color _gateColor;
    private bool _open;
    private bool _vert;
    private float _scale = 1.0f;
    private Font? _cachedFont;
    private int _cachedFontSize = -1;
    private bool _animatingTransition;
    private bool _stateInitialized;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Direction
    {
        set
        {
            _vert = value == "vert";
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

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Open
    {
        get => _open;
        set => UpdateState(value);
    }

    public override string? ExternalCaption => _vert ? ID : null;

    public GateValveControl(string id)
    {
        ID = id;
        this.Name = GUIConst.ControlName(DeviceType.Valve, id);
        _gateColor = IsDark ? Color.FromArgb(80, 80, 100) : Color.Gray;
        CalculateSize();
    }

    #region [State Management]

    private void UpdateState(bool open)
    {
        Connected = true;

        // The poll loop pushes the valve's state every 200 ms even when nothing
        // changed. Only replay the one-shot colour transition + glow pulse on a
        // genuine open<->closed change (or the first reading after connect);
        // otherwise the animation restarts every cycle and the valve shimmers
        // and never settles on a stable colour.
        if (_stateInitialized && open == _open)
            return;
        _stateInitialized = true;

        Color oldColor = _gateColor;
        _open = open;

        Color newColor = open
            ? (IsDark ? VacuumTheme.GlowGreen : Color.LimeGreen)
            : (IsDark ? VacuumTheme.GlowRed : Color.Red);

        _gateColor = newColor;
        _animatingTransition = true;
        StartAnimation(oldColor, newColor, 100f);
        PulseGlow(newColor, 250);
        ReportActualState(open);
        Invalidate();
    }

    protected override void OnConnectedChanged()
    {
        if (!Connected)
        {
            _gateColor = IsDark ? Color.FromArgb(80, 80, 100) : Color.Gray;
            _open = false;
        }
        else
        {
            _gateColor = _open
                ? (IsDark ? VacuumTheme.GlowGreen : Color.LimeGreen)
                : (IsDark ? VacuumTheme.GlowRed : Color.Red);
        }
    }

    protected override void OnAnimationTick()
    {
        if (_animatingTransition)
        {
            _gateColor = AnimationColor;
            if (AnimationProgress >= 1f)
                _animatingTransition = false;
        }
    }

    #endregion

    #region [Layout Metrics]

    private new struct Layout
    {
        public int FlangeThick;
        public int BoreRadius;
        public Rectangle Body;
        public Rectangle Bonnet;
        public Rectangle LeftFlange, RightFlange;
        public Rectangle TopFlange, BottomFlange;
        public Point TextPos;
        public Size TextSize;
    }

    private Layout ComputeLayout(Size textSize)
    {
        float sf = _scale * BaseScale;
        var L = new Layout();

        int bodyW = (int)Math.Round(DefaultBodyW * sf);
        int bodyH = (int)Math.Round(DefaultBodyH * sf);
        L.FlangeThick = Math.Max(5, (int)(5 * sf));
        L.BoreRadius = Math.Max(6, bodyH / 3);

        int bonnetW = Math.Max(12, (int)(bodyW * 0.35f));
        int bonnetH = Math.Max(20, (int)(bodyH * 1.1f));

        if (!_vert)
        {
            int bodyX = L.FlangeThick;
            int bodyY = bonnetH;
            L.Body = new Rectangle(bodyX, bodyY, bodyW, bodyH);
            L.Bonnet = new Rectangle(bodyX + bodyW / 2 - bonnetW / 2, 0, bonnetW, bonnetH);

            L.LeftFlange = new Rectangle(0, bodyY + bodyH / 4, L.FlangeThick, bodyH / 2);
            L.RightFlange = new Rectangle(bodyX + bodyW, bodyY + bodyH / 4, L.FlangeThick, bodyH / 2);

            L.TextSize = textSize;
            L.TextPos = new Point(bodyX + bodyW / 2 - textSize.Width / 2,
                                  bodyY + bodyH + L.FlangeThick + 1);
        }
        else
        {
            int bodyX = L.FlangeThick;
            int bodyY = L.FlangeThick;
            int vBodyW = bodyH;
            int vBodyH = bodyW;
            L.Body = new Rectangle(bodyX, bodyY, vBodyW, vBodyH);

            L.Bonnet = new Rectangle(bodyX + vBodyW, bodyY + vBodyH / 2 - bonnetW / 2, bonnetH, bonnetW);

            L.TopFlange = new Rectangle(bodyX + vBodyW / 4, 0, vBodyW / 2, L.FlangeThick);
            L.BottomFlange = new Rectangle(bodyX + vBodyW / 4, bodyY + vBodyH, vBodyW / 2, L.FlangeThick);

            // No caption inside the child frame — DesignCanvas draws the ID
            // label below the child in parent space (so the bottom anchor
            // stays at frame bottom and the pipe stays visible).
            L.TextSize = Size.Empty;
            L.TextPos = Point.Empty;
        }

        return L;
    }

    #endregion

    #region [Anchors]

    protected override IReadOnlyList<AnchorDef> DefineAnchors()
    {
        var textSize = MeasureIdText();
        var L = ComputeLayout(textSize);

        if (_vert)
        {
            int cx = L.Body.X + L.Body.Width / 2;
            return [new("top", new Point(cx, 0)), new("bottom", new Point(cx, L.Body.Bottom + L.FlangeThick))];
        }
        else
        {
            int cy = L.Body.Y + L.Body.Height / 2;
            return [new("left", new Point(0, cy)), new("right", new Point(L.Body.Right + L.FlangeThick, cy))];
        }
    }

    #endregion

    #region [Sizing]

    protected override void CalculateSize()
    {
        EnsureFont();
        var textSize = MeasureIdText();
        var L = ComputeLayout(textSize);

        if (!_vert)
        {
            int totalW = L.Body.Width + L.FlangeThick * 2;
            int totalH = L.Body.Bottom + L.FlangeThick + textSize.Height + (int)(3 * BaseScale);
            Size = new Size(totalW, totalH);
        }
        else
        {
            int totalW = L.Bonnet.Right;
            int totalH = L.Body.Height + L.FlangeThick * 2;
            Size = new Size(totalW, totalH);
        }
    }

    private void EnsureFont()
    {
        // Integer point size computed exactly as master (truncation, cap in points),
        // then rendered as a pixel-unit font (pt × 4/3) to stay DPI-neutral.
        int pt = (int)Math.Round(DefaultFontSize * _scale * BaseScale);
        if (pt > 11) pt = 11;
        if (pt < 1) pt = 1;

        if (_cachedFont == null || _cachedFontSize != pt)
        {
            _cachedFont?.Dispose();
            _cachedFont = new Font("Arial", pt * 4f / 3f, FontStyle.Regular, GraphicsUnit.Pixel);
            _cachedFontSize = pt;
        }
    }

    private Size MeasureIdText()
    {
        EnsureFont();
        return TextRenderer.MeasureText(ID ?? "", _cachedFont!);
    }

    #endregion

    #region [Painting]

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        EnsureFont();

        var textSize = MeasureIdText();
        var L = ComputeLayout(textSize);

        if (_vert)
        {
            DrawMetalHousing(g, L.TopFlange, 2);
            DrawMetalHousing(g, L.BottomFlange, 2);
        }
        else
        {
            DrawMetalHousing(g, L.LeftFlange, 2);
            DrawMetalHousing(g, L.RightFlange, 2);
        }

        DrawMetalHousing(g, L.Body, 5);
        if (L.Body.Width > 30 && L.Body.Height > 30)
            DrawScrews(g, L.Body, Math.Max(6, (int)(8 * _scale * BaseScale)));

        DrawMetalHousing(g, L.Bonnet, 4);
        if (L.Bonnet.Width > 16 && L.Bonnet.Height > 16)
            DrawBonnetScrews(g, L.Bonnet);

        DrawBore(g, L);

        DrawGate(g, L);

        if (PulseIntensity > 0.01f)
        {
            var glowRect = new Rectangle(L.Body.X - 4, L.Body.Y - 4,
                L.Body.Width + 8, L.Body.Height + 8);
            DrawGlow(g, glowRect, PulseColor, PulseIntensity);
        }

        // Vertical valves rely on the DesignCanvas-rendered caption.
        if (!_vert)
            DrawNeonText(g, L.TextPos.X, L.TextPos.Y);

        DrawAnchorOverlay(g);
        DrawPendingOverlay(g);
    }

    private void DrawBore(Graphics g, Layout L)
    {
        int cx, cy;
        if (!_vert)
        {
            cx = L.Body.X + L.Body.Width / 2;
            cy = L.Body.Y + L.Body.Height / 2;
        }
        else
        {
            cx = L.Body.X + L.Body.Width / 2;
            cy = L.Body.Y + L.Body.Height / 2;
        }

        int r = L.BoreRadius;
        var boreRect = new Rectangle(cx - r, cy - r, r * 2, r * 2);

        using var cavityBrush = VacuumTheme.CreateCavityGradient(boreRect);
        g.FillEllipse(cavityBrush, boreRect);

        using var shadowPen = new Pen(Color.FromArgb(80, 0, 0, 0), 1.5f);
        g.DrawEllipse(shadowPen, boreRect);

        var innerRect = new Rectangle(boreRect.X + 2, boreRect.Y + 2,
            boreRect.Width - 4, boreRect.Height - 4);
        if (innerRect.Width > 4 && innerRect.Height > 4)
        {
            using var innerPen = new Pen(Color.FromArgb(30, 255, 255, 255), 0.5f);
            g.DrawEllipse(innerPen, innerRect);
        }
    }

    private void DrawGate(Graphics g, Layout L)
    {
        int plateThick = Math.Max(4, (int)(4 * _scale * BaseScale));

        if (!_vert)
        {
            int plateX = L.Bonnet.X + L.Bonnet.Width / 2 - plateThick / 2;

            if (_open)
            {
                int plateTop = L.Bonnet.Y + 3;
                int plateBottom = L.Bonnet.Bottom - 2;
                var plateRect = new Rectangle(plateX, plateTop, plateThick, plateBottom - plateTop);
                DrawGatePlateRect(g, plateRect);
            }
            else
            {
                int bodyCenterY = L.Body.Y + L.Body.Height / 2;
                int plateTop = L.Bonnet.Y + 3;
                int plateBottom = bodyCenterY + L.BoreRadius;
                var plateRect = new Rectangle(plateX, plateTop, plateThick, plateBottom - plateTop);
                DrawGatePlateRect(g, plateRect);
            }

            using var railPen = new Pen(VacuumTheme.MetalEdge, 1f);
            int railX1 = plateX - 2;
            int railX2 = plateX + plateThick + 2;
            int railTop = L.Bonnet.Y;
            int railBot = L.Body.Y + L.Body.Height / 2 + L.BoreRadius;
            g.DrawLine(railPen, railX1, railTop, railX1, railBot);
            g.DrawLine(railPen, railX2, railTop, railX2, railBot);
        }
        else
        {
            int plateY = L.Bonnet.Y + L.Bonnet.Height / 2 - plateThick / 2;

            if (_open)
            {
                int plateLeft = L.Bonnet.X + 2;
                int plateRight = L.Bonnet.Right - 3;
                var plateRect = new Rectangle(plateLeft, plateY, plateRight - plateLeft, plateThick);
                DrawGatePlateRect(g, plateRect);
            }
            else
            {
                int bodyCenterX = L.Body.X + L.Body.Width / 2;
                int plateLeft = bodyCenterX - L.BoreRadius;
                int plateRight = L.Bonnet.Right - 3;
                var plateRect = new Rectangle(plateLeft, plateY, plateRight - plateLeft, plateThick);
                DrawGatePlateRect(g, plateRect);
            }

            using var railPen = new Pen(VacuumTheme.MetalEdge, 1f);
            int railY1 = plateY - 2;
            int railY2 = plateY + plateThick + 2;
            int railLeft = L.Body.X + L.Body.Width / 2 - L.BoreRadius;
            int railRight = L.Bonnet.Right;
            g.DrawLine(railPen, railLeft, railY1, railRight, railY1);
            g.DrawLine(railPen, railLeft, railY2, railRight, railY2);
        }
    }

    private void DrawGatePlateRect(Graphics g, Rectangle rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;

        Color lightColor = VacuumTheme.LerpColor(_gateColor, Color.White, 0.10f);
        using var brush = new LinearGradientBrush(rect, lightColor, _gateColor,
            rect.Width > rect.Height ? LinearGradientMode.Vertical : LinearGradientMode.Horizontal);
        g.FillRectangle(brush, rect);

        using var pen = new Pen(Color.FromArgb(100, 0, 0, 0), 0.5f);
        g.DrawRectangle(pen, rect);
    }

    private static void DrawBonnetScrews(Graphics g, Rectangle bonnet)
    {
        float r = 2f;
        PointF p1, p2;
        if (bonnet.Height > bonnet.Width)
        {
            float cx = bonnet.X + bonnet.Width / 2f;
            p1 = new PointF(cx, bonnet.Y + 6);
            p2 = new PointF(cx, bonnet.Bottom - 6);
        }
        else
        {
            float cy = bonnet.Y + bonnet.Height / 2f;
            p1 = new PointF(bonnet.X + 6, cy);
            p2 = new PointF(bonnet.Right - 6, cy);
        }

        using var fillBrush = new SolidBrush(VacuumTheme.ScrewFill);
        using var borderPen = new Pen(VacuumTheme.ScrewBorder, 0.5f);
        g.FillEllipse(fillBrush, p1.X - r, p1.Y - r, r * 2, r * 2);
        g.DrawEllipse(borderPen, p1.X - r, p1.Y - r, r * 2, r * 2);
        g.FillEllipse(fillBrush, p2.X - r, p2.Y - r, r * 2, r * 2);
        g.DrawEllipse(borderPen, p2.X - r, p2.Y - r, r * 2, r * 2);
    }

    private void DrawNeonText(Graphics g, int x, int y)
    {
        if (_cachedFont == null || string.IsNullOrEmpty(ID)) return;

        if (IsDark && Connected)
        {
            var textSize = TextRenderer.MeasureText(ID, _cachedFont);
            using var glowBrush = new SolidBrush(Color.FromArgb(20, _gateColor));
            g.FillRectangle(glowBrush, x - 2, y - 1, textSize.Width + 4, textSize.Height + 2);
        }

        Color textColor = IsDark ? VacuumTheme.TextPrimary : Color.Black;
        TextRenderer.DrawText(g, ID, _cachedFont, new Point(x, y), textColor);
    }

    #endregion

    #region [Events]

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }

    protected override void OnDoubleClick(EventArgs e)
    {
        if (Connected)
            base.OnDoubleClick(e);
        else
            ThemedMessageBox.Show(this, $"Gate valve {ID} is not connected!");
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
