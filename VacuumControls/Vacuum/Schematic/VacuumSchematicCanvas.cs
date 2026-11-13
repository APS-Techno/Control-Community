using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using APS.Controls.Theme;
using APS.Controls.Vacuum.Rendering;
using APS.Controls.XML.GUI;

namespace APS.Controls.Vacuum.Schematic;

/// <summary>
/// Shared base for the vacuum schematic canvas used by Builder (editor) and
/// Control (runtime). Subclasses extend via the OnPaint* and Get* virtuals.
/// </summary>
public class VacuumSchematicCanvas : Panel
{
    private const int WM_PAINT = 0x000F;
    private const float ReferenceCanvasWidth = 2200f;

    /// <summary>
    /// Upper bound for <see cref="CanvasScale"/>. The schematic is authored at the
    /// 2200 px reference width (CanvasScale == 1.0); on wider canvases (e.g. 4K)
    /// widgets, pipes, and chamber labels grow proportionally up to this factor so
    /// the scheme fills the screen instead of staying FHD-sized. The cap only bounds
    /// extreme ultrawide layouts — a typical 4K panel lands around 1.4–1.8×.
    /// </summary>
    private const float MaxCanvasScale = 2.5f;
    private const float MinSymbolScale = 0.5f;
    private const float MaxSymbolScale = 1.5f;
    private const float MinZoom = 1.0f;
    private const float MaxZoom = 2.5f;
    private const float WheelZoomStep = 0.15f;
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int WM_MBUTTONDOWN = 0x0207;

    private float _aspectRatio = 16f / 9f;
    private XML_Schematic? _schematic;
    private readonly List<(Control Control, float Left, float Top)> _children = [];
    private float _symbolScale = 1.0f;
    private float _zoomFactor = 1.0f;
    private Point _panOffset;
    private bool _panning;
    private Point _panLastScreen;
    private Cursor? _cursorBeforePan;
    private InputFilter? _inputFilter;

    public VacuumSchematicCanvas()
    {
        DoubleBuffered = true;
        BackColor = VacuumTheme.BackgroundDark;
        AutoScroll = false;
    }

    /// <summary>Pixel rect of the letterboxed canvas inside ClientRectangle.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Rectangle CanvasArea { get; private set; }

    /// <summary>
    /// Scale factor of the CanvasArea width against the reference 2200 px design
    /// width, clamped to [0.1, <see cref="MaxCanvasScale"/>]. 1.0 at the reference
    /// width; below it the scheme shrinks, above it (4K and wider) it grows.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float CanvasScale { get; private set; } = 1.0f;

    /// <summary>Width:height ratio for the letterbox canvas. Defaults to 16:9. Invalid (≤0 or non-finite) values are ignored.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float AspectRatio
    {
        get => _aspectRatio;
        set
        {
            if (value <= 0f || !float.IsFinite(value)) return;
            _aspectRatio = value;
            RecalculateCanvasArea();
            Invalidate();
        }
    }

