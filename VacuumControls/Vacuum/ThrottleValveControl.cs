using System.ComponentModel;
using System.Drawing.Drawing2D;
using APS.Controls.Dialogs;
using APS.Controls.Theme;
using APS.Controls.Core;

namespace APS.Controls.Vacuum;

/// <summary>
/// Position 0% = disc perpendicular (closed), 100% = disc parallel (fully open).
/// Shows continuous position, not just open/closed.
/// </summary>
[ToolboxItem(false)]
public class ThrottleValveControl : ThemedVacuumControl
{
    private const int DefaultBodyW = 60;
    private const int DefaultBodyH = 50;
    private const int DefaultFontSize = 10;

    private Color _discColor;
    private float _position; // 0-100%
    private bool _vert;
    private float _scale = 1.0f;
    private Font? _idFont;
    private Font? _posFont;
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

    /// <summary>Throttle position 0-100%.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float Position
    {
        get => _position;
        set => UpdatePosition(value);
    }

    public override string? ExternalCaption => _vert ? ID : null;

    public ThrottleValveControl(string id)
    {
        ID = id;
        this.Name = GUIConst.ControlName(DeviceType.Valve, id);
        _discColor = IsDark ? Color.FromArgb(80, 80, 100) : Color.Gray;
        CalculateSize();
    }

    #region [State Management]

    private void UpdatePosition(float pos)
    {
        Connected = true;

        // The poll loop pushes the position every 200 ms even when nothing
        // changed. Only replay the one-shot colour transition + glow pulse when
        // the position actually moves (or on the first reading after connect);
        // otherwise the animation restarts every cycle and the disc shimmers
        // and never settles on a stable colour.
        float clamped = Math.Clamp(pos, 0f, 100f);
        if (_stateInitialized && clamped == _position)
            return;
        _stateInitialized = true;

        Color oldColor = _discColor;
        _position = clamped;

        Color newColor = _position switch
        {
            < 5f => IsDark ? VacuumTheme.GlowRed : Color.Red,
            < 50f => IsDark ? VacuumTheme.GlowYellow : Color.Orange,
            _ => IsDark ? VacuumTheme.GlowGreen : Color.LimeGreen,
        };

        _discColor = newColor;
        _animatingTransition = true;
        StartAnimation(oldColor, newColor, 100f);
        PulseGlow(newColor, 250);
        Invalidate();
    }

    protected override void OnConnectedChanged()
    {
        if (!Connected)
        {
            _discColor = IsDark ? Color.FromArgb(80, 80, 100) : Color.Gray;
            _position = 0f;
        }
        else
        {
            _discColor = _position switch
            {
                < 5f => IsDark ? VacuumTheme.GlowRed : Color.Red,
                < 50f => IsDark ? VacuumTheme.GlowYellow : Color.Orange,
                _ => IsDark ? VacuumTheme.GlowGreen : Color.LimeGreen,
            };
        }
    }

