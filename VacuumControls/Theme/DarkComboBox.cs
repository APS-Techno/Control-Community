using System.Runtime.InteropServices;

namespace APS.Controls.Theme;

/// <summary>
/// Paints the dropdown button dark; the classic Windows gray arrow area would
/// otherwise show on a dark-themed form.
/// </summary>
public sealed class DarkComboBox : ComboBox
{
    private static readonly Color BgInput = Color.FromArgb(18, 30, 48);
    private static readonly Color Border = Color.FromArgb(40, 60, 90);
    private static readonly Color TextPrimary = Color.FromArgb(200, 214, 229);

    public DarkComboBox()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        FlatStyle = FlatStyle.Flat;
        BackColor = BgInput;
        ForeColor = TextPrimary;
        // DropDownStyle stays at the default (editable DropDown); callers that need
        // DropDownList set it explicitly.
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var bg = (e.State & DrawItemState.Selected) == DrawItemState.Selected
            ? Color.FromArgb(40, 60, 90)
            : BgInput;
        using var brush = new SolidBrush(bg);
        e.Graphics.FillRectangle(brush, e.Bounds);
        TextRenderer.DrawText(
            e.Graphics,
            Items[e.Index]?.ToString() ?? "",
            Font,
            e.Bounds,
            TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
    }

    private const int WM_PAINT = 0x000F;

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == WM_PAINT)
        {
            using var g = Graphics.FromHwnd(Handle);
            int btnW = SystemInformation.VerticalScrollBarWidth + 4;
            var btnRect = new Rectangle(ClientSize.Width - btnW, 0, btnW, ClientSize.Height);
            using (var bg = new SolidBrush(BgInput)) g.FillRectangle(bg, btnRect);
            using (var pen = new Pen(Border))
            {
                g.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            }
            var cx = btnRect.Left + btnRect.Width / 2;
            var cy = btnRect.Height / 2;
            var arrow = new[]
            {
                new Point(cx - 4, cy - 2),
                new Point(cx + 4, cy - 2),
                new Point(cx, cy + 3),
            };
            using var arrowBrush = new SolidBrush(TextPrimary);
            g.FillPolygon(arrowBrush, arrow);
        }
    }
}
