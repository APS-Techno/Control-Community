using System.ComponentModel;
using System.Runtime.InteropServices;
using APS.Controls.Theme;

namespace APS.SystemControl.GUIControls.Process;

/// <summary>
/// Knows nothing about its content — consumer adds child to <see cref="ContentPanel"/>.
/// </summary>
[DesignerCategory("Code")]
public class FloatingPanel : UserControl
{
    private const int WM_NCHITTEST   = 0x0084;
    private const int HTCLIENT       = 1;
    private const int HTCAPTION      = 2;
    private const int HTLEFT         = 10;
    private const int HTRIGHT        = 11;
    private const int HTTOP          = 12;
    private const int HTTOPLEFT      = 13;
    private const int HTTOPRIGHT     = 14;
    private const int HTBOTTOM       = 15;
    private const int HTBOTTOMLEFT   = 16;
    private const int HTBOTTOMRIGHT  = 17;
    private const int HTCLOSE        = 20;

    // 96-DPI logical chrome metrics, scaled once by system DPI (fonts scale
    // on their own as point sizes).
    private static readonly int ResizeBandPx  = DpiScale.Px(6);
    private static readonly int CaptionHeight = DpiScale.Px(22);
    private static readonly int SnapThreshold = DpiScale.Px(10);

    // Caption strip background — slightly lighter than the panel body so the
    // drag area is visually distinct. Not in VacuumTheme since it's specific
    // to this control's chrome.
    private static readonly Color CaptionBackground = Color.FromArgb(22, 32, 50);

    private readonly Panel _contentPanel;
    private string _caption = string.Empty;
    private Rectangle _closeRect; // in client coords
    private bool _closeHover;
    private Size _minSize = new(DpiScale.Px(200), DpiScale.Px(100));
    private Control? _subscribedParent;

    private readonly System.Windows.Forms.Timer _boundsDebounce;
    private bool _layoutSettled;

    public FloatingPanel()
    {
        DoubleBuffered = true;
        BackColor = VacuumTheme.PanelBackground;
        SetStyle(ControlStyles.ResizeRedraw, true);

        // No child Label for the title — child controls swallow WM_NCHITTEST
        // and block the OS-driven drag. We paint the title directly in OnPaint.

        _contentPanel = new Panel
        {
            BackColor = VacuumTheme.PanelBackground,
            Padding = new Padding(1),
        };
        Controls.Add(_contentPanel);

        _boundsDebounce = new System.Windows.Forms.Timer { Interval = 200 };
        _boundsDebounce.Tick += (_, _) =>
        {
            _boundsDebounce.Stop();
            if (_layoutSettled) BoundsChanged?.Invoke(this, EventArgs.Empty);
        };

        Resize += (_, _) => LayoutChildren();
        LayoutChildren();
    }

    public string Caption
    {
        get => _caption;
        set { _caption = value ?? string.Empty; Invalidate(); }
    }

    /// <summary>
    /// Minimum size enforced by <see cref="ReclampToParent"/>. Note: this is
    /// separate from WinForms <see cref="Control.MinimumSize"/> — set this
    /// property, not MinimumSize, to constrain the floating panel.
    /// </summary>
    public Size MinSize
    {
        get => _minSize;
        set { _minSize = value; ReclampToParent(); }
    }

    public Panel ContentPanel => _contentPanel;

