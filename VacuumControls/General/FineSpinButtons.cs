using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace APS.Controls.General;

public class FineSpinButtons : FlowLayoutPanel
{
    private readonly ArrowButton _btnUp = new() { PointUp = true };
    private readonly ArrowButton _btnDown = new() { PointUp = false };

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public NumericTextBox? Target { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public decimal Step { get; set; } = 0.1m;

    public FineSpinButtons()
    {
        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
        AutoSize = true;
        Margin = new Padding(0);
        Padding = new Padding(0);
        Controls.Add(_btnUp);
        Controls.Add(_btnDown);
        _btnUp.Click += (_, _) => StepUp();
        _btnDown.Click += (_, _) => StepDown();
    }

    public void StepUp()
    {
        if (Target == null) return;
        decimal next = Math.Min(Target.Maximum, Target.Value + Step);
        if (next != Target.Value) Target.Value = next;
    }

    public void StepDown()
    {
        if (Target == null) return;
        decimal next = Math.Max(Target.Minimum, Target.Value - Step);
        if (next != Target.Value) Target.Value = next;
    }

    private sealed class ArrowButton : Button
    {
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool PointUp { get; set; }

        public ArrowButton()
        {
            Text = string.Empty;
            Size = new Size(16, 11);
            Margin = new Padding(0);
            Padding = new Padding(0);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 1;
            TabStop = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int cx = ClientSize.Width / 2;
            int cy = ClientSize.Height / 2;
            Point[] tri = PointUp
                ? new[] { new Point(cx - 3, cy + 2), new Point(cx + 3, cy + 2), new Point(cx, cy - 2) }
                : new[] { new Point(cx - 3, cy - 2), new Point(cx + 3, cy - 2), new Point(cx, cy + 2) };
            using var br = new SolidBrush(ForeColor);
            g.FillPolygon(br, tri);
        }
    }
}