    protected override void OnAnimationTick()
    {
        if (_animatingTransition)
        {
            _discColor = AnimationColor;
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
        public Rectangle LeftFlange, RightFlange;
        public Rectangle TopFlange, BottomFlange;
        public int BoreCX, BoreCY;
        public Point TextPos;
        public Size TextSize;
        public Point PosTextPos;
        public Size PosTextSize;
    }

    private Layout ComputeLayout(Size idTextSize, Size posTextSize)
    {
        float sf = _scale * BaseScale;
        var L = new Layout();

        int bodyW = (int)Math.Round(DefaultBodyW * sf);
        int bodyH = (int)Math.Round(DefaultBodyH * sf);
        L.FlangeThick = Math.Max(5, (int)(5 * sf));
        L.BoreRadius = Math.Max(8, Math.Min(bodyW, bodyH) / 3);

        if (!_vert)
        {
            int bodyX = L.FlangeThick;
            int bodyY = 0;
            L.Body = new Rectangle(bodyX, bodyY, bodyW, bodyH);

            int cy = bodyY + bodyH / 2;
            L.LeftFlange = new Rectangle(0, cy - bodyH / 4, L.FlangeThick, bodyH / 2);
            L.RightFlange = new Rectangle(bodyX + bodyW, cy - bodyH / 4, L.FlangeThick, bodyH / 2);

            L.BoreCX = bodyX + bodyW / 2;
            L.BoreCY = cy;

            L.PosTextSize = posTextSize;
            L.PosTextPos = new Point(L.BoreCX - posTextSize.Width / 2,
                                      bodyY + 2);

            L.TextSize = idTextSize;
            L.TextPos = new Point(bodyX + bodyW / 2 - idTextSize.Width / 2,
                                  bodyY + bodyH + L.FlangeThick + 1);
        }
        else
        {
            int bodyX = L.FlangeThick;
            int bodyY = L.FlangeThick;
            int vBodyW = bodyH;
            int vBodyH = bodyW;
            L.Body = new Rectangle(bodyX, bodyY, vBodyW, vBodyH);

            int cx = bodyX + vBodyW / 2;
            L.TopFlange = new Rectangle(cx - vBodyW / 4, 0, vBodyW / 2, L.FlangeThick);
            L.BottomFlange = new Rectangle(cx - vBodyW / 4, bodyY + vBodyH, vBodyW / 2, L.FlangeThick);

            L.BoreCX = cx;
            L.BoreCY = bodyY + vBodyH / 2;

            L.PosTextSize = posTextSize;
            L.PosTextPos = new Point(bodyX + 2,
                                      L.BoreCY - posTextSize.Height / 2);

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
        var idSize = MeasureText(_idFont, ID ?? "");
        var posSize = MeasureText(_posFont, "100%");
        var L = ComputeLayout(idSize, posSize);

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
        EnsureFonts();
        var idSize = MeasureText(_idFont, ID ?? "");
        var posSize = MeasureText(_posFont, "100%");
        var L = ComputeLayout(idSize, posSize);

        if (!_vert)
        {
            int totalW = L.Body.Width + L.FlangeThick * 2;
            int totalH = L.Body.Bottom + L.FlangeThick + idSize.Height + (int)(3 * BaseScale);
            Size = new Size(totalW, totalH);
        }
        else
        {
            int totalW = L.Body.Right + L.FlangeThick;
            int totalH = L.Body.Height + L.FlangeThick * 2;
            Size = new Size(totalW, totalH);
        }
    }

    private void EnsureFonts()
    {
        // Integer point sizes computed exactly as master (truncation, cap in points),
        // then rendered as pixel-unit fonts (pt × 4/3) to stay DPI-neutral.
        // _posFont was fontSize - 1 in point space in master; keep that subtraction in points.
        int pt = (int)Math.Round(DefaultFontSize * _scale * BaseScale);
        if (pt > 11) pt = 11;
        if (pt < 1) pt = 1;

        if (_idFont == null || _cachedFontSize != pt)
        {
            _idFont?.Dispose();
            _posFont?.Dispose();
            _idFont = new Font("Arial", pt * 4f / 3f, FontStyle.Regular, GraphicsUnit.Pixel);
            _posFont = new Font("Consolas", Math.Max(1, pt - 1) * 4f / 3f, FontStyle.Bold, GraphicsUnit.Pixel);
            _cachedFontSize = pt;
        }
    }

    private static Size MeasureText(Font? font, string text)
    {
        if (font == null) return new Size(20, 12);
        return TextRenderer.MeasureText(text, font);
    }

    #endregion

    #region [Painting]

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        EnsureFonts();

        var idSize = MeasureText(_idFont, ID ?? "");
        var posSize = MeasureText(_posFont, FormatPosition());
        var L = ComputeLayout(idSize, posSize);

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

        DrawBore(g, L.BoreCX, L.BoreCY, L.BoreRadius);

        DrawDisc(g, L.BoreCX, L.BoreCY, L.BoreRadius);

        DrawShaftHub(g, L.BoreCX, L.BoreCY, L.BoreRadius);

        DrawPositionText(g, L.PosTextPos);

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
    }

    private static void DrawBore(Graphics g, int cx, int cy, int r)
    {
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

    private void DrawDisc(Graphics g, int cx, int cy, int boreR)
    {
        // Disc angle: 0% position → 90° (perpendicular, closed), 100% → 0° (parallel, open)
        // For horizontal flow, 90° means vertical line blocking the bore
        float angleDeg = 90f * (1f - _position / 100f);
        float angleRad = angleDeg * MathF.PI / 180f;

        float discR = boreR - 2;
        float cos = MathF.Cos(angleRad);
        float sin = MathF.Sin(angleRad);

        float x1, y1, x2, y2;
        if (!_vert)
        {
            x1 = cx - cos * discR;
            y1 = cy - sin * discR;
            x2 = cx + cos * discR;
            y2 = cy + sin * discR;
        }
        else
        {
            // Vertical flow: at 0% (closed) disc is horizontal;
            // at 100% (open) disc is vertical (parallel to flow).
            x1 = cx - sin * discR;
            y1 = cy - cos * discR;
            x2 = cx + sin * discR;
            y2 = cy + cos * discR;
        }

        float discThick = Math.Max(3f, boreR * 0.25f);
        using var discPen = new Pen(_discColor, discThick) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(discPen, x1, y1, x2, y2);

        using var edgePen = new Pen(Color.FromArgb(80, 255, 255, 255), 1f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(edgePen, x1, y1, x2, y2);
    }

    private void DrawShaftHub(Graphics g, int cx, int cy, int boreR)
    {
        int hubR = Math.Max(3, boreR / 4);
        var hubRect = new Rectangle(cx - hubR, cy - hubR, hubR * 2, hubR * 2);

        using var metalBrush = VacuumTheme.CreateMetalGradient(hubRect);
        g.FillEllipse(metalBrush, hubRect);

        using var glowPen = new Pen(Color.FromArgb(60, _discColor), 1.5f);
        g.DrawEllipse(glowPen, hubRect);

        if (hubR > 2)
        {
            var specRect = new Rectangle(cx - hubR / 3, cy - hubR / 2, hubR, hubR / 2);
            if (specRect.Width > 0 && specRect.Height > 0)
            {
                using var specBrush = new SolidBrush(Color.FromArgb(50, 255, 255, 255));
                g.FillEllipse(specBrush, specRect);
            }
        }
    }

    private void DrawPositionText(Graphics g, Point pos)
    {
        if (_posFont == null) return;

        string text = FormatPosition();
        Color textColor = IsDark
            ? (Connected ? _discColor : VacuumTheme.TextDim)
            : Color.Black;

        TextRenderer.DrawText(g, text, _posFont, pos, textColor);
    }

    private string FormatPosition()
    {
        return Connected ? $"{_position:F0}%" : "---";
    }

    private void DrawNeonText(Graphics g, int x, int y)
    {
        if (_idFont == null || string.IsNullOrEmpty(ID)) return;

        if (IsDark && Connected)
        {
            var textSize = TextRenderer.MeasureText(ID, _idFont);
            using var glowBrush = new SolidBrush(Color.FromArgb(20, _discColor));
            g.FillRectangle(glowBrush, x - 2, y - 1, textSize.Width + 4, textSize.Height + 2);
        }

        Color textColor = IsDark ? VacuumTheme.TextPrimary : Color.Black;
        TextRenderer.DrawText(g, ID, _idFont, new Point(x, y), textColor);
    }

    #endregion

    #region [Events]

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }

    /// <summary>Invoked on double-click to dispatch operator commands to the controller channel.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Action<string, ThrottleAction, float?>? SendHWMessage { get; set; }

    protected override void OnDoubleClick(EventArgs e)
    {
        if (!Connected)
        {
            ThemedMessageBox.Show(this, $"Throttle valve {ID} is not connected!");
            return;
        }

        using var dlg = new ThrottlePressureDialog(ID ?? "", _position);
        if (dlg.ShowDialog(FindForm()) == DialogResult.OK && dlg.Action != ThrottleAction.Cancel)
        {
            SendHWMessage?.Invoke(ID!, dlg.Action, dlg.PressureTorr);
        }

        base.OnDoubleClick(e);
    }

    #endregion

    #region [Dispose]

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _idFont?.Dispose();
            _idFont = null;
            _posFont?.Dispose();
            _posFont = null;
        }
        base.Dispose(disposing);
    }

    #endregion
}
