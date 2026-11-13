using System.ComponentModel;
using System.Drawing.Drawing2D;
using APS.Controls.Theme;
using APS.Controls.Core;

namespace APS.Controls.Vacuum;

/// <summary>A named, operator-selectable stage position (mm). Core-agnostic
/// mirror of the host's stage-preset model, pushed in via <see cref="StageControl.SetPresets"/>.</summary>
public sealed record StagePreset(string Name, float Position);

/// <summary>
/// Arbitrary moves, jog, soft limits, preset editing and the
/// service-only <i>Seek Home</i> action live in the extended-options popup,
/// opened by double-clicking the body (or the menu's <i>Open Panel…</i> item).
/// </summary>
[ToolboxItem(false)]
public class StageControl : ThemedVacuumControl
{
    private const int DefaultWidth = 100;
    private const int DefaultHeight = 104;

    private readonly string _id;
    private readonly Button _moveButton;
    private readonly Button _stopButton;
    private readonly ContextMenuStrip _moveMenu;
    private readonly ToolTip _tips = new();

    private float _height;
    private string _displayValue = "00.0";
    private float _scale = 1.0f;

    private Font? _labelFont;
    private Font? _valueFont;

    private Action<string, float>? _sendHWMessage;
    private Action<string>? _sendStop;
    private Action<string>? _openSettings;
    private IReadOnlyList<StagePreset> _presets = [];

