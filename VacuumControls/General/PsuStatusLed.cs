using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace APS.Controls.General;

public class PsuStatusLed : Control
{
    private static readonly Font CaptionFont = new("Segoe UI", 7.5f, FontStyle.Bold);

    private bool _ledOn;
    private string _caption = "";

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool LedOn
    {
        get => _ledOn;
        set { if (_ledOn == value) return; _ledOn = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Caption
    {
        get => _caption;
        set { _caption = value ?? ""; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color OnColor { get; set; } = Color.LimeGreen;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color OffColor { get; set; } = Color.FromArgb(60, 60, 60);

    public PsuStatusLed()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.UserPaint, true);
        Size = new Size(40, 14);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var ledRect = new Rectangle(0, (Height - 10) / 2, 10, 10);
        using var brush = new SolidBrush(_ledOn ? OnColor : OffColor);
        g.FillEllipse(brush, ledRect);
        if (!string.IsNullOrEmpty(_caption))
        {
            using var textBrush = new SolidBrush(ForeColor);
            g.DrawString(_caption, CaptionFont, textBrush, 12, (Height - CaptionFont.Height) / 2f);
        }
    }
}
