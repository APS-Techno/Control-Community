using System.Drawing.Drawing2D;
using APS.SystemControl.XML;
using APS.SystemControl.XML.GUI;
using APS.Controls.XML.GUI;

namespace APS.SystemControl.GUIControls.General;

/// <summary>
/// Panels snap to a percentage-based grid and persist their positions to XML.
/// </summary>
public class PanelDragManager : IDisposable
{
    private const float GridStep = 0.25f; // percent of parent

    private readonly Control _parent;
    private readonly XML_Panels _xmlPanels;
    private readonly string _configFilePath;
    private readonly List<ManagedPanel> _panels = [];

    public bool IsEditMode { get; private set; }

    public PanelDragManager(Control parent, XML_Panels xmlPanels, string configFilePath)
    {
        _parent = parent;
        _xmlPanels = xmlPanels;
        _configFilePath = configFilePath;
        _parent.Resize += OnParentResize;
    }

    private void OnParentResize(object? sender, EventArgs e) => RepositionAllPanels();

    /// <summary>
    /// Idempotent because delegate -= is a no-op when the handler was never
    /// attached (or already detached).
    /// </summary>
    public void Dispose()
    {
        if (IsEditMode) DetachHandlers();
        _parent.Resize -= OnParentResize;
    }

    public void Register(Control panel, XML_Panel xmlPanel)
    {
        _panels.Add(new ManagedPanel
        {
            Control = panel,
            XmlPanel = xmlPanel
        });
    }

    private static float PixelToPercent(int pixel, int parentSize)
    {
        if (parentSize <= 0) return 0f;
        return pixel / (float)parentSize * 100f;
    }

    private static int PercentToPixel(float percent, int parentSize)
    {
        return (int)(percent / 100f * parentSize);
    }

    /// <summary>
    /// Panels.xml positions are percentages of the tab, but panel sizes are fixed pixels,
    /// so a narrower window would push a right-column panel past the edge. The stored
    /// percentage is kept; only the on-screen position is pulled back inside.
    /// </summary>
    public static Point PlaceInside(float leftPct, float topPct, Size panel, Size parent)
    {
        int x = Math.Min(PercentToPixel(leftPct, parent.Width), parent.Width - panel.Width);
        int y = Math.Min(PercentToPixel(topPct, parent.Height), parent.Height - panel.Height);
        return new Point(Math.Max(0, x), Math.Max(0, y));
    }

    private static float SnapToGrid(float percent)
    {
        return MathF.Round(percent / GridStep) * GridStep;
    }

    internal Point SnapPosition(Point pixelPos, int panelWidth, int panelHeight)
    {
        int parentW = _parent.ClientSize.Width;
        int parentH = _parent.ClientSize.Height;

        float leftPct = PixelToPercent(pixelPos.X, parentW);
        float topPct = PixelToPercent(pixelPos.Y, parentH);

        leftPct = SnapToGrid(leftPct);
        topPct = SnapToGrid(topPct);

        // When the panel is LARGER than the parent (restored-down window, hi-DPI
        // panel) the max goes negative and Math.Clamp(min > max) throws out of the
        // mouse-move handler — pin to the origin instead.
        float maxLeftPct = Math.Max(0f, PixelToPercent(parentW - panelWidth, parentW));
        float maxTopPct = Math.Max(0f, PixelToPercent(parentH - panelHeight, parentH));

        leftPct = Math.Clamp(leftPct, 0f, maxLeftPct);
        topPct = Math.Clamp(topPct, 0f, maxTopPct);

        return new Point(
            PercentToPixel(leftPct, parentW),
            PercentToPixel(topPct, parentH));
    }

    private int OverlapMargin => (int)(4 * _parent.DeviceDpi / 96f);

    private bool WouldOverlap(ManagedPanel dragged, Point proposedLocation)
    {
        int margin = OverlapMargin;
        var draggedRect = new Rectangle(
            proposedLocation.X - margin,
            proposedLocation.Y - margin,
            dragged.Control.Width + margin * 2,
            dragged.Control.Height + margin * 2);
        var currentRect = dragged.Control.Bounds;

        foreach (var other in _panels)
        {
            if (ReferenceEquals(other, dragged)) continue;
            // Skip pairs that were ALREADY overlapping — the user is trying to
            // untangle them, so don't block moves that merely continue an existing
            // overlap. Only reject moves that introduce NEW overlap with a panel
            // the dragged panel wasn't already intersecting.
            if (currentRect.IntersectsWith(other.Control.Bounds)) continue;
            if (draggedRect.IntersectsWith(other.Control.Bounds))
                return true;
        }

        return false;
    }

