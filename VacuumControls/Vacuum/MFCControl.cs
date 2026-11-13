using System.ComponentModel;
using System.Drawing.Drawing2D;
using APS.Controls.Theme;
using APS.Controls.Core;
using APS.Controls.Dialogs;

namespace APS.Controls.Vacuum;

[ToolboxItem(false)]
public class MFCControl : ThemedVacuumControl
{
    private const int DefaultWidth = 90;
    private const int DefaultHeight = 80;

    private readonly FloatInputDialog _inputDialog;
    private readonly string _gas;

    private string _caption = string.Empty;
    private string _value = "0.00";
    private string _format = "F2";
    private Color _fontColor = VacuumTheme.LcdForeground;
    private float _scale = 1.0f;

    private Font? _captionFont;
    private Font? _valueFont;
    private Font? _gasFont;
    private float _cachedSf = -1f;

    private Action<string, float>? _sendHWMessage;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Caption
    {
        get => _caption;
        set { _caption = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Value
    {
        get => _value;
        set { _value = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Format
    {
        get => _format;
        set => _format = value;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color FontColor
    {
        get => _fontColor;
        set { _fontColor = value; Invalidate(); }
    }

    public string Gas => _gas;

    /// <summary>Invoked on double-click to send a setpoint to the hardware layer.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Action<string, float>? SendHWMessage { set => _sendHWMessage = value; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float VScale
    {
        set
        {
            _scale = value;
            CalculateSize();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color FrameColor { get; set; } = VacuumTheme.IsDark
        ? VacuumTheme.GlowBlue
        : Color.DarkBlue;

    public MFCControl(string name, string gas)
    {
        _gas = gas;
        ID = name;
        this.Name = GUIConst.ControlName(DeviceType.MFC, name);
        _inputDialog = new FloatInputDialog($"Enter {gas} flow (sccm):");

        SetStyle(ControlStyles.Selectable, true);
        CalculateSize();
    }

    public void UpdateState(float floatValue)
    {
        Value = floatValue.ToString(Format);
        Refresh();
    }

    protected override IReadOnlyList<AnchorDef> DefineAnchors()
    {
        return [
            new("left",   new Point(0, Height / 2)),
            new("right",  new Point(Width, Height / 2)),
            new("top",    new Point(Width / 2, 0)),
            new("bottom", new Point(Width / 2, Height))
        ];
    }

    public readonly record struct MfcLayout(
        int Inset, Rectangle Lcd, Rectangle Screen, RectangleF CaptionBand, RectangleF ValueBand, Rectangle GasStrip)
    {
        public bool ShowsCaption => !CaptionBand.IsEmpty;
    }

    public static MfcLayout ComputeLayout(int w, int h, float sf, bool hasCaption)
    {
        int inset = Math.Max(3, (int)Math.Round(6 * sf));
        int gap = Math.Max(1, (int)Math.Round(2.5f * sf));
        int gasStripH = Math.Max(9, (int)Math.Round(15 * sf));
        int bezel = Math.Max(1, (int)Math.Round(2.5f * sf));

        int lcdTop = inset;
        int lcdBottom = h - inset - gasStripH - gap;
        var lcd = new Rectangle(inset, lcdTop, w - inset * 2, lcdBottom - lcdTop);
        var screen = Rectangle.Inflate(lcd, -bezel, -bezel);
        var gasStrip = new Rectangle(inset, h - inset - gasStripH, w - inset * 2, gasStripH);

        var captionBand = RectangleF.Empty;
        RectangleF valueBand = screen;
        if (hasCaption && screen.Height >= 18)
        {
            float captionH = screen.Height * 0.38f;
            captionBand = new RectangleF(screen.X, screen.Y, screen.Width, captionH);
            valueBand = new RectangleF(screen.X, screen.Y + captionH, screen.Width, screen.Height - captionH);
        }

        return new MfcLayout(inset, lcd, screen, captionBand, valueBand, gasStrip);
    }

    protected override void CalculateSize()
    {
        float sf = BaseScale * _scale;
        Width = (int)(DefaultWidth * sf);
        Height = (int)(DefaultHeight * sf);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;

        int w = ClientSize.Width;
        int h = ClientSize.Height;
        if (w < 8 || h < 8) return;

        float sf = BaseScale * _scale;
        EnsureFonts(sf);
        var L = ComputeLayout(w, h, sf, !string.IsNullOrEmpty(_caption));

        var bounds = new Rectangle(0, 0, w - 1, h - 1);

        DrawMetalHousing(g, bounds, 5);

        if (L.Inset >= 8 && w >= 70 && h >= 70)
            DrawScrews(g, bounds, L.Inset / 2);

        if (L.Lcd.Width > 4 && L.Lcd.Height > 4)
            DrawMfcLcd(g, L);

        DrawGasStrip(g, L.GasStrip);

        using var accentPen = new Pen(FrameColor, 1.5f);
        using var path = CreateRoundedRect(bounds, 5);
        g.DrawPath(accentPen, path);
    }

    private void DrawMfcLcd(Graphics g, MfcLayout L)
    {
        using (var bezelBrush = new SolidBrush(Color.FromArgb(17, 17, 17)))
            g.FillRectangle(bezelBrush, L.Lcd);
        using (var bezelPen = new Pen(VacuumTheme.BorderSecondary, 1.5f))
            g.DrawRectangle(bezelPen, L.Lcd);

        var screenRect = L.Screen;
        if (screenRect.Width < 2 || screenRect.Height < 2) return;

        using (var lcdBg = VacuumTheme.CreateLcdBackground(screenRect))
            g.FillRectangle(lcdBg, screenRect);

        var state = g.Save();
        g.SetClip(screenRect, CombineMode.Intersect);

        if (L.ShowsCaption && _captionFont != null)
        {
            using var captionBrush = new SolidBrush(Color.FromArgb(0, 170, 102));
            DrawCentered(g, _caption, _captionFont, captionBrush, L.CaptionBand);
        }

        if (_valueFont != null)
        {
            using var valueBrush = new SolidBrush(_fontColor);
            DrawCentered(g, _value, _valueFont, valueBrush, L.ValueBand);
        }

        g.Restore(state);
    }

    private static void DrawCentered(Graphics g, string text, Font font, Brush brush, RectangleF band)
    {
        var size = g.MeasureString(text, font);
        g.DrawString(text, font, brush,
            band.X + (band.Width - size.Width) / 2f,
            band.Y + (band.Height - size.Height) / 2f);
    }

    private void DrawGasStrip(Graphics g, Rectangle stripRect)
    {
        using (var bgBrush = new SolidBrush(Color.FromArgb(26, 26, 46)))
            g.FillRectangle(bgBrush, stripRect);
        using (var borderPen = new Pen(VacuumTheme.BorderSecondary, 1f))
            g.DrawRectangle(borderPen, stripRect);

        if (!string.IsNullOrEmpty(_gas) && _gasFont != null)
        {
            Color gasTextColor = IsDark ? VacuumTheme.GlowBlue : Color.DarkBlue;
            using var gasBrush = new SolidBrush(gasTextColor);
            var textSize = g.MeasureString(_gas, _gasFont);
            float textX = stripRect.X + (stripRect.Width - textSize.Width) / 2f;
            float textY = stripRect.Y + (stripRect.Height - textSize.Height) / 2f;
            g.DrawString(_gas, _gasFont, gasBrush, textX, textY);
        }
    }

    private void EnsureFonts(float sf)
    {
        if (_captionFont != null && Math.Abs(_cachedSf - sf) <= 0.001f) return;

        _captionFont?.Dispose();
        _valueFont?.Dispose();
        _gasFont?.Dispose();

        _captionFont = new Font("Consolas", Math.Max(5.5f, 6.5f * 4f / 3f * sf), FontStyle.Regular, GraphicsUnit.Pixel);
        _valueFont   = new Font("Consolas", Math.Max(6.5f, 11f  * 4f / 3f * sf), FontStyle.Bold,    GraphicsUnit.Pixel);
        _gasFont     = new Font("Consolas", Math.Max(6f,   7.5f * 4f / 3f * sf), FontStyle.Bold,    GraphicsUnit.Pixel);
        _cachedSf = sf;
    }

    protected override void OnDoubleClick(EventArgs e)
    {
        base.OnDoubleClick(e);

        if (_inputDialog.ShowDialog() == DialogResult.OK)
        {
            float? enteredValue = _inputDialog.InputValue;
            if (enteredValue.HasValue)
            {
                _sendHWMessage?.Invoke(ID!, enteredValue.Value);
            }
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _captionFont?.Dispose();
            _valueFont?.Dispose();
            _gasFont?.Dispose();
            _captionFont = null;
            _valueFont = null;
            _gasFont = null;
        }
        base.Dispose(disposing);
    }
}
