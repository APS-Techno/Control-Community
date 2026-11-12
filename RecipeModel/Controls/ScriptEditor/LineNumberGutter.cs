using APS.Controls.Theme;

namespace APS.RecipeModel.Controls.ScriptEditor;

public sealed class LineNumberGutter : Control
{
    private readonly RichTextBox _editor;
    private const int PaddingLeft = 4;
    private const int PaddingRight = 6;

    public LineNumberGutter(RichTextBox editor)
    {
        _editor = editor;
        DoubleBuffered = true;
        TabStop = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Width = ComputeWidth(1, _editor.Font);
        BackColor = _editor.BackColor;
    }

    public static int DigitCount(int value)
    {
        if (value < 10) return 1;
        if (value < 100) return 2;
        if (value < 1000) return 3;
        if (value < 10000) return 4;
        if (value < 100000) return 5;
        if (value < 1000000) return 6;
        return value.ToString().Length;
    }

    private static int ComputeWidth(int maxLine, Font font)
    {
        int digits = DigitCount(Math.Max(1, maxLine));
        int charWidth = TextRenderer.MeasureText("0", font).Width;
        return PaddingLeft + digits * charWidth + PaddingRight;
    }

    public bool EnsureWidth()
    {
        int lines = Math.Max(1, _editor.Lines.Length);
        int w = ComputeWidth(lines, _editor.Font);
        if (w == Width) return false;
        Width = w;
        return true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        g.Clear(_editor.BackColor);

        if (_editor.Lines.Length == 0)
        {
            DrawSeparator(g);
            return;
        }

        int firstVisibleChar = _editor.GetCharIndexFromPosition(new Point(1, 1));
        int firstVisibleLine = _editor.GetLineFromCharIndex(firstVisibleChar);
        int totalLines = _editor.Lines.Length;

        Color baseFg = _editor.ForeColor;
        using var fg = new SolidBrush(Color.FromArgb(140, baseFg.R, baseFg.G, baseFg.B));
        using var sf = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Far,
            LineAlignment = StringAlignment.Near,
            FormatFlags = StringFormatFlags.NoWrap
        };

        var font = _editor.Font;
        int clientH = ClientSize.Height;

        for (int line = firstVisibleLine; line < totalLines; line++)
        {
            int charIndex = _editor.GetFirstCharIndexFromLine(line);
            if (charIndex < 0) break;

            Point p = _editor.GetPositionFromCharIndex(charIndex);
            if (p.Y > clientH) break;

            var rect = new RectangleF(0, p.Y, Width - PaddingRight, font.Height + 2);
            g.DrawString((line + 1).ToString(), font, fg, rect, sf);
        }

        DrawSeparator(g);
    }

    private void DrawSeparator(Graphics g)
    {
        using var pen = new Pen(VacuumTheme.BorderSecondary, 1f);
        g.DrawLine(pen, Width - 1, 0, Width - 1, ClientSize.Height);
    }
}
