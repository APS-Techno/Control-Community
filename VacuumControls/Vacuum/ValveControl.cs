using System.ComponentModel;
using System.Drawing.Drawing2D;
using APS.Controls.Theme;
using APS.Controls.Core;

namespace APS.Controls.Vacuum;

/// <summary>
/// Horizontal: flow left-right, stem extends upward.
/// Vertical: flow top-bottom, stem extends to the right.
/// </summary>
[ToolboxItem(false)]
public class ValveControl : ThemedVacuumControl
{
    private const int DefaultWidth = 20;
    private const int DefaultHeight = 25;
    private const int DefaultFontSize = 10;

    private Color _triangleColor;
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

    public ValveControl(string Id)
    {
        ID = Id;
        this.Name = GUIConst.ControlName(DeviceType.Valve, Id);
        _triangleColor = IsDark ? Color.FromArgb(80, 80, 100) : Color.Gray;
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

        Color oldColor = _triangleColor;
        _open = open;

        Color newColor = open
            ? (IsDark ? VacuumTheme.GlowGreen : Color.LimeGreen)
            : (IsDark ? VacuumTheme.GlowRed : Color.Red);

        _triangleColor = newColor;
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
            _triangleColor = IsDark ? Color.FromArgb(80, 80, 100) : Color.Gray;
            _open = false;
        }
        else
        {
            _triangleColor = _open
                ? (IsDark ? VacuumTheme.GlowGreen : Color.LimeGreen)
                : (IsDark ? VacuumTheme.GlowRed : Color.Red);
        }
    }

    protected override void OnAnimationTick()
    {
        if (_animatingTransition)
        {
            _triangleColor = AnimationColor;
            if (AnimationProgress >= 1f)
                _animatingTransition = false;
        }
    }

    #endregion

    #region [Layout Metrics]

    private new struct Layout
    {
        public int FlangeThick;
        public Rectangle Body;
        public Rectangle Cavity;
        public Rectangle LeftFlange, RightFlange;
        public Rectangle TopFlange, BottomFlange;
        public Rectangle Stem;
        public Rectangle Actuator;
        public Point TextPos;
        public Size TextSize;
        public int TriX, TriY, TriW, TriH;
        public int HubCX, HubCY, HubRadius;
    }

    private Layout ComputeLayout(Size textSize)
    {
        float sf = _scale * BaseScale;
        var L = new Layout();

        int bodyW = (int)Math.Round(DefaultWidth * sf);
        int bodyH = (int)Math.Round(DefaultHeight * sf);
        int pad = Math.Max(6, (int)(4 * BaseScale));
        L.FlangeThick = Math.Max(4, (int)(3 * sf));
        int stemLength = Math.Max(6, bodyH / 4);
        int stemThick = Math.Max(3, bodyW / 5);

        int actW = Math.Max(12, (int)(bodyW * 0.8f));
        int actH = Math.Max(8, (int)(bodyH * 0.35f));

        if (!_vert)
        {
            int hW = bodyH + pad * 2;
            int hH = bodyW + pad * 2;

            int bodyX = L.FlangeThick;
            int bodyY = actH + stemLength;
            L.Body = new Rectangle(bodyX, bodyY, hW, hH);

            L.LeftFlange = new Rectangle(0, bodyY + hH / 4, L.FlangeThick, hH / 2);
            L.RightFlange = new Rectangle(bodyX + hW, bodyY + hH / 4, L.FlangeThick, hH / 2);

            L.Stem = new Rectangle(bodyX + hW / 2 - stemThick / 2, actH, stemThick, stemLength);
            L.Actuator = new Rectangle(bodyX + hW / 2 - actW / 2, 0, actW, actH);

            int cavInset = Math.Max(3, pad - 2);
            L.Cavity = new Rectangle(bodyX + cavInset, bodyY + cavInset,
                hW - cavInset * 2, hH - cavInset * 2);

            L.TriX = L.Cavity.X + 2;
            L.TriY = L.Cavity.Y + 2;
            L.TriW = L.Cavity.Width - 4;
            L.TriH = L.Cavity.Height - 4;
            L.HubRadius = Math.Max(3, L.TriH / 5);
            L.HubCX = L.TriX + L.TriW / 2;
            L.HubCY = L.TriY + L.TriH / 2;

            L.TextSize = textSize;
            L.TextPos = new Point(bodyX + hW / 2 - textSize.Width / 2, bodyY + hH + 2);
        }
        else
        {
            int hW = bodyW + pad * 2;
            int hH = bodyH + pad * 2;

            int bodyX = 0;
            int bodyY = L.FlangeThick;
            L.Body = new Rectangle(bodyX, bodyY, hW, hH);

            L.TopFlange = new Rectangle(bodyX + hW / 4, 0, hW / 2, L.FlangeThick);
            L.BottomFlange = new Rectangle(bodyX + hW / 4, bodyY + hH, hW / 2, L.FlangeThick);

            L.Stem = new Rectangle(bodyX + hW, bodyY + hH / 2 - stemThick / 2, stemLength, stemThick);
            L.Actuator = new Rectangle(bodyX + hW + stemLength, bodyY + hH / 2 - actW / 2, actH, actW);

            int cavInset = Math.Max(3, pad - 2);
            L.Cavity = new Rectangle(bodyX + cavInset, bodyY + cavInset,
                hW - cavInset * 2, hH - cavInset * 2);

            L.TriX = L.Cavity.X + 2;
            L.TriY = L.Cavity.Y + 2;
            L.TriW = L.Cavity.Width - 4;
            L.TriH = L.Cavity.Height - 4;
            L.HubRadius = Math.Max(3, L.TriW / 5);
            L.HubCX = L.TriX + L.TriW / 2;
            L.HubCY = L.TriY + L.TriH / 2;

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
            return [new("top", new Point(cx, 0)),
                    new("bottom", new Point(cx, L.Body.Bottom + L.FlangeThick))];
        }
        else
        {
            int cy = L.Body.Y + L.Body.Height / 2;
            return [new("left", new Point(0, cy)),
                    new("right", new Point(L.Body.Right + L.FlangeThick, cy))];
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
            int totalW = L.FlangeThick + L.Body.Width + L.FlangeThick;
            int totalH = L.Body.Bottom + textSize.Height + (int)(5 * BaseScale);
            Size = new Size(totalW, totalH);
        }
        else
        {
            int totalW = L.Actuator.Right;
            int totalH = L.FlangeThick + L.Body.Height + L.FlangeThick;
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
        Color fillColor = _triangleColor;

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
        if (L.Body.Width > 20 && L.Body.Height > 20)
            DrawScrews(g, L.Body, 8); // 8 = master's Math.Min(8, Math.Max(6,(int)(4*SF))+2) at SF=1

        DrawCavity(g, L.Cavity, 3);

        if (_vert)
        {
            Point[] topTri = [
                new(L.TriX, L.TriY),
                new(L.TriX + L.TriW, L.TriY),
                new(L.TriX + L.TriW / 2, L.TriY + L.TriH / 2)
            ];
            Point[] botTri = [
                new(L.TriX, L.TriY + L.TriH),
                new(L.TriX + L.TriW / 2, L.TriY + L.TriH / 2),
                new(L.TriX + L.TriW, L.TriY + L.TriH)
            ];
            DrawGradientTriangle(g, topTri, fillColor);
            DrawGradientTriangle(g, botTri, fillColor);
        }
        else
        {
            Point[] leftTri = [
                new(L.TriX, L.TriY),
                new(L.TriX, L.TriY + L.TriH),
                new(L.TriX + L.TriW / 2, L.TriY + L.TriH / 2)
            ];
            Point[] rightTri = [
                new(L.TriX + L.TriW / 2, L.TriY + L.TriH / 2),
                new(L.TriX + L.TriW, L.TriY),
                new(L.TriX + L.TriW, L.TriY + L.TriH)
            ];
            DrawGradientTriangle(g, leftTri, fillColor);
            DrawGradientTriangle(g, rightTri, fillColor);
        }

        DrawCenterHub(g, L.HubCX, L.HubCY, L.HubRadius, fillColor);

        DrawStem(g, L.Stem);

        DrawActuator(g, L.Actuator);

        if (PulseIntensity > 0.01f)
        {
            var glowRect = new Rectangle(L.Body.X - 4, L.Body.Y - 4,
                L.Body.Width + 8, L.Body.Height + 8);
            DrawGlow(g, glowRect, PulseColor, PulseIntensity);
        }

        // Vertical valves rely on the DesignCanvas-rendered caption.
        if (!_vert)
            DrawNeonText(g, L.TextPos.X, L.TextPos.Y, fillColor);

        DrawAnchorOverlay(g);
        DrawPendingOverlay(g);
    }

    #endregion

    #region [Drawing Helpers]

    private static void DrawGradientTriangle(Graphics g, Point[] points, Color baseColor)
    {
        if (points.Length < 3) return;

        int minX = Math.Min(points[0].X, Math.Min(points[1].X, points[2].X));
        int minY = Math.Min(points[0].Y, Math.Min(points[1].Y, points[2].Y));
        int maxX = Math.Max(points[0].X, Math.Max(points[1].X, points[2].X));
        int maxY = Math.Max(points[0].Y, Math.Max(points[1].Y, points[2].Y));

        var gradientRect = new Rectangle(minX, minY, Math.Max(maxX - minX, 1), Math.Max(maxY - minY, 1));
        Color lightColor = VacuumTheme.LerpColor(baseColor, Color.White, 0.15f);
        using var brush = new LinearGradientBrush(gradientRect, lightColor, baseColor, LinearGradientMode.Vertical);
        g.FillPolygon(brush, points);

        using var pen = new Pen(Color.FromArgb(60, 0, 0, 0), 1f);
        g.DrawPolygon(pen, points);
    }

    private static void DrawCenterHub(Graphics g, int cx, int cy, int radius, Color stateColor)
    {
        var hubRect = new Rectangle(cx - radius, cy - radius, radius * 2, radius * 2);
        if (hubRect.Width <= 0 || hubRect.Height <= 0) return;

        using var metalBrush = VacuumTheme.CreateMetalGradient(hubRect);
        g.FillEllipse(metalBrush, hubRect);

        using var glowPen = new Pen(Color.FromArgb(60, stateColor), 1.5f);
        g.DrawEllipse(glowPen, hubRect);

        if (radius > 2)
        {
            var specRect = new Rectangle(cx - radius / 3, cy - radius / 2, radius, radius / 2);
            if (specRect.Width > 0 && specRect.Height > 0)
            {
                using var specBrush = new SolidBrush(Color.FromArgb(50, 255, 255, 255));
                g.FillEllipse(specBrush, specRect);
            }
        }
    }

    private static void DrawStem(Graphics g, Rectangle rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;
        using var brush = VacuumTheme.CreateMetalGradient(rect);
        g.FillRectangle(brush, rect);
        using var borderPen = new Pen(VacuumTheme.MetalEdge, 0.5f);
        g.DrawRectangle(borderPen, rect);
    }

    private static void DrawActuator(Graphics g, Rectangle rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;

        using var brush = VacuumTheme.CreateMetalGradient(rect);
        g.FillRectangle(brush, rect);
        using var borderPen = new Pen(VacuumTheme.MetalEdge, 1f);
        g.DrawRectangle(borderPen, rect);

        int portR = Math.Max(2, Math.Min(rect.Width, rect.Height) / 5);
        int portCX, portCY;
        if (rect.Height >= rect.Width)
        {
            portCX = rect.X + rect.Width / 2;
            portCY = rect.Y + portR + 2;
        }
        else
        {
            portCX = rect.X + portR + 2;
            portCY = rect.Y + rect.Height / 2;
        }
        var portRect = new Rectangle(portCX - portR, portCY - portR, portR * 2, portR * 2);
        using var portBrush = new SolidBrush(Color.FromArgb(40, 0, 0, 0));
        g.FillEllipse(portBrush, portRect);
        using var portPen = new Pen(VacuumTheme.MetalEdge, 0.5f);
        g.DrawEllipse(portPen, portRect);
    }

    private void DrawNeonText(Graphics g, int x, int y, Color glowColor)
    {
        if (_cachedFont == null || string.IsNullOrEmpty(ID)) return;

        if (IsDark && Connected)
        {
            var textSize = TextRenderer.MeasureText(ID, _cachedFont);
            using var glowBrush = new SolidBrush(Color.FromArgb(20, glowColor));
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
            ThemedMessageBox.Show(this, $"Valve {ID} is not connected!");
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
