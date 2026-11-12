using System.ComponentModel;
using System.Drawing.Drawing2D;
using APS.Controls.Theme;

namespace APS.Controls.Vacuum;

[ToolboxItem(false)]
public class ChamberControl : ThemedVacuumControl
{
    private string _label = "Chamber";
    private float _wallPercent = 0.75f;
    private int _topAnchors = 2;
    private int _bottomAnchors = 1;
    private int _leftAnchors = 1;
    private int _rightAnchors = 1;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float WidthPercent { get; set; } = 20f;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float HeightPercent { get; set; } = 20f;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Label
    {
        get => _label;
        set { _label = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float WallPercent
    {
        get => _wallPercent;
        set { _wallPercent = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int TopAnchors
    {
        get => _topAnchors;
        set { _topAnchors = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int BottomAnchors
    {
        get => _bottomAnchors;
        set { _bottomAnchors = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int LeftAnchors
    {
        get => _leftAnchors;
        set { _leftAnchors = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int RightAnchors
    {
        get => _rightAnchors;
        set { _rightAnchors = value; Invalidate(); }
    }

    public ChamberControl(string id)
    {
        ID = id;
        this.Name = $"CHAMBER_{id}";
        DoubleBuffered = true;
        CalculateSize();
    }

    protected override void CalculateSize()
    {
        int s = (int)(200 * BaseScale);
        Size = new Size(s, s);
    }

    protected override IReadOnlyList<AnchorDef> DefineAnchors()
    {
        var anchors = new List<AnchorDef>();
        int w = Width, h = Height;

        for (int i = 1; i <= _topAnchors; i++)
        {
            float frac = i / (float)(_topAnchors + 1);
            anchors.Add(new AnchorDef($"top_{i}", new Point((int)(w * frac), 0)));
        }
        for (int i = 1; i <= _bottomAnchors; i++)
        {
            float frac = i / (float)(_bottomAnchors + 1);
            anchors.Add(new AnchorDef($"bottom_{i}", new Point((int)(w * frac), h)));
        }
        for (int i = 1; i <= _leftAnchors; i++)
        {
            float frac = i / (float)(_leftAnchors + 1);
            anchors.Add(new AnchorDef($"left_{i}", new Point(0, (int)(h * frac))));
        }
        for (int i = 1; i <= _rightAnchors; i++)
        {
            float frac = i / (float)(_rightAnchors + 1);
            anchors.Add(new AnchorDef($"right_{i}", new Point(w, (int)(h * frac))));
        }
        return anchors;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        float wallPx = Math.Max(4, _wallPercent / 100f * Width * 5f);
        var outerRect = new RectangleF(0, 0, Width, Height);
        var innerRect = new RectangleF(wallPx, wallPx, Width - 2 * wallPx, Height - 2 * wallPx);

        var outerIntRect = new Rectangle(0, 0, Width, Height);
        if (outerIntRect.Width > 0 && outerIntRect.Height > 0)
        {
            using var metalBrush = VacuumTheme.CreateMetalGradient(outerIntRect);
            g.FillRectangle(metalBrush, outerRect);
        }

        using (var outerPen = new Pen(VacuumTheme.MetalEdge, 1.0f))
            g.DrawRectangle(outerPen, outerRect.X, outerRect.Y, outerRect.Width - 1, outerRect.Height - 1);

        if (innerRect.Width > 0 && innerRect.Height > 0)
        {
            using var cavityBrush = new SolidBrush(VacuumTheme.CavityDark);
            g.FillRectangle(cavityBrush, innerRect);
            using var innerPen = new Pen(Color.FromArgb(100, VacuumTheme.MetalDark), 0.8f);
            g.DrawRectangle(innerPen, innerRect.X, innerRect.Y, innerRect.Width, innerRect.Height);
        }

        if (!string.IsNullOrEmpty(_label))
        {
            using var brush = new SolidBrush(VacuumTheme.TextPrimary);
            float fontSize = 9f;
            var font = new Font("Segoe UI", fontSize, FontStyle.Bold);
            var textSize = g.MeasureString(_label, font);
            while ((textSize.Width > innerRect.Width - 4 || textSize.Height > innerRect.Height - 4) && fontSize > 5f)
            {
                font.Dispose();
                fontSize -= 0.5f;
                font = new Font("Segoe UI", fontSize, FontStyle.Bold);
                textSize = g.MeasureString(_label, font);
            }
            float tx = innerRect.X + (innerRect.Width - textSize.Width) / 2f;
            float ty = innerRect.Y + (innerRect.Height - textSize.Height) / 2f;
            g.DrawString(_label, font, brush, tx, ty);
            font.Dispose();
        }
    }
}
