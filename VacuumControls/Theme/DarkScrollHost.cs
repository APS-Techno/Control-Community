using System.Runtime.InteropServices;

namespace APS.Controls.Theme;

/// <summary>
/// Wraps a control that has native Win32 scrollbars (RichTextBox, TreeView,
/// ListView, AutoScroll Panel, etc.), hides those scrollbars, and replaces
/// them visually with <see cref="DarkScrollBar"/>s on the right and bottom.
/// State is driven by polling (100 ms) so keyboard-initiated scrolls stay
/// in sync.
/// </summary>
public class DarkScrollHost : UserControl
{
    private const int SB_HORZ = 0;
    private const int SB_VERT = 1;
    private const int WM_HSCROLL = 0x0114;
    private const int WM_VSCROLL = 0x0115;
    private const int SB_THUMBPOSITION = 4;
    private const int SB_ENDSCROLL = 8;
    private const uint SIF_RANGE = 0x0001;
    private const uint SIF_PAGE = 0x0002;
    private const uint SIF_POS = 0x0004;
    private const uint SIF_ALL = SIF_RANGE | SIF_PAGE | SIF_POS;
    private const int EM_LINESCROLL = 0x00B6;
    private const int GWL_STYLE = -16;
    private const long WS_VSCROLL = 0x00200000;
    private const long WS_HSCROLL = 0x00100000;

    [StructLayout(LayoutKind.Sequential)]
    private struct SCROLLINFO
    {
        public uint cbSize;
        public uint fMask;
        public int nMin;
        public int nMax;
        public uint nPage;
        public int nPos;
        public int nTrackPos;
    }

    [DllImport("user32.dll")] private static extern bool ShowScrollBar(IntPtr hWnd, int wBar, bool bShow);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern bool GetScrollInfo(IntPtr hwnd, int fnBar, ref SCROLLINFO lpsi);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    private readonly Control _inner;
    private readonly DarkScrollBar _vBar;
    private readonly DarkScrollBar _hBar;
    private readonly System.Windows.Forms.Timer _pollTimer;
    private bool _syncing;
    private int _lastVMax, _lastVPos, _lastVPage;
    private int _lastHMax, _lastHPos, _lastHPage;
    private WheelFilter? _wheelFilter;

    public Control Inner => _inner;

    public DarkScrollHost(Control inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _inner.Dock = DockStyle.Fill;

        // For RichTextBox we have a first-class property; use it — it's more
        // reliable than style-bit stripping on that control.
        if (_inner is RichTextBox rtb) rtb.ScrollBars = RichTextBoxScrollBars.None;

        // DataGridView reserves space for its own bars based on its ScrollBars
        // property. Hiding them only via Win32 leaves a dead strip; clearing
        // the property reclaims that space. We then route mouse wheel through
        // FirstDisplayedScrollingRowIndex since DGV's own wheel handler relies
        // on its scrollbars being active.
        if (_inner is DataGridView dgv0)
        {
            dgv0.ScrollBars = ScrollBars.None;
            dgv0.MouseWheel += OnInnerMouseWheel;
        }

        _vBar = new DarkScrollBar
        {
            Orientation = Orientation.Vertical,
            Dock = DockStyle.Right,
            Width = 12,
            Visible = false,
        };
        _vBar.Scroll += OnVBarScroll;

        _hBar = new DarkScrollBar
        {
            Orientation = Orientation.Horizontal,
            Dock = DockStyle.Bottom,
            Height = 12,
            Visible = false,
        };
        _hBar.Scroll += OnHBarScroll;

        // Dock order: Fill first (takes whole client), then Right and Bottom
        // dock in — vBar ends up full-height on the right, hBar ends up on
        // the bottom occupying everything to the left of vBar.
        Controls.Add(_inner);
        Controls.Add(_vBar);
        Controls.Add(_hBar);

        _pollTimer = new System.Windows.Forms.Timer { Interval = 100 };
        _pollTimer.Tick += (_, _) => SyncFromInner();

        _inner.HandleCreated += (_, _) => HideNativeBars();
        _inner.Resize += (_, _) => HideNativeBars();
        HandleCreated += (_, _) =>
        {
            if (_inner.IsHandleCreated) HideNativeBars();
            _pollTimer.Start();
            if (_wheelFilter is null)
            {
                _wheelFilter = new WheelFilter(this);
                Application.AddMessageFilter(_wheelFilter);
            }
        };
        // Message filters are thread-global, so drop ours the moment this host stops
        // being a live window rather than waiting for Dispose — a reparented control
        // re-registers on the next HandleCreated.
        HandleDestroyed += (_, _) => RemoveWheelFilter();

        if (_inner is ICustomScrollSource css)
            css.ScrollMetricsChanged += (_, _) => SyncFromInner();
    }

