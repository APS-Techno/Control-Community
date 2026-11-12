using System.Drawing.Drawing2D;

namespace APS.Controls.Theme;

/// <summary>
/// Owner-paints the glyph for the same reason as <see cref="DarkCheckBox"/>.
/// </summary>
public sealed class DarkRadioButton : RadioButton
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

    public DarkRadioButton()
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

        var prevSmoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var fill = new SolidBrush(_hover ? BgHover : BgInput))
            g.FillEllipse(fill, glyph);
        using (var pen = new Pen(Checked ? Accent : Border))
            g.DrawEllipse(pen, glyph);

        if (Checked)
        {
            var inner = Rectangle.Inflate(glyph, -3, -3);
            using var dot = new SolidBrush(Accent);
            g.FillEllipse(dot, inner);
        }

        g.SmoothingMode = prevSmoothing;

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
