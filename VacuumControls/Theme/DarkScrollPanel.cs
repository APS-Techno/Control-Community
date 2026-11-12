using System.ComponentModel;

namespace APS.Controls.Theme;

/// <summary>
/// Drop-in replacement for <see cref="Panel"/> with <c>AutoScroll=true</c>.
/// Children added via <c>Controls.Add</c> are reparented to an internal content
/// panel, so existing call-sites keep working.
/// </summary>
[ToolboxItem(true)]
public class DarkScrollPanel : Panel, IMessageFilter
{
    private readonly Panel _content;
    private readonly DarkScrollBar _vBar;
    private bool _initializing = true;
    private bool _messageFilterActive;
    private int _updateDepth;

    /// <summary>Hosts the scrollable children; use its <c>ClientSize</c> for layout math.</summary>
    public Panel ContentPanel => _content;

    [DefaultValue(12)]
    public int ScrollBarWidth { get; set; } = 12;

    public DarkScrollPanel()
    {
        base.AutoScroll = false;
        _content = new Panel
        {
            Location = Point.Empty,
            AutoScroll = false,
            BackColor = Color.Transparent,
        };
        _vBar = new DarkScrollBar
        {
            Orientation = Orientation.Vertical,
            Dock = DockStyle.Right,
            Width = ScrollBarWidth,
            Visible = false,
        };
        _vBar.Scroll += (_, _) => _content.Location = new Point(0, -_vBar.Value);

        base.Controls.Add(_vBar);
        base.Controls.Add(_content);
        _initializing = false;

        Resize += (_, _) => UpdateLayout();
        _content.ControlAdded += (_, _) => UpdateLayout();
        _content.ControlRemoved += (_, _) => UpdateLayout();
    }

    /// <summary>Always <c>false</c>; setting it has no effect — this class manages its own scrolling.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new bool AutoScroll
    {
        get => false;
        set { }
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        if (_initializing) return;
        if (e.Control is null || e.Control == _content || e.Control == _vBar) return;

        e.Control.Parent = _content;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!_messageFilterActive)
        {
            Application.AddMessageFilter(this);
            _messageFilterActive = true;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _messageFilterActive)
        {
            Application.RemoveMessageFilter(this);
            _messageFilterActive = false;
        }
        base.Dispose(disposing);
    }

    /// <summary>Defer layout/scrollbar recompute while bulk-adding children.
    /// Pair with <see cref="EndUpdate"/>; calls nest. Without this, every
    /// <c>Controls.Add</c> re-flips scrollbar visibility and resizes
    /// <c>ContentPanel</c>, producing a visible one-by-one cascade.</summary>
    public void BeginUpdate()
    {
        _updateDepth++;
        ContentPanel.SuspendLayout();
    }

    public void EndUpdate()
    {
        if (_updateDepth == 0) return;
        ContentPanel.ResumeLayout(false);
        if (--_updateDepth == 0) UpdateLayout();
    }

    private void UpdateLayout()
    {
        if (_updateDepth > 0) return;
        if (IsDisposed) return;
        int contentHeight = 0;
        foreach (Control c in _content.Controls)
            contentHeight = Math.Max(contentHeight, c.Bottom);

        int viewHeight = ClientSize.Height;
        bool needsV = contentHeight > viewHeight;

        _vBar.Visible = needsV;
        int viewWidth = ClientSize.Width - (needsV ? _vBar.Width : 0);
        if (viewWidth <= 0 || viewHeight <= 0) return;

        _content.Size = new Size(viewWidth, Math.Max(contentHeight, viewHeight));

        if (needsV)
        {
            _vBar.Maximum = contentHeight;
            _vBar.LargeChange = viewHeight;
            int max = Math.Max(0, contentHeight - viewHeight);
            if (_vBar.Value > max) _vBar.Value = max;
        }
        else
        {
            _vBar.Value = 0;
            _content.Location = Point.Empty;
        }
    }

    /// <summary>Recompute the scroll range — call after mutating children's
    /// positions directly (e.g. custom layout via <c>Location</c>).</summary>
    public void InvalidateScrollLayout() => UpdateLayout();

    // Routes WM_MOUSEWHEEL over this panel to our bar.
    private const int WM_MOUSEWHEEL = 0x020A;

    public bool PreFilterMessage(ref Message m)
    {
        if (m.Msg != WM_MOUSEWHEEL) return false;
        if (!_vBar.Visible || IsDisposed || !Visible) return false;

        var screen = MousePosition;
        var client = PointToClient(screen);
        if (!ClientRectangle.Contains(client)) return false;

        int delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
        _vBar.ScrollByWheel(delta);
        return true;
    }
}
