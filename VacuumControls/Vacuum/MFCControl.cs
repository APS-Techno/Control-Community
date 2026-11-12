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
    private float _cachedBaseScale = -1f;

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

        EnsureFonts();

        var bounds = new Rectangle(0, 0, w - 1, h - 1);

        DrawMetalHousing(g, bounds, 5);

        if (w > 40 && h > 40)
            DrawScrews(g, bounds, 7);

        int inset = Math.Max(6, (int)(8 * BaseScale));
        int gasStripH = Math.Max(14, (int)(16 * BaseScale));
        int lcdTop = inset;
        int lcdBottom = h - inset - gasStripH - Math.Max(2, (int)(3 * BaseScale));
        var lcdRect = new Rectangle(inset, lcdTop, w - inset * 2, lcdBottom - lcdTop);

        if (lcdRect.Width > 4 && lcdRect.Height > 4)
            DrawMfcLcd(g, lcdRect);

        var gasStripRect = new Rectangle(
            inset, h - inset - gasStripH,
            w - inset * 2, gasStripH);
        DrawGasStrip(g, gasStripRect);

        using var accentPen = new Pen(FrameColor, 1.5f);
        using var path = CreateRoundedRect(bounds, 5);
        g.DrawPath(accentPen, path);
    }

    private void DrawMfcLcd(Graphics g, Rectangle lcdRect)
    {
        using (var bezelBrush = new SolidBrush(Color.FromArgb(17, 17, 17)))
            g.FillRectangle(bezelBrush, lcdRect);
        using (var bezelPen = new Pen(VacuumTheme.BorderSecondary, 1.5f))
            g.DrawRectangle(bezelPen, lcdRect);

        var screenRect = new Rectangle(lcdRect.X + 3, lcdRect.Y + 3,
            lcdRect.Width - 6, lcdRect.Height - 6);
        if (screenRect.Width < 2 || screenRect.Height < 2) return;

        using (var lcdBg = VacuumTheme.CreateLcdBackground(screenRect))
            g.FillRectangle(lcdBg, screenRect);

        if (!string.IsNullOrEmpty(_caption) && _captionFont != null)
        {
            using var captionBrush = new SolidBrush(Color.FromArgb(0, 170, 102));
            var captionSize = g.MeasureString(_caption, _captionFont);
            float captionX = screenRect.X + (screenRect.Width - captionSize.Width) / 2f;
            float captionY = screenRect.Y + 1;
            g.DrawString(_caption, _captionFont, captionBrush, captionX, captionY);
        }

        if (_valueFont != null)
        {
            using var valueBrush = new SolidBrush(_fontColor);
            var valueSize = g.MeasureString(_value, _valueFont);
            float valueX = screenRect.X + (screenRect.Width - valueSize.Width) / 2f;
            float valueY = !string.IsNullOrEmpty(_caption)
                ? screenRect.Y + 13 * BaseScale
                : screenRect.Y + (screenRect.Height - valueSize.Height) / 2f;
            g.DrawString(_value, _valueFont, valueBrush, valueX, valueY);
        }
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

    private void EnsureFonts()
    {
        // Keyed on BaseScale only; DPI does not affect widget-internal fonts.
        // Pixel-unit fonts: legacy pt × 4/3 so 96-DPI rendering is unchanged.
        if (_captionFont != null && Math.Abs(_cachedBaseScale - BaseScale) <= 0.001f) return;

        _captionFont?.Dispose();
        _valueFont?.Dispose();
        _gasFont?.Dispose();

        _captionFont = new Font("Consolas", 7f * 4f / 3f * BaseScale, FontStyle.Regular, GraphicsUnit.Pixel);
        _valueFont   = new Font("Consolas", 12f * 4f / 3f * BaseScale, FontStyle.Bold,    GraphicsUnit.Pixel);
        _gasFont     = new Font("Consolas", 8f  * 4f / 3f * BaseScale, FontStyle.Bold,    GraphicsUnit.Pixel);
        _cachedBaseScale = BaseScale;
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
