using System.Drawing;
using System.Windows.Forms;

namespace APS.Controls.Theme;

/// <summary>
/// The stock WinForms ToolTip renders system light/yellow. ScaledFont lives in Control and
/// cannot be referenced from VacuumControls, so we use Segoe UI 9pt directly.
/// </summary>
public sealed class DarkToolTip : ToolTip
{
    private static readonly Font HintFont = new("Segoe UI", 9f); // process-lifetime; intentionally not disposed

    public DarkToolTip()
    {
        OwnerDraw = true;
        ShowAlways = true;
        InitialDelay = 500;
        AutoPopDelay = 15000;
        ReshowDelay = 100;
        Draw += OnDraw;
        Popup += OnPopup;
    }

    private void OnPopup(object? sender, PopupEventArgs e)
    {
        string text = e.AssociatedControl is { } ctl ? GetToolTip(ctl) ?? string.Empty : string.Empty;
        Size sz = TextRenderer.MeasureText(text, HintFont);
        e.ToolTipSize = new Size(sz.Width + 2 * DpiScale.Px(8), sz.Height + 2 * DpiScale.Px(5));
    }

    private void OnDraw(object? sender, DrawToolTipEventArgs e)
    {
        int padX = DpiScale.Px(8), padY = DpiScale.Px(5);
        Graphics g = e.Graphics;
        using (var bg = new SolidBrush(VacuumTheme.PanelBackground))
            g.FillRectangle(bg, e.Bounds);
        using (var border = new Pen(VacuumTheme.PanelBorder))
            g.DrawRectangle(border, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
        var textRect = new Rectangle(padX, padY, e.Bounds.Width - 2 * padX, e.Bounds.Height - 2 * padY);
        TextRenderer.DrawText(g, e.ToolTipText, HintFont, textRect, VacuumTheme.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.Top);
    }
}
