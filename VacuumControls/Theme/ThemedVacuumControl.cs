using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Linq;

namespace APS.Controls.Theme;

/// <summary>
/// Inherits Panel to support child control hosting.
/// </summary>
[ToolboxItem(false)]
public class ThemedVacuumControl : Panel
{
    private static readonly System.Windows.Forms.Timer s_animTimer = new() { Interval = 16 };
    private static readonly HashSet<ThemedVacuumControl> s_animating = [];

    static ThemedVacuumControl()
    {
        s_animTimer.Tick += (_, _) =>
        {
            var controls = s_animating.ToArray();
            foreach (var c in controls)
            {
                c.TickAnimation();
                if (!c._isAnimating)
                    s_animating.Remove(c);
            }
            if (s_animating.Count == 0)
                s_animTimer.Stop();
        };
    }

    private bool _isAnimating;
    private float _animProgress;
    private float _animDuration;
    private float _animElapsed;
    private Color _animFromColor;
    private Color _animToColor;
    private bool _pulseActive;
    private Color _pulseColor;
    private float _pulseDuration;
    private float _pulseElapsed;
    private bool _pulseLoop;

    public const int DefaultPendingTimeoutMs = 8000;
    private bool _pending;
    private bool _pendingTarget;
    private int _pendingElapsedMs;
    private int _pendingTimeoutMs = DefaultPendingTimeoutMs;

    /// <summary>True while a user-requested toggle is awaiting hardware confirmation.</summary>
    public bool IsPending => _pending;

    /// <summary>
    /// Arms the "processing" indication: a looping amber glow + dim overlay, held
    /// until the actual state reaches <paramref name="target"/> (see
    /// <see cref="ReportActualState"/>) or the watchdog elapses. Idempotent — a
    /// re-click restarts the clock.
    /// </summary>
    public void BeginPending(bool target, int timeoutMs = DefaultPendingTimeoutMs)
    {
        _pending = true;
        _pendingTarget = target;
        _pendingElapsedMs = 0;
        _pendingTimeoutMs = timeoutMs;
        PulseGlow(VacuumTheme.GlowAmber, 800, loop: true);
        Invalidate();
    }

    /// <summary>
    /// Called by derived controls from their state setter on a genuine state change.
    /// Clears the pending indication once the actual state matches the requested target.
    /// </summary>
    protected void ReportActualState(bool actual)
    {
        if (_pending && actual == _pendingTarget)
            EndPending(success: true);
    }

    /// <summary>Advances the pending watchdog; trips a failure (red flash) on timeout.
    /// Called from <see cref="TickAnimation"/> at the 16 ms tick.</summary>
    protected void AdvancePendingWatchdog(int dtMs)
    {
        if (!_pending) return;
        _pendingElapsedMs += dtMs;
        if (_pendingElapsedMs >= _pendingTimeoutMs)
            EndPending(success: false);
    }

    private void EndPending(bool success)
    {
        if (!_pending) return;
        _pending = false;
        _pendingElapsedMs = 0;

        // Only stop the pulse if OUR amber loop is still the one showing — a
        // successful toggle (or an alarm) may already have replaced it with its
        // own glow, which we must not clobber.
        bool amberStillShowing = _pulseActive && _pulseColor == VacuumTheme.GlowAmber;
        if (amberStillShowing)
            StopPulse();
        if (!success)
            PulseGlow(VacuumTheme.GlowRed, 600);   // brief "did not take" flash
        Invalidate();
    }

    /// <summary>
    /// Derived controls call this at the end of their OnPaint to dim the body
    /// while pending. The amber glow itself is drawn by the control's existing
    /// pulse-glow draw (PulseColor is amber while pending).
    /// </summary>
    protected void DrawPendingOverlay(Graphics g)
    {
        if (!_pending) return;
        using var dim = new SolidBrush(Color.FromArgb(90, 10, 10, 20));
        g.FillRectangle(dim, ClientRectangle);
    }

    protected float AnimationProgress => _animProgress;
    protected Color AnimationColor { get; private set; }
    protected float PulseIntensity { get; private set; }
    protected Color PulseColor => _pulseColor;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? ID { get; set; }
    public bool IsDark => VacuumTheme.IsDark;
    /// <remarks>Not used for schematic widget sizing — widgets are canvas-proportional
    /// via BaseScale; GDI+ handles DPI for point-size text.</remarks>
    public float ScaleFactor => DeviceDpi / 96f;