    /// <summary>Current stage position in mm.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float StageHeight
    {
        get => _height;
        set
        {
            _height = value;
            _displayValue = value.ToString("F1");
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Action<string, float>? SendHWMessage { set => _sendHWMessage = value; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Action<string>? SendStop { set => _sendStop = value; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Action<string>? OpenSettings { set => _openSettings = value; }

    public void SetPresets(IReadOnlyList<StagePreset> presets) => _presets = presets ?? [];

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float VScale
    {
        set
        {
            _scale = value;
            CalculateSize();
        }
    }

    public StageControl(string Id)
    {
        _id = Id;
        ID = Id;
        this.Name = GUIConst.ControlName(DeviceType.Stage, Id);

        _moveMenu = new ContextMenuStrip
        {
            BackColor = VacuumTheme.MetalDark,
            ForeColor = VacuumTheme.TextPrimary,
            ShowImageMargin = false,
        };

        // Move → opens the preset / Home menu. Arbitrary entry is intentionally
        // absent here; it lives in the popup (double-click / Open Panel…).
        // Glyph "⇅" = go to a position; tooltip keeps the meaning discoverable.
        _moveButton = MakeButton("⇅", VacuumTheme.GlowBlue);
        _moveButton.Click += MoveButtonClick!;

        // Stop → immediate halt. Glyph "■" = the universal stop square.
        _stopButton = MakeButton("■", VacuumTheme.GlowRed);
        _stopButton.Click += (_, _) => _sendStop?.Invoke(_id);

        _tips.SetToolTip(_moveButton, "Move to preset…");
        _tips.SetToolTip(_stopButton, "Stop");

        Controls.Add(_moveButton);
        Controls.Add(_stopButton);

        this.DoubleClick += (_, _) => _openSettings?.Invoke(_id);

        CalculateSize();
    }

    private static Button MakeButton(string text, Color accent)
    {
        var b = new Button
        {
            Text = "",              // glyph is owner-drawn in DrawGlyph, not via Text
            Tag = text,             // carries the glyph string for the paint handler
            FlatStyle = FlatStyle.Flat,
            BackColor = VacuumTheme.MetalDark,
            ForeColor = accent,
            Cursor = Cursors.Hand,
        };
        b.FlatAppearance.BorderColor = accent;
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(40, 40, 60);
        b.FlatAppearance.MouseDownBackColor = Color.FromArgb(20, 50, 80);
        b.Paint += DrawGlyph;
        return b;
    }

    private static readonly FontFamily GlyphFamily = new("Segoe UI Symbol");

    /// <summary>
    /// Paints the button's glyph optically centred on its <b>ink</b> bounds
    /// rather than the font line box — WinForms' text centring leaves symbol
    /// glyphs (⇅, ■) sitting low because their empty ascent is counted. The
    /// glyph is scaled to the button height so it stays centred at any DPI/scale.
    /// </summary>
    private static void DrawGlyph(object? sender, PaintEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string glyph || glyph.Length == 0) return;

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        float em = Math.Max(6f, b.Height * 0.55f);
        using var path = new GraphicsPath();
        path.AddString(glyph, GlyphFamily, (int)FontStyle.Regular, em,
            new PointF(0, 0), StringFormat.GenericTypographic);

        var ink = path.GetBounds();
        if (ink.Width <= 0.01f || ink.Height <= 0.01f) return;

        float dx = (b.Width - ink.Width) / 2f - ink.Left;
        float dy = (b.Height - ink.Height) / 2f - ink.Top;
        using var m = new Matrix();
        m.Translate(dx, dy);
        path.Transform(m);

        using var brush = new SolidBrush(b.ForeColor);
        g.FillPath(brush, path);
    }

    public readonly record struct StageLayout(
        int Inset, Rectangle Label, Rectangle Lcd, Rectangle Screen, Rectangle Move, Rectangle Stop);

    public static StageLayout ComputeLayout(int w, int h, float sf)
    {
        int inset = Math.Max(3, (int)Math.Round(7 * sf));
        int gap = Math.Max(2, (int)Math.Round(3 * sf));
        int labelStripH = Math.Max(9, (int)Math.Round(15 * sf));
        int btnH = Math.Max(12, (int)Math.Round(20 * sf));
        int bezel = Math.Max(1, (int)Math.Round(2.5f * sf));

        int innerW = w - inset * 2;
        var label = new Rectangle(inset, inset, innerW, labelStripH);

        int btnY = h - inset - btnH;
        int moveW = (innerW - gap) / 2;
        var move = new Rectangle(inset, btnY, moveW, btnH);
        var stop = new Rectangle(inset + moveW + gap, btnY, innerW - moveW - gap, btnH);

        int lcdTop = label.Bottom + gap;
        var lcd = new Rectangle(inset, lcdTop, innerW, btnY - gap - lcdTop);
        var screen = Rectangle.Inflate(lcd, -bezel, -bezel);

        return new StageLayout(inset, label, lcd, screen, move, stop);
    }

    protected override void CalculateSize()
    {
        float sf = BaseScale * _scale;
        Width = (int)(DefaultWidth * sf);
        Height = (int)(DefaultHeight * sf);
        LayoutButtons();
    }

    private void LayoutButtons()
    {
        var L = ComputeLayout(Width, Height, BaseScale * _scale);
        _moveButton.Bounds = L.Move;
        _stopButton.Bounds = L.Stop;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;

        int w = ClientSize.Width;
        int h = ClientSize.Height;
        if (w < 8 || h < 8) return;

        float sf = BaseScale * _scale;
        var L = ComputeLayout(w, h, sf);
        EnsureFonts(sf, L.Screen.Height);

        var bounds = new Rectangle(0, 0, w - 1, h - 1);

        DrawMetalHousing(g, bounds, 5);

        if (L.Inset >= 8 && w >= 72 && h >= 72)
            DrawScrews(g, bounds, L.Inset / 2);

        DrawLabelStrip(g, L.Label);

        if (L.Lcd.Width > 4 && L.Lcd.Height > 4)
            DrawStageLcd(g, L.Lcd, L.Screen);

        using var accentPen = new Pen(VacuumTheme.GlowBlue, 1.5f);
        using var path = CreateRoundedRect(bounds, 5);
        g.DrawPath(accentPen, path);
    }

    private void DrawLabelStrip(Graphics g, Rectangle stripRect)
    {
        using (var bgBrush = new SolidBrush(Color.FromArgb(26, 26, 46)))
            g.FillRectangle(bgBrush, stripRect);
        using (var borderPen = new Pen(VacuumTheme.BorderSecondary, 1f))
            g.DrawRectangle(borderPen, stripRect);

        if (_labelFont != null)
        {
            string label = _id;
            Color labelColor = IsDark ? VacuumTheme.GlowBlue : Color.DarkBlue;
            using var labelBrush = new SolidBrush(labelColor);
            var textSize = g.MeasureString(label, _labelFont);
            float textX = stripRect.X + (stripRect.Width - textSize.Width) / 2f;
            float textY = stripRect.Y + (stripRect.Height - textSize.Height) / 2f;
            g.DrawString(label, _labelFont, labelBrush, textX, textY);
        }
    }

    private void DrawStageLcd(Graphics g, Rectangle lcdRect, Rectangle screenRect)
    {
        using (var bezelBrush = new SolidBrush(Color.FromArgb(17, 17, 17)))
            g.FillRectangle(bezelBrush, lcdRect);
        using (var bezelPen = new Pen(VacuumTheme.BorderSecondary, 1.5f))
            g.DrawRectangle(bezelPen, lcdRect);

        if (screenRect.Width < 2 || screenRect.Height < 2) return;

        using (var lcdBg = VacuumTheme.CreateLcdBackground(screenRect))
            g.FillRectangle(lcdBg, screenRect);

        var state = g.Save();
        g.SetClip(screenRect, CombineMode.Intersect);

        if (_valueFont != null)
        {
            using var valueBrush = new SolidBrush(VacuumTheme.LcdForeground);
            var valueSize = g.MeasureString(_displayValue, _valueFont);
            float valueX = screenRect.X + (screenRect.Width - valueSize.Width) / 2f;
            float valueY = screenRect.Y + (screenRect.Height - valueSize.Height) / 2f;
            g.DrawString(_displayValue, _valueFont, valueBrush, valueX, valueY);
        }

        if (_labelFont != null && screenRect.Width >= 46 && screenRect.Height >= 18)
        {
            using var unitBrush = new SolidBrush(VacuumTheme.TextDim);
            string unit = "mm";
            var unitSize = g.MeasureString(unit, _labelFont);
            g.DrawString(unit, _labelFont, unitBrush,
                screenRect.Right - unitSize.Width - 2,
                screenRect.Bottom - unitSize.Height - 1);
        }

        g.Restore(state);
    }

    private void EnsureFonts(float sf, int screenH)
    {
        float labelPx = Math.Max(6f, 7.5f * 4f / 3f * sf);
        float valuePx = Math.Clamp(12f * 4f / 3f * sf, 6f, Math.Max(6f, screenH * 0.72f));
        if (_labelFont != null && _valueFont != null
            && Math.Abs(_labelFont.Size - labelPx) <= 0.001f
            && Math.Abs(_valueFont.Size - valuePx) <= 0.001f) return;

        _labelFont?.Dispose();
        _valueFont?.Dispose();

        _labelFont = new Font("Consolas", labelPx, FontStyle.Bold, GraphicsUnit.Pixel);
        _valueFont = new Font("Consolas", valuePx, FontStyle.Bold, GraphicsUnit.Pixel);
    }

    private void MoveButtonClick(object sender, EventArgs e)
    {
        // Dispose previous items so repeated opens don't leak ToolStripItems.
        while (_moveMenu.Items.Count > 0)
        {
            var it = _moveMenu.Items[0];
            _moveMenu.Items.RemoveAt(0);
            it.Dispose();
        }

        foreach (var p in _presets)
        {
            var item = new ToolStripMenuItem($"{p.Name}  ({p.Position:F1})");
            float pos = p.Position;
            item.Click += (_, _) => _sendHWMessage?.Invoke(_id, pos);
            _moveMenu.Items.Add(item);
        }

        if (_moveMenu.Items.Count > 0)
            _moveMenu.Items.Add(new ToolStripSeparator());

        var panel = new ToolStripMenuItem("Open Panel…");
        panel.Click += (_, _) => _openSettings?.Invoke(_id);
        _moveMenu.Items.Add(panel);

        _moveMenu.Show(_moveButton, new Point(0, _moveButton.Height));
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutButtons();
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _labelFont?.Dispose();
            _valueFont?.Dispose();
            _labelFont = null;
            _valueFont = null;
            _moveButton.Click -= MoveButtonClick!;
            _moveMenu.Dispose();
            _tips.Dispose();
        }
        base.Dispose(disposing);
    }
}