    private void Panel_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        if (sender is not Control ctl) return;

        var mp = FindManaged(ctl);
        if (mp is null) return;

        mp.IsDragging = true;
        mp.DragOffset = e.Location;
        ctl.BringToFront();
    }

    private void Panel_MouseMove(object? sender, MouseEventArgs e)
    {
        if (sender is not Control ctl) return;

        var mp = FindManaged(ctl);
        if (mp is null || !mp.IsDragging) return;

        Point parentPoint = _parent.PointToClient(ctl.PointToScreen(e.Location));
        Point proposed = new(
            parentPoint.X - mp.DragOffset.X,
            parentPoint.Y - mp.DragOffset.Y);

        Point snapped = SnapPosition(proposed, ctl.Width, ctl.Height);

        if (!WouldOverlap(mp, snapped))
        {
            ctl.Location = snapped;
            mp.IsDirty = true;
        }

        ctl.Invalidate();
    }

    private void Panel_MouseUp(object? sender, MouseEventArgs e)
    {
        if (sender is not Control ctl) return;

        var mp = FindManaged(ctl);
        if (mp is null) return;

        mp.IsDragging = false;
        ctl.Invalidate();
    }

    private void Parent_Paint(object? sender, PaintEventArgs e)
    {
        if (!IsEditMode) return;

        int w = _parent.ClientSize.Width;
        int h = _parent.ClientSize.Height;

        using var pen = new Pen(Color.FromArgb(60, Color.Gray)) { DashStyle = DashStyle.Dot };

        for (float pct = GridStep; pct < 100f; pct += GridStep)
        {
            int x = PercentToPixel(pct, w);
            e.Graphics.DrawLine(pen, x, 0, x, h);
        }

        for (float pct = GridStep; pct < 100f; pct += GridStep)
        {
            int y = PercentToPixel(pct, h);
            e.Graphics.DrawLine(pen, 0, y, w, y);
        }
    }

    private void Panel_Paint(object? sender, PaintEventArgs e)
    {
        if (sender is not Control ctl) return;

        var mp = FindManaged(ctl);
        if (mp is null) return;

        var borderColor = mp.IsDragging ? Color.Red : Color.DodgerBlue;
        using var pen = new Pen(borderColor, 2f);
        e.Graphics.DrawRectangle(pen, 1, 1, ctl.Width - 3, ctl.Height - 3);

        using var dotBrush = new SolidBrush(borderColor);
        for (int row = 0; row < 2; row++)
        {
            for (int col = 0; col < 2; col++)
            {
                e.Graphics.FillEllipse(dotBrush, 6 + col * 6, 6 + row * 6, 3, 3);
            }
        }
    }

    public void EnterEditMode()
    {
        if (IsEditMode) return;
        IsEditMode = true;

        foreach (var mp in _panels)
        {
            mp.OriginalLeft = mp.XmlPanel.Left;
            mp.OriginalTop = mp.XmlPanel.Top;
            mp.IsDirty = false;
            mp.IsDragging = false;

            mp.Control.MouseDown += Panel_MouseDown;
            mp.Control.MouseMove += Panel_MouseMove;
            mp.Control.MouseUp += Panel_MouseUp;
            mp.Control.Paint += Panel_Paint;
            mp.Control.Cursor = Cursors.SizeAll;
        }

        _parent.Paint += Parent_Paint;
        _parent.Invalidate();
    }

    public void ExitEditMode()
    {
        if (!IsEditMode) return;

        SavePositions();
        DetachHandlers();
        IsEditMode = false;
    }

    public void CancelEditMode()
    {
        if (!IsEditMode) return;

        foreach (var mp in _panels)
        {
            if (mp.IsDirty)
            {
                mp.XmlPanel.Left = mp.OriginalLeft;
                mp.XmlPanel.Top = mp.OriginalTop;
                mp.Control.Location = PlaceInside(mp.OriginalLeft, mp.OriginalTop,
                    mp.Control.Size, _parent.ClientSize);
                mp.IsDirty = false;
            }
        }

        DetachHandlers();
        IsEditMode = false;
    }

    private void DetachHandlers()
    {
        foreach (var mp in _panels)
        {
            mp.Control.MouseDown -= Panel_MouseDown;
            mp.Control.MouseMove -= Panel_MouseMove;
            mp.Control.MouseUp -= Panel_MouseUp;
            mp.Control.Paint -= Panel_Paint;
            mp.Control.Cursor = Cursors.Default;
            mp.Control.Invalidate();
        }

        _parent.Paint -= Parent_Paint;
        _parent.Invalidate();
    }

    /// <summary>
    /// Only moved panels are rewritten, preserving existing values (including >100%)
    /// for unmoved panels.
    /// </summary>
    private void SavePositions()
    {
        bool anyDirty = false;
        int parentW = _parent.ClientSize.Width;
        int parentH = _parent.ClientSize.Height;

        foreach (var mp in _panels)
        {
            if (!mp.IsDirty) continue;
            anyDirty = true;

            mp.XmlPanel.Left = SnapToGrid(PixelToPercent(mp.Control.Left, parentW));
            mp.XmlPanel.Top = SnapToGrid(PixelToPercent(mp.Control.Top, parentH));
            mp.IsDirty = false;
        }

        if (anyDirty)
            Serializer<XML_Panels>.Save(_xmlPanels, _configFilePath);
    }

    /// <summary>
    /// Hidden panels are left untouched (their slot closes and re-opens by definition
    /// order when shown again). Persists to Panels.xml only when <paramref name="persist"/>
    /// and a position actually changed.
    /// </summary>
    public void CompactColumns(bool persist = true)
    {
        int parentW = _parent.ClientSize.Width;
        int parentH = _parent.ClientSize.Height;
        if (parentW <= 0 || parentH <= 0) return;

        var boxes = new List<ColumnCompactor.PanelBox>(_panels.Count);
        for (int i = 0; i < _panels.Count; i++)
        {
            var mp = _panels[i];
            boxes.Add(new ColumnCompactor.PanelBox(
                Order: i,
                Left: mp.XmlPanel.Left,
                TopPercent: mp.XmlPanel.Top,
                HeightPercent: PixelToPercent(mp.Control.Height, parentH),
                Visible: mp.Control.Visible));
        }

        float gapPercent = PixelToPercent((int)(8 * _parent.DeviceDpi / 96f), parentH);
        const float ColumnTolerancePercent = 5f;   // columns share Left; bucket width
        var newTops = ColumnCompactor.Compute(boxes, gapPercent, ColumnTolerancePercent);

        bool anyChanged = false;
        for (int i = 0; i < _panels.Count; i++)
        {
            if (!newTops.TryGetValue(i, out float top)) continue;   // hidden -> untouched
            top = SnapToGrid(top);
            var mp = _panels[i];
            if (MathF.Abs(mp.XmlPanel.Top - top) > 0.001f)
                anyChanged = true;
            mp.XmlPanel.Top = top;
            mp.Control.Location = PlaceInside(mp.XmlPanel.Left, top, mp.Control.Size, _parent.ClientSize);
        }

        if (persist && anyChanged)
            Serializer<XML_Panels>.Save(_xmlPanels, _configFilePath);
    }

    public void RepositionAllPanels()
    {
        if (_parent.ClientSize.Width <= 0 || _parent.ClientSize.Height <= 0) return;

        foreach (var mp in _panels)
            mp.Control.Location = PlaceInside(mp.XmlPanel.Left, mp.XmlPanel.Top,
                mp.Control.Size, _parent.ClientSize);
    }

    private ManagedPanel? FindManaged(Control ctl)
    {
        foreach (var mp in _panels)
        {
            if (ReferenceEquals(mp.Control, ctl))
                return mp;
        }
        return null;
    }

    private class ManagedPanel
    {
        public required Control Control { get; init; }
        public required XML_Panel XmlPanel { get; init; }
        public float OriginalLeft { get; set; }
        public float OriginalTop { get; set; }
        public bool IsDirty { get; set; }
        public bool IsDragging { get; set; }
        public Point DragOffset { get; set; }
    }
}
