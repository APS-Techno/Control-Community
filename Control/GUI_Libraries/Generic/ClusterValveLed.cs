using APS.Controls.Theme;
using APS.SystemControl.Controls.Theme;

namespace APS.Controls.Generic;

internal sealed class ClusterValveLed : UserControl
{
    private const int LedDiameter = 10;
    private const int LedPadding  = 3;
    private const int LabelGap    = 4;

    private static readonly Color LedOnFill   = Color.FromArgb(0, 255, 255);
    private static readonly Color LedOnBorder = Color.FromArgb(0, 180, 180);
    private static readonly Font  LabelFont   = ScaledFont.Create(9f);

    private readonly Label _label;
    private readonly ToolTip _tip = new();
    private readonly string _descr;

    public string ValveId { get; }

    private bool _isOpen;
    public bool IsOpen
    {
        get => _isOpen;
        set { if (_isOpen == value) return; _isOpen = value; UpdateTooltip(); Invalidate(); }
    }

    public ClusterValveLed(string valveId, string descr, string? displayText = null)
    {
        ValveId = valveId;
        _descr  = descr ?? string.Empty;

        DoubleBuffered = true;
        BackColor      = Color.Transparent;
        Margin         = new Padding(0, 2, 10, 2);

        _label = new Label
        {
            Text = displayText ?? valveId, Font = LabelFont,
            ForeColor = VacuumTheme.TextPrimary, AutoSize = true,
        };
        Controls.Add(_label);
        _label.Location = new Point(LedPadding + LedDiameter + LabelGap, 1);
        Height = Math.Max(LedDiameter + LedPadding * 2, _label.PreferredHeight + 2);
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

        int cy = Height / 2;
        var rect = new Rectangle(LedPadding, cy - LedDiameter / 2, LedDiameter, LedDiameter);
        Color fill   = IsOpen ? LedOnFill   : VacuumTheme.PanelField;
        Color border = IsOpen ? LedOnBorder : VacuumTheme.TextDim;
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
