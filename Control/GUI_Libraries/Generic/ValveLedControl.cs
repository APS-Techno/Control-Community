using APS.Controls.Theme;
using APS.SystemControl.Controls.Theme;

namespace APS.Controls.Generic;

public sealed class ValveLedControl : UserControl
{
    private const int LedDiameter = 10;   // 96-DPI base
    private const int LedPadding  = 3;   // 96-DPI base
    private const int LabelGap    = 4;   // 96-DPI base

    private float Dpi => DeviceDpi / 96f;

    private static readonly Color LedOnFill     = Color.FromArgb(0, 255, 255);
    private static readonly Font  LabelFont     = ScaledFont.Create(9f);

    private readonly Label _label;
    private readonly ToolTip _tip = new();
    private readonly string _descr;

    private readonly Color _onFill;
    private readonly Color _onBorder;

    public string ValveId { get; }

    private bool _isOpen;
    public bool IsOpen
    {
        get => _isOpen;
        set
        {
            if (_isOpen == value) return;
            _isOpen = value;
            UpdateTooltip();
            Invalidate();
        }
    }

    public ValveLedControl(string valveId, string descr, Color? accent = null)
    {
        ValveId = valveId;
        _descr  = descr ?? string.Empty;

        _onFill   = accent ?? LedOnFill;
        _onBorder = Color.FromArgb(
            (int)(_onFill.R * 0.7f), (int)(_onFill.G * 0.7f), (int)(_onFill.B * 0.7f));

        DoubleBuffered = true;
        BackColor      = Color.Transparent;
        Margin         = new Padding(0, (int)(2 * Dpi), (int)(10 * Dpi), (int)(2 * Dpi));

        _label = new Label
        {
            Text      = valveId,
            Font      = LabelFont,
            ForeColor = VacuumTheme.TextPrimary,
            AutoSize  = true,
        };
        Controls.Add(_label);

        int ledD = (int)(LedDiameter * Dpi);
        int ledP = (int)(LedPadding  * Dpi);
        int lblG = (int)(LabelGap    * Dpi);
        _label.Location = new Point(ledP + ledD + lblG, 1);
        Height = Math.Max(ledD + ledP * 2, _label.PreferredHeight + 2);
        Width  = _label.Location.X + _label.PreferredWidth + 2;

        var tip = BuildTooltip();
        _tip.SetToolTip(_label, tip);
        _tip.SetToolTip(this, tip);
    }

    private string BuildTooltip()
        => string.IsNullOrEmpty(_descr)
            ? $"{ValveId} — {(IsOpen ? "OPEN" : "CLOSED")}"
            : $"{ValveId} — {_descr} — {(IsOpen ? "OPEN" : "CLOSED")}";

    private void UpdateTooltip()
    {
        var t = BuildTooltip();
        _tip.SetToolTip(_label, t);
        _tip.SetToolTip(this, t);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        float dpi = e.Graphics.DpiY / 96f;
        int ledD = (int)(LedDiameter * dpi);
        int ledP = (int)(LedPadding  * dpi);
        int cy = Height / 2;
        var rect = new Rectangle(ledP, cy - ledD / 2, ledD, ledD);

        Color fill   = IsOpen ? _onFill   : VacuumTheme.PanelField;
        Color border = IsOpen ? _onBorder : VacuumTheme.TextDim;

        using var fillBrush = new SolidBrush(fill);
        using var borderPen = new Pen(border, 1f);
        g.FillEllipse(fillBrush, rect);
        g.DrawEllipse(borderPen, rect);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tip.Dispose();
        base.Dispose(disposing);
    }
}