    private const int WM_MOUSEWHEEL = 0x020A;

    private void RemoveWheelFilter()
    {
        if (_wheelFilter is null) return;
        Application.RemoveMessageFilter(_wheelFilter);
        _wheelFilter = null;
    }

    /// <summary>
    /// Routes the wheel to whichever host the pointer is over.
    /// <para>
    /// Two things make this necessary. The inner control's native scrollbars are
    /// suppressed, and a control with no scrollbar does not scroll itself on a wheel
    /// notch. And WinForms delivers <c>WM_MOUSEWHEEL</c> to the control with FOCUS, not
    /// the one under the cursor — so a read-only panel nobody has clicked would never
    /// see the message even if it could act on it.
    /// </para>
    /// </summary>
    private sealed class WheelFilter(DarkScrollHost owner) : IMessageFilter
    {
        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != WM_MOUSEWHEEL) return false;
            if (owner.IsDisposed || !owner.IsHandleCreated || !owner.Visible) return false;

            // lParam carries the cursor position in SCREEN coordinates.
            long lp = m.LParam.ToInt64();
            var screenPt = new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
            if (!owner.RectangleToScreen(owner.ClientRectangle).Contains(screenPt)) return false;

            int delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
            return owner.ScrollByWheel(delta);
        }
    }

    /// <summary>
    /// Applies one wheel gesture through the scrollbar, so every control type reuses the
    /// bar's clamping and the RichTextBox over-scroll correction. Returns false — leaving
    /// the message for whatever is underneath, typically a scrolling parent — when there
    /// is nothing to scroll here.
    /// </summary>
    private bool ScrollByWheel(int delta)
    {
        if (!_vBar.Visible) return false;
        int lines = SystemInformation.MouseWheelScrollLines;
        if (lines <= 0) lines = 3;
        int step = -delta / 120 * lines;
        if (step == 0) return false;
        int before = _vBar.Value;
        _vBar.Value = before + step;          // raises Scroll -> OnVBarScroll
        return _vBar.Value != before;
    }

    private void OnInnerMouseWheel(object? sender, MouseEventArgs e)
    {
        if (_inner is not DataGridView dgv || dgv.RowCount == 0) return;
        int lines = SystemInformation.MouseWheelScrollLines;
        if (lines <= 0) lines = 3;
        int step = -e.Delta / 120 * lines;
        if (step == 0) return;
        int target = Math.Max(0, Math.Min(dgv.RowCount - 1, dgv.FirstDisplayedScrollingRowIndex + step));
        try { dgv.FirstDisplayedScrollingRowIndex = target; }
        catch (InvalidOperationException) { }
        catch (ArgumentOutOfRangeException) { }
        if (e is HandledMouseEventArgs h) h.Handled = true;
    }

    private bool _hidingBars;

    private void HideNativeBars()
    {
        if (!_inner.IsHandleCreated || _hidingBars) return;
        // ShowScrollBar generates WM_WINDOWPOSCHANGED → Resize on the inner
        // control even when the bar is already hidden. Combined with a Resize
        // handler that re-lays-out content (e.g. a ListView fill-width column)
        // this recursed forever at non-96 DPI. Only touch bars whose style bit
        // says they are actually visible, and never re-enter.
        _hidingBars = true;
        try
        {
            long style = GetWindowLongPtr(_inner.Handle, GWL_STYLE).ToInt64();
            if ((style & WS_VSCROLL) != 0) ShowScrollBar(_inner.Handle, SB_VERT, false);
            if ((style & WS_HSCROLL) != 0) ShowScrollBar(_inner.Handle, SB_HORZ, false);
        }
        finally
        {
            _hidingBars = false;
        }
    }

    private void SyncFromInner()
    {
        if (_syncing || IsDisposed || !_inner.IsHandleCreated) return;
        HideNativeBars();
        SyncVertical();
        SyncHorizontal();
    }

    private void SyncVertical()
    {
        int max = 0, pos = 0, page = 0;
        switch (_inner)
        {
            case ICustomScrollSource css:
                max = css.ContentSize.Height;
                page = css.ViewportSize.Height;
                pos = css.ScrollPosition.Y;
                break;
            case RichTextBox rtb:
            {
                int total = rtb.Lines.Length;
                max = total; page = VisibleLineCount(rtb); pos = TopVisibleLine(rtb);
                break;
            }
            case TreeView tv:
            {
                int total = CountVisibleNodes(tv.Nodes);
                int first = tv.TopNode is null ? 0 : IndexOfNode(tv.Nodes, tv.TopNode);
                int visible = Math.Max(1, tv.VisibleCount);
                max = total; page = visible; pos = first;
                break;
            }
            case DataGridView dgv:
            {
                int total = dgv.RowCount;
                int first = Math.Max(0, dgv.FirstDisplayedScrollingRowIndex);
                int visible = Math.Max(1, dgv.DisplayedRowCount(false));
                max = total; page = visible; pos = first;
                break;
            }
            default:
            {
                var si = new SCROLLINFO { cbSize = (uint)Marshal.SizeOf<SCROLLINFO>(), fMask = SIF_ALL };
                if (!GetScrollInfo(_inner.Handle, SB_VERT, ref si)) return;
                max = si.nMax; pos = si.nPos; page = (int)si.nPage;
                break;
            }
        }

        bool needed = max > page && page > 0;
        _syncing = true;
        try
        {
            if (_vBar.Visible != needed) _vBar.Visible = needed;
            if (!needed) return;
            if (_lastVMax != max || _lastVPage != page)
            {
                _vBar.Minimum = 0;
                _vBar.Maximum = max;
                _vBar.LargeChange = page;
                _lastVMax = max; _lastVPage = page;
            }
            if (_vBar.Value != pos) { _vBar.Value = pos; _lastVPos = pos; }
        }
        finally { _syncing = false; }
    }

    private void OnVBarScroll(object? sender, EventArgs e)
    {
        if (_syncing || !_inner.IsHandleCreated) return;
        int target = _vBar.Value;
        int delta = target - _lastVPos;
        if (delta == 0) return;

        switch (_inner)
        {
            case ICustomScrollSource css:
                css.ScrollPosition = new Point(css.ScrollPosition.X, target);
                break;
            case RichTextBox rtb:
            {
                // EM_LINESCROLL: wParam = horizontal chars, lParam = vertical lines.
                SendMessage(rtb.Handle, EM_LINESCROLL, IntPtr.Zero, new IntPtr(delta));
                // A rich edit will happily scroll PAST the last line — unlike a plain
                // edit control it does not pin the final line to the bottom — leaving a
                // viewport of blank space where the end of the document should be. Pull
                // back by however many lines of emptiness opened up below the last line.
                // Correcting afterwards rather than clamping the target beforehand also
                // recovers a view that is already over-scrolled, where "how many lines
                // fit" measures as 1 and any up-front clamp would keep it stuck.
                for (int guard = 0; guard < 4; guard++)
                {
                    int overshoot = OverscrolledLines(rtb);
                    if (overshoot <= 0) break;
                    SendMessage(rtb.Handle, EM_LINESCROLL, IntPtr.Zero, new IntPtr(-overshoot));
                }
                // Read the position back instead of trusting `target`: the bar counts
                // uniform lines while the control scrolls real ones, so section headers
                // and other taller lines make the two drift apart. Left unchecked the
                // error accumulates across drags until the view runs off the end.
                _lastVPos = TopVisibleLine(rtb);
                if (_vBar.Value != _lastVPos)
                {
                    _syncing = true;
                    try { _vBar.Value = _lastVPos; }
                    finally { _syncing = false; }
                }
                return;
            }
            case TreeView tv:
            {
                var node = GetVisibleNodeAt(tv.Nodes, target);
                if (node != null) tv.TopNode = node;
                break;
            }
            case DataGridView dgv:
            {
                if (dgv.RowCount == 0) break;
                int newIndex = Math.Max(0, Math.Min(target, dgv.RowCount - 1));
                try { dgv.FirstDisplayedScrollingRowIndex = newIndex; }
                catch (InvalidOperationException) { /* row not currently displayable */ }
                catch (ArgumentOutOfRangeException) { /* clamp race */ }
                break;
            }
            // WinForms ScrollableControl reads nTrackPos via GetScrollInfo for
            // SB_THUMBPOSITION instead of HIWORD(wParam), so the synthetic
            // WM_VSCROLL below is a no-op when the native bar is hidden.
            case ScrollableControl sc when sc.AutoScroll:
                sc.AutoScrollPosition = new Point(-sc.AutoScrollPosition.X, target);
                break;
            default:
                SendMessage(_inner.Handle, WM_VSCROLL, new IntPtr((target << 16) | SB_THUMBPOSITION), IntPtr.Zero);
                SendMessage(_inner.Handle, WM_VSCROLL, new IntPtr(SB_ENDSCROLL), IntPtr.Zero);
                break;
        }
        _lastVPos = target;
    }

    /// <summary>
    /// Lines of blank space currently showing below the last line of text, i.e. how far
    /// the view has run off the end. Zero when the document fills the viewport or is
    /// shorter than it. The uniform font height is only used to turn a pixel gap into a
    /// line count, and the caller re-checks after correcting, so a mixed-size document
    /// converges rather than relying on the estimate being exact.
    /// </summary>
    private static int OverscrolledLines(RichTextBox rtb)
    {
        if (rtb.TextLength == 0) return 0;
        int lineHeight = Math.Max(1, TextRenderer.MeasureText("X", rtb.Font).Height);
        int lastBottom = rtb.GetPositionFromCharIndex(rtb.TextLength - 1).Y + lineHeight;
        int gap = rtb.ClientSize.Height - lastBottom;
        // Only a view scrolled off the end counts: if the whole document already fits,
        // the top line is 0 and there is nothing to pull back. A gap narrower than one
        // line is the correct resting place — scrolling is quantised to whole lines, so
        // pulling back over a few spare pixels would only clip the final line instead,
        // and the two states would fight each other on every scroll.
        if (gap < lineHeight || TopVisibleLine(rtb) == 0) return 0;
        return gap / lineHeight;
    }

    private static int TopVisibleLine(RichTextBox rtb)
        => rtb.GetLineFromCharIndex(rtb.GetCharIndexFromPosition(new Point(0, 0)));

    /// <summary>
    /// How many lines actually fit in the viewport right now, measured from the laid-out
    /// text rather than <c>ClientSize.Height / font height</c>. Rich text mixes fonts and
    /// sizes — the reference panels use a larger face for section headings — so a uniform
    /// estimate overstates the page, which shortens the scrollbar's reachable range and
    /// strands the end of the document.
    /// </summary>
    private static int VisibleLineCount(RichTextBox rtb)
    {
        int first = TopVisibleLine(rtb);
        int last = rtb.GetLineFromCharIndex(
            rtb.GetCharIndexFromPosition(new Point(0, Math.Max(0, rtb.ClientSize.Height - 1))));
        return Math.Max(1, last - first + 1);
    }

    private void SyncHorizontal()
    {
        int max = 0, pos = 0, page = 0;
        bool ok;
        if (_inner is ICustomScrollSource css)
        {
            max = css.ContentSize.Width;
            page = css.ViewportSize.Width;
            pos = css.ScrollPosition.X;
            ok = true;
        }
        else if (_inner is DataGridView dgv)
        {
            max = dgv.Columns.GetColumnsWidth(DataGridViewElementStates.Visible);
            page = dgv.ClientSize.Width;
            pos = dgv.HorizontalScrollingOffset;
            ok = true;
        }
        else
        {
            var si = new SCROLLINFO { cbSize = (uint)Marshal.SizeOf<SCROLLINFO>(), fMask = SIF_ALL };
            ok = GetScrollInfo(_inner.Handle, SB_HORZ, ref si);
            if (ok) { max = si.nMax; pos = si.nPos; page = (int)si.nPage; }
        }

        bool needed = ok && max > page && page > 0;
        _syncing = true;
        try
        {
            if (_hBar.Visible != needed) _hBar.Visible = needed;
            if (!needed) return;
            if (_lastHMax != max || _lastHPage != page)
            {
                _hBar.Minimum = 0;
                _hBar.Maximum = max;
                _hBar.LargeChange = page;
                _lastHMax = max; _lastHPage = page;
            }
            if (_hBar.Value != pos) { _hBar.Value = pos; _lastHPos = pos; }
        }
        finally { _syncing = false; }
    }

    private void OnHBarScroll(object? sender, EventArgs e)
    {
        if (_syncing || !_inner.IsHandleCreated) return;
        int target = _hBar.Value;
        int delta = target - _lastHPos;
        if (delta == 0) return;

        switch (_inner)
        {
            case ICustomScrollSource css:
                css.ScrollPosition = new Point(target, css.ScrollPosition.Y);
                break;
            case RichTextBox rtb:
                // EM_LINESCROLL horizontal scroll — wParam = delta chars
                SendMessage(rtb.Handle, EM_LINESCROLL, new IntPtr(delta), IntPtr.Zero);
                break;
            case DataGridView dgv:
                try { dgv.HorizontalScrollingOffset = Math.Max(0, target); }
                catch (ArgumentOutOfRangeException) { /* clamp race */ }
                break;
            case ScrollableControl sc when sc.AutoScroll:
                sc.AutoScrollPosition = new Point(target, -sc.AutoScrollPosition.Y);
                break;
            default:
                SendMessage(_inner.Handle, WM_HSCROLL, new IntPtr((target << 16) | SB_THUMBPOSITION), IntPtr.Zero);
                SendMessage(_inner.Handle, WM_HSCROLL, new IntPtr(SB_ENDSCROLL), IntPtr.Zero);
                break;
        }
        _lastHPos = target;
    }

    private static int CountVisibleNodes(TreeNodeCollection nodes)
    {
        int n = 0;
        foreach (TreeNode node in nodes)
        {
            n++;
            if (node.IsExpanded) n += CountVisibleNodes(node.Nodes);
        }
        return n;
    }

    private static int IndexOfNode(TreeNodeCollection roots, TreeNode target)
    {
        int i = 0;
        int? found = null;
        void Walk(TreeNodeCollection nodes)
        {
            foreach (TreeNode node in nodes)
            {
                if (found.HasValue) return;
                if (node == target) { found = i; return; }
                i++;
                if (node.IsExpanded) Walk(node.Nodes);
            }
        }
        Walk(roots);
        return found ?? 0;
    }

    private static TreeNode? GetVisibleNodeAt(TreeNodeCollection roots, int index)
    {
        int i = 0;
        TreeNode? result = null;
        void Walk(TreeNodeCollection nodes)
        {
            foreach (TreeNode node in nodes)
            {
                if (result != null) return;
                if (i++ == index) { result = node; return; }
                if (node.IsExpanded) Walk(node.Nodes);
            }
        }
        Walk(roots);
        return result;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pollTimer?.Dispose();
            RemoveWheelFilter();
        }
        base.Dispose(disposing);
    }
}
