using System.Drawing.Drawing2D;

namespace APS.Controls.Theme;

/// <summary>
/// Owner-paints the glyph: a standard CheckBox honors BackColor only around the
/// glyph, which the Win32 button theme draws white when unchecked.
/// </summary>
public sealed class DarkCheckBox : CheckBox
{
    private static readonly Color BgInput = Color.FromArgb(18, 30, 48);
    private static readonly Color BgPanel = Color.FromArgb(13, 27, 42);
    private static readonly Color BgHover = Color.FromArgb(30, 44, 66);
    private static readonly Color Border = Color.FromArgb(40, 60, 90);
    private static readonly Color TextPrimary = Color.FromArgb(200, 214, 229);
    private static readonly Color Accent = Color.FromArgb(72, 169, 254);

    private const int GlyphSize = 14;
    private const int GlyphTextGap = 5;

    private bool _hover;

    public DarkCheckBox()
    {
        SetStyle(ControlStyles.UserPaint
               | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.SupportsTransparentBackColor
               | ControlStyles.ResizeRedraw, true);
        BackColor = BgPanel;
        ForeColor = TextPrimary;
        AutoSize = true;
        FlatStyle = FlatStyle.Flat;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var textSize = TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding);
        int w = GlyphSize + GlyphTextGap + textSize.Width + 2;
        int h = Math.Max(GlyphSize, textSize.Height) + 2;
        return new Size(w, h);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        using (var bg = new SolidBrush(BackColor))
            g.FillRectangle(bg, ClientRectangle);

        int glyphY = (ClientSize.Height - GlyphSize) / 2;
        var glyph = new Rectangle(0, glyphY, GlyphSize, GlyphSize);

        using (var fill = new SolidBrush(_hover ? BgHover : BgInput))
            g.FillRectangle(fill, glyph);
        using (var pen = new Pen(Checked || CheckState == CheckState.Indeterminate ? Accent : Border))
            g.DrawRectangle(pen, glyph);

        if (Checked)
        {
            var prevSmoothing = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var checkPen = new Pen(Accent, 2f);
            Point p1 = new(glyph.Left + 3,  glyph.Top + GlyphSize / 2);
            Point p2 = new(glyph.Left + GlyphSize / 2 - 1, glyph.Bottom - 4);
            Point p3 = new(glyph.Right - 3, glyph.Top + 3);
            g.DrawLines(checkPen, [p1, p2, p3]);
            g.SmoothingMode = prevSmoothing;
        }
        else if (CheckState == CheckState.Indeterminate)
        {
            using var fillBrush = new SolidBrush(Accent);
            g.FillRectangle(fillBrush,
                glyph.Left + 3, glyph.Top + 3, GlyphSize - 6, GlyphSize - 6);
        }

        var textRect = new Rectangle(
            GlyphSize + GlyphTextGap, 0,
            ClientSize.Width - GlyphSize - GlyphTextGap,
            ClientSize.Height);
        TextRenderer.DrawText(g, Text, Font, textRect,
            Enabled ? ForeColor : Color.FromArgb(119, 136, 153),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        if (Focused && ShowFocusCues)
        {
            var focusRect = Rectangle.Inflate(textRect, -1, -1);
            ControlPaint.DrawFocusRectangle(g, focusRect);
        }
    }
}
