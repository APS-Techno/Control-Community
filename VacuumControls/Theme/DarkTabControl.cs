using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace APS.Controls.Theme;

/// <summary>
/// Handles all painting itself, so no DarkModeHelper is needed.
/// </summary>
[ToolboxItem(true)]
public class DarkTabControl : TabControl
{
    private const int WM_ERASEBKGND = 0x0014;
    private const int WM_PAINT = 0x000F;

    private static readonly Color StripBg = Color.FromArgb(30, 44, 66);
    private static readonly Color PageBg = Color.FromArgb(13, 27, 42);
    private static readonly Color SelectedBg = Color.FromArgb(30, 44, 66);
    private static readonly Color HoverBg = Color.FromArgb(44, 60, 86);
    private static readonly Color AccentColor = Color.FromArgb(72, 169, 254);
    private static readonly Color TextColor = Color.FromArgb(200, 214, 229);

    private int _hoverIndex = -1;
    private Font? _headerFont;
    private Font? _boldFont;

    /// <summary>
    /// When null, the ambient <see cref="Control.Font"/> is used. Set this INSTEAD of
    /// <see cref="Control.Font"/> to enlarge the headers without enlarging the
    /// tab-page contents: Control.Font cascades into every page's child controls.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Font? HeaderFont
    {
        get => _headerFont;
        set
        {
            _headerFont = value;
            _boldFont?.Dispose();
            _boldFont = null;
            Invalidate();
        }
    }

    private Font HeaderFontOrAmbient => _headerFont ?? Font;
    private Font BoldFont => _boldFont ??= new Font(HeaderFontOrAmbient, FontStyle.Bold);

    public DarkTabControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        DrawMode = TabDrawMode.OwnerDrawFixed;
        Padding = new Point(DpiScale.Px(14), DpiScale.Px(5));
        ItemSize = new Size(0, DpiScale.Px(28));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int newHover = -1;
        for (int i = 0; i < TabCount; i++)
        {
            if (GetTabRect(i).Contains(e.Location))
            {
                newHover = i;
                break;
            }
        }
        if (newHover != _hoverIndex)
        {
            _hoverIndex = newHover;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverIndex != -1)
        {
            _hoverIndex = -1;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using (var stripBrush = new SolidBrush(StripBg))
            g.FillRectangle(stripBrush, ClientRectangle);

        // Oversized to cover the native border region.
        if (TabCount > 0)
        {
            var dr = DisplayRectangle;
            using var pageBrush = new SolidBrush(PageBg);
            g.FillRectangle(pageBrush, dr.X - 4, dr.Y - 4, dr.Width + 8, dr.Height + 8);
        }

        for (int i = 0; i < TabCount; i++)
            DrawTab(g, i);

        // Pin SmoothingMode.None so the 1-px frame renders identically on both
        // sides (AA softens them asymmetrically).
        var prevSmoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.None;
        using (var borderPen = new Pen(SystemColors.ControlDark))
        {
            var cr = ClientRectangle;
            g.DrawRectangle(borderPen, 0, 0, cr.Width - 1, cr.Height - 1);
        }
        g.SmoothingMode = prevSmoothing;
    }

    private void DrawTab(Graphics g, int index)
    {
        var bounds = GetTabRect(index);
        bool selected = index == SelectedIndex;
        bool hovered = index == _hoverIndex && !selected;

        Color bg = selected ? SelectedBg : hovered ? HoverBg : StripBg;
        using (var brush = new SolidBrush(bg))
            g.FillRectangle(brush, bounds);

        if (selected)
        {
            using var pen = new Pen(AccentColor, 2);
            g.DrawLine(pen, bounds.Left + 2, bounds.Bottom - 1, bounds.Right - 2, bounds.Bottom - 1);
        }

        var font = selected ? BoldFont : HeaderFontOrAmbient;
        var textColor = selected ? AccentColor : TextColor;
        TextRenderer.DrawText(g, TabPages[index].Text, font, bounds, textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        // Handled in OnPaint — intentionally empty
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        _boldFont?.Dispose();
        _boldFont = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _boldFont?.Dispose();
            _boldFont = null;
        }
        base.Dispose(disposing);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_ERASEBKGND)
        {
            // Suppress default erase — OnPaint handles everything
            m.Result = (IntPtr)1;
            return;
        }

        base.WndProc(ref m);

        // After default WM_PAINT, paint over any native borders/artifacts
        if (m.Msg == WM_PAINT && TabCount > 0 && !GetStyle(ControlStyles.UserPaint))
        {
            PaintOverBorders();
        }
    }

    private void PaintOverBorders()
    {
        using var g = Graphics.FromHwnd(Handle);
        var dr = DisplayRectangle;

        using var pageBrush = new SolidBrush(PageBg);
        int b = 4;
        g.FillRectangle(pageBrush, dr.X - b, dr.Y - b, dr.Width + b * 2, b);
        g.FillRectangle(pageBrush, dr.X - b, dr.Bottom, dr.Width + b * 2, b);
        g.FillRectangle(pageBrush, dr.X - b, dr.Y - b, b, dr.Height + b * 2);
        g.FillRectangle(pageBrush, dr.Right, dr.Y - b, b, dr.Height + b * 2);

        using var stripBrush = new SolidBrush(StripBg);
        g.FillRectangle(stripBrush, 0, 0, Width, dr.Y - b);

        for (int i = 0; i < TabCount; i++)
            DrawTab(g, i);
    }

    public void ApplyPageTheme()
    {
        foreach (TabPage page in TabPages)
        {
            page.UseVisualStyleBackColor = false;
            page.BackColor = PageBg;
            page.ForeColor = TextColor;
        }
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        if (e.Control is TabPage page)
        {
            page.UseVisualStyleBackColor = false;
            page.BackColor = PageBg;
            page.ForeColor = TextColor;
        }
    }
}