    private float _baseScale = 1.0f;
    /// <summary>
    /// Global scale factor pushed in from the hosting schematic so that
    /// components resize proportionally with the canvas. Default 1.0 when
    /// the control is used outside a VacuumSchematic (e.g. Builder designer).
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float BaseScale
    {
        get => _baseScale;
        set
        {
            if (!float.IsFinite(value) || value <= 0f) return;
            if (Math.Abs(_baseScale - value) < 0.001f) return;
            _baseScale = value;
            CalculateSize();
            Invalidate();
        }
    }

    private bool _connected;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Connected
    {
        get => _connected;
        set
        {
            if (_connected == value) return;
            _connected = value;
            if (!value && _pending) EndPending(false);
            OnConnectedChanged();
            Invalidate();
        }
    }

    public record AnchorDef(string Name, Point RelativePosition);

    private IReadOnlyList<AnchorDef>? _cachedAnchors;
    private Size _anchorCacheSize;

    protected virtual IReadOnlyList<AnchorDef> DefineAnchors() => [];

    public virtual Point GetAnchorPoint(string name)
    {
        var anchors = GetCachedAnchors();
        foreach (var a in anchors)
        {
            if (string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))
                return new Point(Location.X + a.RelativePosition.X, Location.Y + a.RelativePosition.Y);
        }
        return new Point(Location.X + Width / 2, Location.Y + Height / 2);
    }

    public virtual IEnumerable<string> GetAnchorNames()
    {
        return GetCachedAnchors().Select(a => a.Name).ToList();
    }

    /// <summary>
    /// Returns the control-relative point that should be placed at the percentage coordinate.
    /// Uses the midpoint of all anchors (the pipe connection center), falling back to
    /// bounding-box center when no anchors are defined.
    /// </summary>
    public Point GetAnchorMidpoint()
    {
        var anchors = GetCachedAnchors();
        if (anchors.Count == 0)
            return new Point(Width / 2, Height / 2);
        int sumX = 0, sumY = 0;
        foreach (var a in anchors)
        {
            sumX += a.RelativePosition.X;
            sumY += a.RelativePosition.Y;
        }
        return new Point(sumX / anchors.Count, sumY / anchors.Count);
    }

    private IReadOnlyList<AnchorDef> GetCachedAnchors()
    {
        if (_cachedAnchors == null || _anchorCacheSize != Size)
        {
            _cachedAnchors = DefineAnchors();
            _anchorCacheSize = Size;
        }
        return _cachedAnchors;
    }

    public ThemedVacuumControl()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        BackColor = VacuumTheme.IsDark ? VacuumTheme.BackgroundDark : Color.White;
    }

    protected virtual void CalculateSize() { }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        CalculateSize();
        Invalidate();
    }

    /// <summary>
    /// When true, the control paints its own anchor dots on top of its
    /// rendered body, into its double-buffered surface — so they survive
    /// the next child repaint (parent-side overlay drawing is erased by
    /// subsequent child paints because Windows skips parent WM_PAINT when
    /// the area is fully covered by opaque children).
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowAnchorOverlay { get; set; }

    /// <summary>
    /// Optional ID label the schematic canvas renders below the child's frame
    /// in parent space, for controls whose orientation has no room for a caption
    /// inside the bounding box (e.g. vertical valves, where extending the frame
    /// downward would put the bottom anchor mid-frame and hide the pipe below).
    /// Null: the control renders its own caption.
    /// </summary>
    public virtual string? ExternalCaption => null;

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        base.OnPaint(e);
    }

    /// <summary>
    /// Called by derived controls at the END of their OnPaint to overlay
    /// anchor dots when in design-canvas mode. Drawing into the child's
    /// own buffer means the dots persist across animations and state changes.
    /// </summary>
    protected void DrawAnchorOverlay(Graphics g)
    {
        if (!ShowAnchorOverlay) return;
        var anchors = GetCachedAnchors();
        if (anchors.Count == 0) return;

        const int radius = 4;
        using var fill = new SolidBrush(Color.FromArgb(180, 0, 180, 255));
        using var border = new Pen(Color.FromArgb(220, 255, 255, 255), 1.2f);
        foreach (var a in anchors)
        {
            float x = a.RelativePosition.X;
            float y = a.RelativePosition.Y;
            g.FillEllipse(fill, x - radius, y - radius, radius * 2, radius * 2);
            g.DrawEllipse(border, x - radius, y - radius, radius * 2, radius * 2);
        }
    }

    protected void StartAnimation(Color from, Color to, float durationMs = 200f)
    {
        _animFromColor = from;
        _animToColor = to;
        _animDuration = durationMs;
        _animElapsed = 0f;
        _animProgress = 0f;
        AnimationColor = from;
        RegisterAnimation();
    }

    protected void PulseGlow(Color glowColor, int durationMs = 400, bool loop = false)
    {
        _pulseColor = glowColor;
        _pulseDuration = durationMs;
        _pulseElapsed = 0f;
        _pulseLoop = loop;
        _pulseActive = true;
        PulseIntensity = 0f;
        RegisterAnimation();
    }

    protected void StopPulse()
    {
        _pulseActive = false;
        PulseIntensity = 0f;
    }

    private void RegisterAnimation()
    {
        _isAnimating = true;
        s_animating.Add(this);
        if (!s_animTimer.Enabled)
            s_animTimer.Start();
    }

    private void TickAnimation()
    {
        AdvancePendingWatchdog(16);
        bool stillAnimating = false;
        const float dt = 16f;

        if (_animElapsed < _animDuration)
        {
            _animElapsed += dt;
            float t = Math.Min(_animElapsed / _animDuration, 1f);
            float eased = 1f - (1f - t) * (1f - t);
            _animProgress = eased;
            AnimationColor = VacuumTheme.LerpColor(_animFromColor, _animToColor, eased);
            stillAnimating = true;
        }
        else
        {
            AnimationColor = _animToColor;
        }

        if (_pulseActive)
        {
            _pulseElapsed += dt;
            float t = _pulseElapsed / _pulseDuration;
            if (t >= 1f)
            {
                if (_pulseLoop)
                {
                    _pulseElapsed = 0f;
                    PulseIntensity = 0f;
                }
                else
                {
                    _pulseActive = false;
                    PulseIntensity = 0f;
                }
            }
            else
            {
                PulseIntensity = t < 0.5f ? t * 2f : (1f - t) * 2f;
            }
            if (_pulseActive) stillAnimating = true;
        }

        _isAnimating = stillAnimating || _pending;
        OnAnimationTick();
        Invalidate();
    }

    protected virtual void OnAnimationTick() { }
    protected virtual void OnConnectedChanged() { }

    public static void DrawMetalHousing(Graphics g, Rectangle bounds, int cornerRadius = 5)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        using var metalBrush = VacuumTheme.CreateMetalGradient(bounds);
        using var path = CreateRoundedRect(bounds, cornerRadius);
        g.FillPath(metalBrush, path);

        int highlightH = Math.Min(10, bounds.Height / 4);
        int highlightW = bounds.Width - 2;
        if (highlightH > 0 && highlightW > 0)
        {
            var highlightRect = new Rectangle(bounds.X + 1, bounds.Y + 1, highlightW, highlightH);
            using var edgeBrush = VacuumTheme.CreateEdgeHighlight(highlightRect);
            using var highlightPath = CreateRoundedRect(highlightRect, cornerRadius);
            g.FillPath(edgeBrush, highlightPath);
        }

        using var borderPen = new Pen(VacuumTheme.MetalEdge, 1f);
        g.DrawPath(borderPen, path);
    }

    public static void DrawScrews(Graphics g, Rectangle bounds, int inset = 8)
    {
        float r = Math.Clamp(bounds.Width * 0.028f, 1.4f, 2.5f);
        float c = Math.Clamp(inset, r + 1f, Math.Max(r + 1f, Math.Min(bounds.Width, bounds.Height) * 0.16f));
        PointF[] positions =
        [
            new(bounds.X + c, bounds.Y + c),
            new(bounds.Right - c, bounds.Y + c),
            new(bounds.X + c, bounds.Bottom - c),
            new(bounds.Right - c, bounds.Bottom - c)
        ];
        using var fillBrush = new SolidBrush(VacuumTheme.ScrewFill);
        using var borderPen = new Pen(VacuumTheme.ScrewBorder, 0.5f);
        foreach (var p in positions)
        {
            g.FillEllipse(fillBrush, p.X - r, p.Y - r, r * 2, r * 2);
            g.DrawEllipse(borderPen, p.X - r, p.Y - r, r * 2, r * 2);
        }
    }

    public static void DrawCavity(Graphics g, Rectangle bounds, int cornerRadius = 3)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        using var path = CreateRoundedRect(bounds, cornerRadius);
        using var cavityBrush = VacuumTheme.CreateCavityGradient(bounds);
        g.FillPath(cavityBrush, path);
        using var borderPen = new Pen(Color.FromArgb(26, 26, 46), 1f);
        g.DrawPath(borderPen, path);
    }

    public static void DrawGlow(Graphics g, Rectangle area, Color glowColor, float intensity)
    {
        if (intensity <= 0.01f) return;
        using var brush = VacuumTheme.CreateGlowBrush(glowColor, intensity);
        g.FillEllipse(brush, area);
    }

    public static void DrawLcdPanel(Graphics g, Rectangle bounds, string text, string? unit = null, string? label = null)
    {
        var bezelRect = new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        using (var bezelBrush = new SolidBrush(Color.FromArgb(17, 17, 17)))
            g.FillRectangle(bezelBrush, bezelRect);
        using (var bezelPen = new Pen(VacuumTheme.BorderSecondary, 1.5f))
            g.DrawRectangle(bezelPen, bezelRect);

        var screenRect = new Rectangle(bounds.X + 3, bounds.Y + 3, bounds.Width - 6, bounds.Height - 6);
        using (var lcdBrush = VacuumTheme.CreateLcdBackground(screenRect))
            g.FillRectangle(lcdBrush, screenRect);

        if (label != null)
        {
            using var labelFont = new Font("Consolas", 7f);
            using var labelBrush = new SolidBrush(Color.FromArgb(0, 170, 102));
            var labelSize = g.MeasureString(label, labelFont);
            g.DrawString(label, labelFont, labelBrush,
                screenRect.X + (screenRect.Width - labelSize.Width) / 2,
                screenRect.Y + 2);
        }

        using var valueFont = new Font("Consolas", 13f, FontStyle.Bold);
        using var valueBrush = new SolidBrush(VacuumTheme.LcdForeground);
        var valueSize = g.MeasureString(text, valueFont);
        float valueY = label != null ? screenRect.Y + 14 : screenRect.Y + (screenRect.Height - valueSize.Height) / 2;
        g.DrawString(text, valueFont, valueBrush,
            screenRect.X + (screenRect.Width - valueSize.Width) / 2, valueY);

        if (unit != null)
        {
            using var unitFont = new Font("Consolas", 7f);
            using var unitBrush = new SolidBrush(VacuumTheme.TextDim);
            g.DrawString(unit, unitFont, unitBrush,
                screenRect.Right - g.MeasureString(unit, unitFont).Width - 2,
                valueY + valueSize.Height - 10);
        }
    }

    public static void DrawLedIndicator(Graphics g, Rectangle bounds, Color ledColor, bool isOn, float glowIntensity = 0f)
    {
        using (var recessBrush = new SolidBrush(Color.FromArgb(26, 26, 46)))
            g.FillRectangle(recessBrush, bounds);
        using (var recessPen = new Pen(Color.FromArgb(51, 51, 51), 1.5f))
            g.DrawRectangle(recessPen, bounds);

        var ledRect = new Rectangle(bounds.X + 2, bounds.Y + 2, bounds.Width - 4, bounds.Height - 4);
        Color fillColor = isOn ? ledColor : Color.FromArgb(ledColor.R / 6, ledColor.G / 6, ledColor.B / 6);
        using (var ledBrush = new SolidBrush(fillColor))
            g.FillRectangle(ledBrush, ledRect);

        if (isOn || glowIntensity > 0)
        {
            float intensity = glowIntensity > 0 ? glowIntensity : 0.5f;
            var glowRect = new Rectangle(bounds.X - 3, bounds.Y - 3, bounds.Width + 6, bounds.Height + 6);
            DrawGlow(g, glowRect, ledColor, intensity);
        }

        if (isOn)
        {
            var specRect = new Rectangle(ledRect.X + 1, ledRect.Y + 1, ledRect.Width / 3, ledRect.Height / 3);
            using var specBrush = new SolidBrush(Color.FromArgb(60, 255, 255, 255));
            g.FillRectangle(specBrush, specRect);
        }
    }

    public static GraphicsPath CreateRoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        if (bounds.Width <= 0 || bounds.Height <= 0) return path;   // nothing to draw; avoids AddArc on d<=0
        int d = radius * 2;
        if (d > bounds.Width) d = bounds.Width;
        if (d > bounds.Height) d = bounds.Height;

        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            s_animating.Remove(this);
        }
        base.Dispose(disposing);
    }
}
