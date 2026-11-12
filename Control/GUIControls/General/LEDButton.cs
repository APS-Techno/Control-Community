using System.ComponentModel;
using APS.Controls.Theme;

namespace APS.SystemControl.Controls.General;
[ToolboxItem(false)]
public class LedButton : Button
{
    private bool _selected;

    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected != value)
            {
                _selected = value;
                this.Invalidate();
            }
        }
    }

    public Color Color { get; set; } = SystemColors.ButtonFace;

    public Color TextColor { get; set; } = SystemColors.ControlText;

    public Color BorderColor { get; set; } = SystemColors.ActiveBorder;

    public Color LedColor { get; set; } = Color.Red;
    public bool LedEnabled { get; set; } = true;

    /// <summary>Dark mode only. Set from a panel's config (<c>&lt;buttonColor&gt;</c>)
    /// so only that panel's buttons change.</summary>
    public Color? FaceColor { get; set; }

    private bool IsDark => VacuumTheme.IsDark;

    // Rebuilt lazily when the source Font changes so we never allocate per-paint.
    private Font? _boldFont;
    private Font? _boldFontSource;
    private static readonly StringFormat NearCenter = new()
    {
        Alignment = StringAlignment.Near,
        LineAlignment = StringAlignment.Center,
    };
    private static readonly StringFormat CenterCenter = new()
    {
        Alignment = StringAlignment.Center,
        LineAlignment = StringAlignment.Center,
    };

    public LedButton()
    {
        this.ResizeRedraw = true;
    }

    private Font GetBoldFont()
    {
        if (_boldFont is null || !ReferenceEquals(_boldFontSource, Font))
        {
            _boldFont?.Dispose();
            _boldFont = new Font(Font, FontStyle.Bold);
            _boldFontSource = Font;
        }
        return _boldFont;
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        _boldFont?.Dispose();
        _boldFont = null;
        _boldFontSource = null;
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

    protected override void OnPaint(PaintEventArgs pevent)
    {
        base.OnPaint(pevent);

        Color bgColor = IsDark ? (FaceColor ?? VacuumTheme.MetalDark) : this.Color;
        pevent.Graphics.Clear(bgColor);

        StringFormat stringFormat = LedEnabled ? NearCenter : CenterCenter;

        // Scale the 20-px offset (96-DPI base) so the LED and text stay aligned at HiDPI.
        float dpi = pevent.Graphics.DpiY / 96f;
        int textOffset = LedEnabled ? (int)(20 * dpi) : 0;
        Rectangle textRect = new(textOffset, 0, this.Width - textOffset, this.Height);

        Color resolvedTextColor = IsDark ? VacuumTheme.TextPrimary : TextColor;

        Font textFont = TextColor != SystemColors.ControlText ? GetBoldFont() : this.Font;

        using (var textBrush = new SolidBrush(resolvedTextColor))
        {
            pevent.Graphics.DrawString(this.Text, textFont, textBrush, textRect, stringFormat);
        }

        if (LedEnabled)
        {
            int ledX = (int)(5 * dpi);
            int ledD = (int)(10 * dpi);
            Rectangle ledBounds = new(ledX, (this.Height - ledD) / 2, ledD, ledD);
            ThemedVacuumControl.DrawLedIndicator(pevent.Graphics, ledBounds, LedColor, _selected);
        }

        ControlPaint.DrawBorder(pevent.Graphics, this.ClientRectangle, BorderColor, ButtonBorderStyle.Solid);
    }
}