    /// <summary>Schematic model rendered by this canvas. Setting invalidates the control.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public XML_Schematic? Schematic
    {
        get => _schematic;
        set { _schematic = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float SymbolScale
    {
        get => _symbolScale;
        set
        {
            if (!float.IsFinite(value)) return;
            float v = Math.Clamp(value, MinSymbolScale, MaxSymbolScale);
            if (Math.Abs(v - _symbolScale) < 0.0001f) return;
            _symbolScale = v;
            RepositionChildren();
            Invalidate();
            ScaleOrZoomChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public float EffectiveSymbolScale => Math.Clamp(CanvasScale * _symbolScale, 0.1f, MaxCanvasScale);

    public float ZoomFactor => _zoomFactor;

    public Point PanOffset => _panOffset;

    /// <summary>Enables wheel zoom and drag panning from the mouse. Programmatic zoom works either way.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool InteractiveZoom { get; set; }

    public event EventHandler? ScaleOrZoomChanged;

    public void SetZoom(float zoom, Point? anchorClientPoint = null)
    {
        if (!float.IsFinite(zoom)) return;
        float z = Math.Clamp(zoom, MinZoom, MaxZoom);
        if (Math.Abs(z - _zoomFactor) < 0.0001f) return;

        int w = ClientSize.Width, h = ClientSize.Height;
        if (w > 0 && h > 0 && CanvasArea.Width > 0 && CanvasArea.Height > 0)
        {
            var anchor = anchorClientPoint ?? new Point(w / 2, h / 2);
            var (pctX, pctY) = PixelToPercent(anchor.X, anchor.Y);
            var (fitW, fitH) = FitSize(w, h);
            int canvasW = (int)Math.Round(fitW * z), canvasH = (int)Math.Round(fitH * z);
            _panOffset = new Point(
                (int)Math.Round(anchor.X - pctX / 100f * canvasW - (w - canvasW) / 2),
                (int)Math.Round(anchor.Y - pctY / 100f * canvasH - (h - canvasH) / 2));
        }
        _zoomFactor = z;
        ApplyView();
        ScaleOrZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ResetZoom()
    {
        EndPan();
        _zoomFactor = 1.0f;
        _panOffset = Point.Empty;
        ApplyView();
        ScaleOrZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyView()
    {
        RecalculateCanvasArea();
        RepositionChildren();
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        RecalculateCanvasArea();
        RepositionChildren();
        Invalidate();
    }

    private (int W, int H) FitSize(int w, int h) =>
        (float)w / h > _aspectRatio
            ? ((int)(h * _aspectRatio), h)
            : (w, (int)(w / _aspectRatio));

    private static int PlaceAxis(int client, int canvas, int pan) =>
        canvas <= client
            ? (client - canvas) / 2
            : Math.Clamp((client - canvas) / 2 + pan, client - canvas, 0);

    private void RecalculateCanvasArea()
    {
        int w = ClientSize.Width, h = ClientSize.Height;
        if (w <= 0 || h <= 0)
        {
            CanvasArea = new Rectangle(0, 0, w, h);
            CanvasScale = 1.0f;
            return;
        }

        var (fitW, fitH) = FitSize(w, h);
        int canvasW = (int)Math.Round(fitW * _zoomFactor);
        int canvasH = (int)Math.Round(fitH * _zoomFactor);
        int x = PlaceAxis(w, canvasW, _panOffset.X);
        int y = PlaceAxis(h, canvasH, _panOffset.Y);
        _panOffset = new Point(x - (w - canvasW) / 2, y - (h - canvasH) / 2);
        CanvasArea = new Rectangle(x, y, canvasW, canvasH);
        CanvasScale = Math.Clamp(CanvasArea.Width / ReferenceCanvasWidth, 0.1f, MaxCanvasScale);
    }

    public Point PercentToPixel(float pctX, float pctY) =>
        new(CanvasArea.X + (int)(pctX / 100f * CanvasArea.Width),
            CanvasArea.Y + (int)(pctY / 100f * CanvasArea.Height));

    public (float pctX, float pctY) PixelToPercent(int x, int y) =>
        ((x - CanvasArea.X) / (float)CanvasArea.Width * 100f,
         (y - CanvasArea.Y) / (float)CanvasArea.Height * 100f);

    public void AddComponent(Control ctrl, float leftPct, float topPct)
    {
        if (ctrl is ThemedVacuumControl tvc) tvc.BaseScale = EffectiveSymbolScale;
        _children.Add((ctrl, leftPct, topPct));
        ctrl.Location = GetChildLocation(ctrl, leftPct, topPct);
        Controls.Add(ctrl);
    }

    public void ClearComponents()
    {
        _children.Clear();
        Controls.Clear();
    }

    /// <summary>
    /// Removes a component control from both the <c>_children</c> resize registry
    /// and the WinForms <see cref="Control.Controls"/> collection.
    /// </summary>
    public void RemoveComponent(Control ctrl)
    {
        _children.RemoveAll(t => t.Control == ctrl);
        Controls.Remove(ctrl);
    }

    /// <summary>
    /// Resolves a pipe-endpoint specification to a canvas pixel point.
    /// Accepts two forms:
    ///   @x,y  — percent coordinates relative to the canvas area (e.g. "@25,50")
    ///   id:anchor — named anchor on a child ThemedVacuumControl (e.g. "V1:p")
    /// Returns null for null/empty input, malformed specs, or unknown ids.
    /// </summary>
    public virtual PointF? ResolveEndpoint(string? spec)
    {
        if (string.IsNullOrEmpty(spec)) return null;

        if (spec.StartsWith('@'))
        {
            var coords = spec[1..].Split(',');
            if (coords.Length == 2
                && float.TryParse(coords[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float fx)
                && float.TryParse(coords[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float fy))
            {
                var pt = PercentToPixel(fx, fy);
                return new PointF(pt.X, pt.Y);
            }
            return null;
        }

        var parts = spec.Split(':');
        if (parts.Length != 2) return null;
        string id = parts[0], anchor = parts[1];

        foreach (Control child in Controls)
        {
            if (child is ThemedVacuumControl tvc &&
                string.Equals(tvc.ID, id, StringComparison.OrdinalIgnoreCase))
            {
                var pt = tvc.GetAnchorPoint(anchor);
                return new PointF(pt.X, pt.Y);
            }
        }
        return null;
    }

    private Point GetChildLocation(Control ctrl, float leftPct, float topPct)
    {
        int cx = ctrl.Width / 2, cy = ctrl.Height / 2;
        if (ctrl is ThemedVacuumControl tvc)
        {
            var mid = tvc.GetAnchorMidpoint();
            cx = mid.X;
            cy = mid.Y;
        }
        return new Point(
            CanvasArea.X + (int)(leftPct / 100f * CanvasArea.Width) - cx,
            CanvasArea.Y + (int)(topPct / 100f * CanvasArea.Height) - cy);
    }

    private void RepositionChildren()
    {
        foreach (var (ctrl, left, top) in _children)
        {
            if (ctrl is ThemedVacuumControl tvc) tvc.BaseScale = EffectiveSymbolScale;
            ctrl.Location = GetChildLocation(ctrl, left, top);
        }
    }

    private bool CanPan => InteractiveZoom && _zoomFactor > MinZoom + 0.001f;

    private bool IsSchematicTarget(Control? c)
    {
        for (; c != null; c = c.Parent)
        {
            if (c == this) return true;
            if (c.Parent == this) return _children.Exists(t => t.Control == c);
        }
        return false;
    }

    private bool IsSchematicPoint(Point screen) =>
        IsHandleCreated && Visible && IsSchematicTarget(FromChildHandle(WindowFromPoint(screen)));

    private void BeginPan(Point screen)
    {
        if (_panning) return;
        _panning = true;
        _panLastScreen = screen;
        _cursorBeforePan = Cursor;
        Cursor = Cursors.SizeAll;
        Capture = true;
    }

    private void UpdatePan(Point screen)
    {
        var before = CanvasArea;
        _panOffset = new Point(
            _panOffset.X + screen.X - _panLastScreen.X,
            _panOffset.Y + screen.Y - _panLastScreen.Y);
        _panLastScreen = screen;
        RecalculateCanvasArea();
        if (CanvasArea == before) return;
        RepositionChildren();
        Invalidate();
    }

    private void EndPan()
    {
        if (!_panning) return;
        _panning = false;
        Cursor = _cursorBeforePan ?? Cursors.Default;
        _cursorBeforePan = null;
        Capture = false;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && CanPan) BeginPan(PointToScreen(e.Location));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_panning) UpdatePan(PointToScreen(e.Location));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button is MouseButtons.Left or MouseButtons.Middle) EndPan();
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture) EndPan();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (_inputFilter != null) return;
        _inputFilter = new InputFilter(this);
        Application.AddMessageFilter(_inputFilter);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (_inputFilter != null)
        {
            Application.RemoveMessageFilter(_inputFilter);
            _inputFilter = null;
        }
        base.OnHandleDestroyed(e);
    }

    private bool HandleWheel(Point screen, int delta)
    {
        if (!InteractiveZoom || !IsSchematicPoint(screen)) return false;
        SetZoom(_zoomFactor + WheelZoomStep * delta / 120f, PointToClient(screen));
        return true;
    }

    private bool HandleMiddleDown()
    {
        var screen = MousePosition;
        if (!CanPan || !IsSchematicPoint(screen)) return false;
        BeginPan(screen);
        return true;
    }

    /// <summary>
    /// WinForms sends WM_MOUSEWHEEL to the focused control and button-down to the hovered
    /// widget, so the canvas would never see either over its children; the filter routes
    /// both by the window under the cursor and leaves overlays (events log, HUD) alone.
    /// </summary>
    private sealed class InputFilter(VacuumSchematicCanvas owner) : IMessageFilter
    {
        public bool PreFilterMessage(ref Message m)
        {
            if (owner.IsDisposed) return false;
            if (m.Msg == WM_MOUSEWHEEL)
            {
                long lp = m.LParam.ToInt64();
                var screen = new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
                int delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
                return owner.HandleWheel(screen, delta);
            }
            return m.Msg == WM_MBUTTONDOWN && owner.HandleMiddleDown();
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point point);

    protected static readonly Dictionary<string, Color> GasColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ar"]      = Color.FromArgb(0, 200, 255),
        ["N2"]      = Color.FromArgb(0, 255, 100),
        ["vacuum"]  = Color.FromArgb(255, 180, 50),
        ["mixed"]   = Color.FromArgb(200, 200, 255),
        ["vent"]    = Color.FromArgb(255, 255, 150),
    };

    protected virtual Color? GetPipeOverlayColor(XML_Pipe pipe)
    {
        if (pipe.Gas == null) return null;
        return GasColors.TryGetValue(pipe.Gas, out var c) ? c : VacuumTheme.GlowBlue;
    }

    /// <summary>
    /// Builds the polyline (start → bends → end) for a pipe in canvas pixel coords.
    /// Endpoints resolve via <see cref="ResolveEndpoint"/>; bend waypoints are parsed
    /// from the "x,y x,y" percent string. Used by both the render path here and the
    /// editor's hit-test path so the two can never diverge.
    /// </summary>
    protected List<PointF> BuildPipePoints(XML_Pipe pipe)
    {
        var pts = new List<PointF>();

        var startPt = ResolveEndpoint(pipe.From);
        if (startPt != null) pts.Add(startPt.Value);

        if (!string.IsNullOrEmpty(pipe.Bends))
        {
            foreach (var pair in pipe.Bends.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var coords = pair.Split(',');
                if (coords.Length == 2 &&
                    float.TryParse(coords[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float bx) &&
                    float.TryParse(coords[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float by))
                {
                    var p = PercentToPixel(bx, by);
                    pts.Add(new PointF(p.X, p.Y));
                }
            }
        }

        var endPt = ResolveEndpoint(pipe.To);
        if (endPt != null) pts.Add(endPt.Value);

        return pts;
    }

    private void DrawPipes(Graphics g)
    {
        var items = _schematic?.Pipes?.Items;
        if (items == null) return;

        float s = GeometryScale;

        foreach (var pipe in items)
        {
            var pts = BuildPipePoints(pipe);
            if (pts.Count < 2) continue;

            var overlay = GetPipeOverlayColor(pipe);
            PipeRenderer.DrawPipe(
                g, pts,
                thickness: 8f * s,
                highlightOffset: 2.4f * s,
                highlightThickness: 1.5f * s,
                overlayColor: overlay,
                overlayStyle: new PipeOverlayStyle(
                    GlowThickness: 7f * s, GlowAlpha: 60, StripeThickness: 2.5f * s, StripeAlpha: 255));
        }
    }

    protected virtual Color GetChamberCavityTint(XML_Chamber chamber, Color baseColor)
        => baseColor;

    private void DrawChambers(Graphics g)
    {
        var items = _schematic?.Chambers?.Items;
        if (items == null) return;

        float s = GeometryScale;

        foreach (var ch in items)
        {
            if (ch.Width <= 0 || ch.Height <= 0) continue;

            float ox = CanvasArea.X + ch.Left / 100f * CanvasArea.Width;
            float oy = CanvasArea.Y + ch.Top / 100f * CanvasArea.Height;
            float ow = ch.Width / 100f * CanvasArea.Width;
            float oh = ch.Height / 100f * CanvasArea.Height;
            float wallPx = ch.Wall / 100f * CanvasArea.Width;

            // The top edge doubles as a nameplate: ~2× the side walls, with the caption
            // seated in it so the label does not eat the chamber interior (cavity).
            using var labelFont = new Font("Arial", 9f * s, FontStyle.Bold);
            float topWallPx = wallPx * 2f;
            if (!string.IsNullOrEmpty(ch.Label))
            {
                float labelH = g.MeasureString(ch.Label, labelFont).Height;
                topWallPx = Math.Max(topWallPx, labelH + 4f); // never thinner than the caption needs
            }

            var outerRect = new RectangleF(ox, oy, ow, oh);
            var innerRect = new RectangleF(
                ox + wallPx, oy + topWallPx,
                ow - 2 * wallPx, oh - wallPx - topWallPx);

            var outerInt = Rectangle.Round(outerRect);
            if (outerInt.Width > 0 && outerInt.Height > 0)
            {
                using var metalBrush = VacuumTheme.CreateMetalGradient(outerInt);
                g.FillRectangle(metalBrush, outerRect);
            }
            using (var outerPen = new Pen(VacuumTheme.MetalEdge, 1.0f))
                g.DrawRectangle(outerPen, outerRect.X, outerRect.Y, outerRect.Width, outerRect.Height);

            if (innerRect.Width > 0 && innerRect.Height > 0)
            {
                Color cavity = GetChamberCavityTint(ch, VacuumTheme.CavityDark);
                using (var brush = new SolidBrush(cavity))
                    g.FillRectangle(brush, innerRect);
                using (var innerPen = new Pen(Color.FromArgb(100, VacuumTheme.MetalDark), 0.8f))
                    g.DrawRectangle(innerPen, innerRect.X, innerRect.Y, innerRect.Width, innerRect.Height);
            }

            if (!string.IsNullOrEmpty(ch.Label))
            {
                using var brush = new SolidBrush(VacuumTheme.TextPrimary);
                var size = g.MeasureString(ch.Label, labelFont);
                float tx = ox + (ow - size.Width) / 2f;
                float ty = oy + (topWallPx - size.Height) / 2f;
                g.DrawString(ch.Label, labelFont, brush, tx, ty);
            }
        }
    }

    /// <summary>Pipe and chamber-caption scale; the floor keeps 8 px pipes and 9 pt captions legible on small canvases.</summary>
    private float GeometryScale => Math.Clamp(EffectiveSymbolScale, 0.55f, MaxCanvasScale);

    protected virtual void OnPaintCanvasBackground(Graphics g) { }
    protected virtual void OnPaintAdornments(Graphics g) { }
    protected virtual void OnPaintOverChildren(Graphics g) { }

    /// <summary>
    /// Font for <see cref="ThemedVacuumControl.ExternalCaption"/> labels. Uses the
    /// same integer-point-then-pixel-unit formula as the widgets' own captions so an
    /// external caption matches the in-frame labels of its neighbours: 8 pt at
    /// <see cref="EffectiveSymbolScale"/> 1.0, growing on 4K or zoom and shrinking on
    /// small canvases or a reduced <see cref="SymbolScale"/>, capped at 11 pt like every other widget label.
    /// </summary>
    protected virtual Font CreateCaptionFont()
    {
        int pt = Math.Clamp((int)Math.Round(8 * EffectiveSymbolScale), 5, 11);
        return new Font("Arial", pt * 4f / 3f, FontStyle.Regular, GraphicsUnit.Pixel);
    }

    /// <summary>
    /// Draws the ID labels of children that suppress their own in-frame caption
    /// (vertical valves — see <see cref="ThemedVacuumControl.ExternalCaption"/>).
    /// Runs for every host, so Builder's designer and Control's runtime scheme
    /// label these widgets identically.
    /// </summary>
    protected void DrawExternalCaptions(Graphics g)
    {
        using var font = CreateCaptionFont();
        using var brush = new SolidBrush(VacuumTheme.TextPrimary);
        foreach (Control child in Controls)
        {
            if (child is not ThemedVacuumControl tvc) continue;
            string? caption = tvc.ExternalCaption;
            if (string.IsNullOrEmpty(caption)) continue;
            var size = TextRenderer.MeasureText(caption, font);
            // Place caption below frame. Right-align to child.Right when it
            // fits, but never let the left edge cross the bottom-anchor x
            // (where the pipe drops straight down). Long IDs are allowed to
            // overflow the child's right edge slightly rather than sitting
            // on top of the pipe.
            int pipeClearX = child.Left + 8;
            foreach (var name in tvc.GetAnchorNames())
            {
                if (string.Equals(name, "bottom", StringComparison.OrdinalIgnoreCase))
                {
                    var pt = tvc.GetAnchorPoint("bottom");
                    pipeClearX = pt.X + 6;
                    break;
                }
            }
            int x = Math.Max(child.Right - size.Width, pipeClearX);
            int y = child.Bottom + 2;
            g.DrawString(caption, font, brush, x, y);
        }
    }

    protected sealed override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var brush = new SolidBrush(BackColor))
            g.FillRectangle(brush, ClientRectangle);
        OnPaintCanvasBackground(g);
        DrawChambers(g);
        DrawPipes(g);
        OnPaintAdornments(g);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == WM_PAINT)
        {
            using var g = Graphics.FromHwnd(Handle);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            DrawExternalCaptions(g);
            OnPaintOverChildren(g);
        }
    }
}