    public event EventHandler? VisibilityChanged;
    public event EventHandler? BoundsChanged;
    /// <summary>
    /// Raised when the user clicks the [×] caption close button. Distinct
    /// from <see cref="VisibilityChanged"/>, which also fires on parent
    /// cascade (form hidden, etc.). Subscribe to this to record user intent.
    /// </summary>
    public event EventHandler? UserCloseRequested;

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);
        if (_subscribedParent != null)
            _subscribedParent.SizeChanged -= OnParentSizeChanged;
        _subscribedParent = Parent;
        if (_subscribedParent != null)
            _subscribedParent.SizeChanged += OnParentSizeChanged;
    }

    private void OnParentSizeChanged(object? sender, EventArgs e) => ReclampToParent();

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _layoutSettled = true;
    }

    protected override void OnMove(EventArgs e)
    {
        base.OnMove(e);
        if (!_layoutSettled) return;
        SnapDuringDrag();
        ReclampToParent();
        ScheduleBoundsChangedDebounced();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (!_layoutSettled) return;
        ReclampToParent();
        ScheduleBoundsChangedDebounced();
    }

    private void ScheduleBoundsChangedDebounced()
    {
        _boundsDebounce.Stop();
        _boundsDebounce.Start();
    }

    private void SnapDuringDrag()
    {
        if (Parent == null) return;
        var p = Parent.ClientRectangle;
        int snappedX = FloatingPanelLayout.SnapAxis(Left, 0, SnapThreshold);
        snappedX = FloatingPanelLayout.SnapAxis(snappedX + Width, p.Width, SnapThreshold) - Width;
        int snappedY = FloatingPanelLayout.SnapAxis(Top, 0, SnapThreshold);
        snappedY = FloatingPanelLayout.SnapAxis(snappedY + Height, p.Height, SnapThreshold) - Height;
        if (snappedX != Left || snappedY != Top)
            SetBounds(snappedX, snappedY, Width, Height);
    }

    private void ReclampToParent()
    {
        if (Parent == null) return;
        var clamped = FloatingPanelLayout.Clamp(Bounds, Parent.ClientRectangle, _minSize);
        if (clamped != Bounds)
            SetBounds(clamped.X, clamped.Y, clamped.Width, clamped.Height);
    }

    private void LayoutChildren()
    {
        int closeSize = DpiScale.Px(18);
        int closeMargin = DpiScale.Px(4);
        _closeRect = new Rectangle(Width - closeSize - closeMargin, (CaptionHeight - closeSize) / 2,
                                   closeSize, closeSize);
        // Inset the content panel by ResizeBandPx on left/right/bottom so the
        // parent-owned resize band stays exposed. A child Panel swallows
        // WM_NCHITTEST, so any edge it covers becomes ungrabbable for resizing.
        // Top needs no inset: the top resize band lives in the caption strip,
        // which is already parent-owned (no child there).
        _contentPanel.Bounds = new Rectangle(
            ResizeBandPx,
            CaptionHeight,
            Width  - ResizeBandPx * 2,
            Height - CaptionHeight - ResizeBandPx);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;

        using (var bg = new SolidBrush(CaptionBackground))
            g.FillRectangle(bg, 0, 0, Width, CaptionHeight);

        // Caption title — painted (not a child Label) so the caption strip
        // remains a single drag surface for WM_NCHITTEST.
        if (!string.IsNullOrEmpty(_caption))
        {
            using var titleFont = APS.SystemControl.Controls.Theme.ScaledFont.Create(9f, FontStyle.Bold);
            using var titleBrush = new SolidBrush(VacuumTheme.TextPrimary);
            var titleRect = new Rectangle(DpiScale.Px(8), 0, Math.Max(0, _closeRect.Left - DpiScale.Px(12)), CaptionHeight);
            using var fmt = new StringFormat
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoWrap,
                Trimming = StringTrimming.EllipsisCharacter,
            };
            g.DrawString(_caption, titleFont, titleBrush, titleRect, fmt);
        }

        using (var pen = new Pen(VacuumTheme.PanelBorder, 1f))
            g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);

        var rect = _closeRect;
        if (_closeHover)
        {
            using var hover = new SolidBrush(Color.FromArgb(60, 80, 105));
            g.FillRectangle(hover, rect);
        }
        using var penX = new Pen(VacuumTheme.TextPrimary, 1.5f);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        int inset = DpiScale.Px(5);
        g.DrawLine(penX, rect.Left + inset, rect.Top + inset,
                         rect.Right - inset, rect.Bottom - inset);
        g.DrawLine(penX, rect.Right - inset, rect.Top + inset,
                         rect.Left + inset, rect.Bottom - inset);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool over = _closeRect.Contains(e.Location);
        if (over != _closeHover) { _closeHover = over; Invalidate(_closeRect); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_closeHover) { _closeHover = false; Invalidate(_closeRect); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    private static extern int MapWindowPoints(IntPtr hWndFrom, IntPtr hWndTo, ref POINT pt, int cPoints);

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_NCHITTEST)
        {
            // lParam is screen point packed as LOWORD/HIWORD.
            int x = (short)(m.LParam.ToInt32() & 0xFFFF);
            int y = (short)((m.LParam.ToInt32() >> 16) & 0xFFFF);
            var ptScreen = new POINT { X = x, Y = y };
            MapWindowPoints(IntPtr.Zero, Handle, ref ptScreen, 1);

            int hit = HitTest(ptScreen.X, ptScreen.Y);
            if (hit != HTCLIENT)
            {
                m.Result = (IntPtr)hit;
                return;
            }
        }
        base.WndProc(ref m);
    }

    private int HitTest(int cx, int cy)
    {
        bool left   = cx < ResizeBandPx;
        bool right  = cx >= Width  - ResizeBandPx;
        bool top    = cy < ResizeBandPx;
        bool bottom = cy >= Height - ResizeBandPx;

        if (top && left)     return HTTOPLEFT;
        if (top && right)    return HTTOPRIGHT;
        if (bottom && left)  return HTBOTTOMLEFT;
        if (bottom && right) return HTBOTTOMRIGHT;
        if (left)            return HTLEFT;
        if (right)           return HTRIGHT;
        if (top)             return HTTOP;
        if (bottom)          return HTBOTTOM;

        // Close button — let mouse events fire normally, but report HTCLIENT.
        if (_closeRect.Contains(cx, cy)) return HTCLIENT;

        if (cy < CaptionHeight) return HTCAPTION;

        return HTCLIENT;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
            BringToFront();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left && _closeRect.Contains(e.Location))
        {
            UserCloseRequested?.Invoke(this, EventArgs.Empty);
            Visible = false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _boundsDebounce.Dispose();
        base.Dispose(disposing);
    }
}
