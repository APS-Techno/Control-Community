using System.ComponentModel;
using APS.Controls.Theme;
using APS.Controls.Core;

namespace APS.Controls.General;

[ToolboxItem(false)]
public class LedControl : ThemedVacuumControl
{
    #region [Private fields]
    private const int DefaultRWidth = 12;
    private const int DefaultRHeight = 12;
    private const int DefaultTextSize = 10;

    private Color ledColor = Color.Red;
    private Color _color;

    private bool _on = false;
    private bool _alignRight = false;
    private float scale = 1.0f;
    private Font? _cachedFont;
    private int _cachedFontSize = -1;

    #endregion

    #region [Properties]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color LedColor { get => ledColor; set => ledColor = value; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BGColor
    {
        get => BackColor;
        set => BackColor = value;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Align { set => SetAlign(value); }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float VScale
    {
        set
        {
            scale = value;
            CalculateSize();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool On
    {
        get => _on;
        set
        {
            bool wasOff = !_on;
            _on = value;
            if (_on)
            {
                _color = ledColor;
                if (wasOff)
                    PulseGlow(ledColor, 400);
            }
            else
            {
                _color = IsDark
                    ? Color.FromArgb(ledColor.R / 6, ledColor.G / 6, ledColor.B / 6)
                    : Color.LightBlue;
            }
            Invalidate();
        }
    }
    #endregion

    public LedControl(string name, DeviceType target)
    {
        ID = name;
        this.Name = GUIConst.ControlName(target, name);
        _color = IsDark
            ? Color.FromArgb(ledColor.R / 6, ledColor.G / 6, ledColor.B / 6)
            : Color.LightBlue;
        BackColor = IsDark ? VacuumTheme.BackgroundDark : Color.White;
        CalculateSize();
    }

    #region [Sizing]
    protected override void CalculateSize()
    {
        // No BaseScale on purpose: LedControl never tracked the canvas; fixed design-pixel
        // size. The point size is rendered as a pixel-unit font (pt × 4/3) to stay DPI-neutral.
        int pt = (int)Math.Round(DefaultTextSize * scale);
        if (pt > 12) pt = 12;
        if (pt < 1) pt = 1;

        if (_cachedFont == null || _cachedFontSize != pt)
        {
            _cachedFont?.Dispose();
            _cachedFont = new Font("Arial", pt * 4f / 3f, FontStyle.Regular, GraphicsUnit.Pixel);
            _cachedFontSize = pt;
        }

        var textSize = TextRenderer.MeasureText(ID ?? "", _cachedFont);
        int W = (int)Math.Round(DefaultRWidth * scale);
        int H = (int)Math.Round(DefaultRHeight * scale);
        if (H < textSize.Height) H = textSize.Height;
        Size = new Size(3 + W + textSize.Width + 10, H + 6);
    }
    #endregion

    #region [Private methods]
    private void SetAlign(string value)
    {
        _alignRight = value == "right";
    }

    protected override void OnConnectedChanged()
    {
        if (!Connected)
            _color = IsDark
                ? Color.FromArgb(80, 80, 100)
                : Color.Gray;
        else if (_on)
            _color = ledColor;
        else
            _color = IsDark
                ? Color.FromArgb(ledColor.R / 6, ledColor.G / 6, ledColor.B / 6)
                : Color.LightBlue;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        const int padding = 3;
        int W = (int)Math.Round(DefaultRWidth * scale);
        int H = (int)Math.Round(DefaultRHeight * scale);

        int pt = (int)Math.Round(DefaultTextSize * scale);
        if (pt > 12) pt = 12;
        if (pt < 1) pt = 1;
        if (_cachedFont == null || _cachedFontSize != pt)
        {
            _cachedFont?.Dispose();
            _cachedFont = new Font("Arial", pt * 4f / 3f, FontStyle.Regular, GraphicsUnit.Pixel);
            _cachedFontSize = pt;
        }

        var textSize = TextRenderer.MeasureText(ID ?? "", _cachedFont);
        if (H < textSize.Height) H = textSize.Height;

        int ledLeft;
        Point textLocation;

        if (_alignRight)
        {
            ledLeft = padding;
            textLocation = new Point(W + 2 * padding, padding);
        }
        else
        {
            textLocation = new Point(0, padding);
            ledLeft = textSize.Width;
        }

        int ledTop = padding + (H - (int)Math.Round(DefaultRHeight * scale)) / 2;
        int ledH = (int)Math.Round(DefaultRHeight * scale);

        var ledRect = new Rectangle(ledLeft, ledTop, W, ledH);
        DrawLedIndicator(e.Graphics, ledRect, _color, _on, PulseIntensity);

        Color textColor = IsDark ? VacuumTheme.TextPrimary : Color.Black;
        if (_on && IsDark)
        {
            using var glowBrush = new SolidBrush(Color.FromArgb(40, ledColor));
            var glowRect = new Rectangle(textLocation.X - 2, textLocation.Y - 1,
                textSize.Width + 4, textSize.Height + 2);
            e.Graphics.FillRectangle(glowBrush, glowRect);
        }

        TextRenderer.DrawText(e.Graphics, ID ?? "", _cachedFont, textLocation, textColor);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cachedFont?.Dispose();
            _cachedFont = null;
        }
        base.Dispose(disposing);
    }
    #endregion
}
