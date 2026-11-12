using System.ComponentModel;

namespace APS.Controls.Theme;

/// <summary>
/// Armless (no step buttons). Hosts forward mouse-wheel events via <see cref="ScrollByWheel"/>.
/// </summary>
[ToolboxItem(true)]
public sealed class DarkScrollBar : Control
{
    private static readonly Color TrackBg   = Color.FromArgb(13, 27, 42);
    private static readonly Color ThumbIdle = Color.FromArgb(40, 60, 90);
    private static readonly Color ThumbHot  = Color.FromArgb(60, 90, 130);
    private static readonly Color ThumbDrag = Color.FromArgb(72, 169, 254);

    private int _minimum, _maximum = 100, _value;
    private int _largeChange = 10, _smallChange = 3;
    private Orientation _orientation = Orientation.Vertical;
    private bool _thumbHover, _dragging;
    private int _dragStartOffset;

    public event EventHandler? Scroll;

    public DarkScrollBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Width = 12; Height = 12;
        BackColor = TrackBg;
    }

    [DefaultValue(typeof(Orientation), "Vertical")]
    public Orientation Orientation
    {
        get => _orientation;
        set { if (_orientation != value) { _orientation = value; Invalidate(); } }
    }

    [DefaultValue(0)] public int Minimum { get => _minimum; set { _minimum = value; Clamp(); Invalidate(); } }
    [DefaultValue(100)] public int Maximum { get => _maximum; set { _maximum = value; Clamp(); Invalidate(); } }
    [DefaultValue(10)] public int LargeChange { get => _largeChange; set { _largeChange = Math.Max(1, value); Clamp(); Invalidate(); } }
    [DefaultValue(3)] public int SmallChange { get => _smallChange; set { _smallChange = Math.Max(1, value); } }

    [DefaultValue(0)]
    public int Value
    {
        get => _value;
        set
        {
            int v = Math.Clamp(value, _minimum, Math.Max(_minimum, _maximum - _largeChange));
            if (v != _value) { _value = v; Scroll?.Invoke(this, EventArgs.Empty); Invalidate(); }
        }
    }

    private void Clamp() => _value = Math.Clamp(_value, _minimum, Math.Max(_minimum, _maximum - _largeChange));

    private int TrackLength => _orientation == Orientation.Vertical ? Height : Width;
    private int TrackThickness => _orientation == Orientation.Vertical ? Width : Height;

    private Rectangle GetThumbRect()
    {
        int range = Math.Max(1, _maximum - _minimum);
        int track = Math.Max(1, TrackLength);
        int thumb = Math.Max(20, (int)((float)_largeChange / range * track));
        int scrollable = Math.Max(1, range - _largeChange);
        int pos = (int)((float)(_value - _minimum) / scrollable * (track - thumb));
        pos = Math.Clamp(pos, 0, Math.Max(0, track - thumb));

        return _orientation == Orientation.Vertical
            ? new Rectangle(2, pos, TrackThickness - 4, thumb)
            : new Rectangle(pos, 2, thumb, TrackThickness - 4);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using (var bg = new SolidBrush(TrackBg))
            e.Graphics.FillRectangle(bg, ClientRectangle);

        var color = _dragging ? ThumbDrag : _thumbHover ? ThumbHot : ThumbIdle;
        using var brush = new SolidBrush(color);
        e.Graphics.FillRectangle(brush, GetThumbRect());
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
        {
            var thumb = GetThumbRect();
            int thumbSize = _orientation == Orientation.Vertical ? thumb.Height : thumb.Width;
            int posClick = _orientation == Orientation.Vertical ? e.Y : e.X;
            int pos = posClick - _dragStartOffset;
            int track = TrackLength;
            pos = Math.Clamp(pos, 0, Math.Max(0, track - thumbSize));
            int scrollable = Math.Max(1, _maximum - _minimum - _largeChange);
            if (track > thumbSize)
                Value = _minimum + (int)((float)pos / (track - thumbSize) * scrollable);
        }
        else
        {
            bool wasHover = _thumbHover;
            _thumbHover = GetThumbRect().Contains(e.Location);
            if (wasHover != _thumbHover) Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_thumbHover && !_dragging) { _thumbHover = false; Invalidate(); }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        var thumb = GetThumbRect();
        if (thumb.Contains(e.Location))
        {
            _dragging = true;
            _dragStartOffset = _orientation == Orientation.Vertical ? e.Y - thumb.Y : e.X - thumb.X;
            Capture = true;
            Invalidate();
        }
        else
        {
            int click = _orientation == Orientation.Vertical ? e.Y : e.X;
            int thumbPos = _orientation == Orientation.Vertical ? thumb.Y : thumb.X;
            Value = click < thumbPos ? Value - _largeChange : Value + _largeChange;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_dragging) { _dragging = false; Capture = false; Invalidate(); }
    }

    /// <summary>Positive delta = scroll up.</summary>
    public void ScrollByWheel(int delta)
    {
        int step = (delta / 120) * _smallChange * 3; // 3 lines per notch
        Value = _value - step;
    }
}
