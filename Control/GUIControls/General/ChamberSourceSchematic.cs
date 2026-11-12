using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using APS.Controls.Theme;
using APS.SystemControl.Controls.Theme;
using APS.SystemControl.Core.Classes;

namespace APS.SystemControl.Controls.General;

/// <summary>
/// Pure rendering + hit-testing: the host supplies <see cref="Flanges"/> and a
/// <see cref="LayoutResolver"/>, and listens to <see cref="FlangeClicked"/>.
/// </summary>
public class ChamberSourceSchematic : UserControl
{
    public sealed record FlangeView(
        int FlangeNumber, string? Chamber, string Material, string? MagnetronLabel,
        bool Occupied, double? LifeFraction, double WarnFraction, double? AngleDegrees,
        string? PowerSupplyId = null, bool PowerSupplyShared = false);

    private IReadOnlyList<FlangeView> _flanges = [];
    private int? _selectedFlange;
    private string? _activeChamber;
    private DarkTabControl? _tabs;
    private readonly BufferedPanel _canvas;

    private readonly Font _fMaterial = ScaledFont.Create(12f, FontStyle.Bold);
    private readonly Font _fSmall    = ScaledFont.Create(8f);
    private readonly Font _fChamber  = ScaledFont.Create(11f, FontStyle.Bold);

    public event EventHandler<int>? FlangeClicked;

    public Func<string?, (double StartDegrees, bool Clockwise)>? LayoutResolver { get; set; }

    public IReadOnlyList<FlangeView> Flanges
    {
        get => _flanges;
        set { _flanges = value ?? []; RebuildTabs(); _canvas.Invalidate(); }
    }

    public int? SelectedFlange
    {
        get => _selectedFlange;
        set { _selectedFlange = value; _canvas.Invalidate(); }
    }

    public ChamberSourceSchematic()
    {
        BackColor = VacuumTheme.PanelBackground;
        _canvas = new BufferedPanel { Dock = DockStyle.Fill, BackColor = VacuumTheme.PanelBackground };
        _canvas.Paint += PaintCanvas;
        _canvas.MouseDown += Canvas_MouseDown;
        Controls.Add(_canvas);
    }

    public static IReadOnlyList<string> DistinctChambers(IReadOnlyList<FlangeView> flanges) =>
        flanges.Where(f => !string.IsNullOrWhiteSpace(f.Chamber))
               .OrderBy(f => f.FlangeNumber)
               .Select(f => f.Chamber!)
               .Distinct()
               .ToList();

    private List<FlangeView> ActiveFlanges()
    {
        var chambers = DistinctChambers(_flanges);
        if (chambers.Count == 0) return _flanges.OrderBy(f => f.FlangeNumber).ToList();
        _activeChamber ??= chambers[0];
        return _flanges.Where(f => f.Chamber == _activeChamber).OrderBy(f => f.FlangeNumber).ToList();
    }

    private void RebuildTabs()
    {
        var chambers = DistinctChambers(_flanges);
        if (_tabs != null) { Controls.Remove(_tabs); _tabs.Dispose(); _tabs = null; }

        if (chambers.Count > 1)
        {
            _tabs = new DarkTabControl { Dock = DockStyle.Top, Height = DpiScale.Px(28) };
            foreach (var ch in chambers) _tabs.TabPages.Add(new TabPage(ch) { Tag = ch });
            _tabs.SelectedIndexChanged += (_, _) =>
            {
                _activeChamber = _tabs!.SelectedTab?.Tag as string;
                _selectedFlange = null;
                _canvas.Invalidate();
            };
            Controls.Add(_tabs);
            _activeChamber = chambers[0];
        }
        else
        {
            _activeChamber = chambers.Count == 1 ? chambers[0] : null;
        }
        _canvas.SendToBack();   // Fill must yield edges to the Top-docked tab strip
    }

    private float FlangeRadius => DpiScale.Px(46);

    private RectangleF CanvasBounds()
    {
        float pad = DpiScale.Px(60);
        return new RectangleF(pad, pad, _canvas.Width - 2 * pad, _canvas.Height - 2 * pad);
    }

    private IReadOnlyDictionary<int, PointF> Positions(IReadOnlyList<FlangeView> flanges)
    {
        var b = CanvasBounds();
        float ringR = Math.Min(b.Width, b.Height) / 2f;
        var (start, cw) = LayoutResolver?.Invoke(_activeChamber) ?? (0.0, true);
        return ChamberRingLayout.Compute(
            flanges.Select(f => (f.FlangeNumber, f.AngleDegrees)).ToList(),
            start, cw, b, ringR - FlangeRadius);
    }

    private void PaintCanvas(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(VacuumTheme.PanelBackground);

        var flanges = ActiveFlanges();
        if (flanges.Count == 0 || _canvas.Width < 10 || _canvas.Height < 10) return;

        var b = CanvasBounds();
        var center = new PointF(b.X + b.Width / 2f, b.Y + b.Height / 2f);
        float ringR = Math.Min(b.Width, b.Height) / 2f;
        // The chamber wall encloses the sources (they sit inside the chamber): flange
        // circles are centred on ringR - FlangeRadius, so their outer edge is ringR.
        float chamberR = ringR + DpiScale.Px(8);
        using (var chPen = new Pen(VacuumTheme.PanelBorder, DpiScale.Px(2)))
            g.DrawEllipse(chPen, center.X - chamberR, center.Y - chamberR, 2 * chamberR, 2 * chamberR);
        if (_activeChamber != null)
            DrawCentered(g, _activeChamber, _fChamber, VacuumTheme.TextDim, center);

        var positions = Positions(flanges);
        foreach (var f in flanges)
            if (positions.TryGetValue(f.FlangeNumber, out var p))
                DrawFlange(g, f, p, FlangeRadius);
    }

    private void DrawFlange(Graphics g, FlangeView f, PointF c, float r)
    {
        var ringRect = new RectangleF(c.X - r, c.Y - r, 2 * r, 2 * r);
        float trackW = DpiScale.Px(6);

        using (var track = new Pen(VacuumTheme.PanelBorder, trackW))
            g.DrawEllipse(track, ringRect);

        if (f.Occupied && f.LifeFraction is double life)
        {
            float sweep = (float)(Math.Min(Math.Max(life, 0.0), 1.0) * 360.0);
            using var arc = new Pen(LifeGauge.ColorFor(life, f.WarnFraction), trackW)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };
            if (sweep > 0) g.DrawArc(arc, ringRect, -90f, sweep);   // start at top, sweep clockwise
        }

        float bodyR = r - trackW;
        var bodyRect = new RectangleF(c.X - bodyR, c.Y - bodyR, 2 * bodyR, 2 * bodyR);
        Color fill = !f.Occupied
            ? VacuumTheme.PanelField
            : f.LifeFraction is null ? Color.FromArgb(40, 90, 140)   // occupied, no DB (Standalone)
            : VacuumTheme.PanelField;
        using (var bodyBrush = new SolidBrush(fill)) g.FillEllipse(bodyBrush, bodyRect);

        if (_selectedFlange == f.FlangeNumber)
            using (var sel = new Pen(Color.Cyan, DpiScale.Px(2)))
                g.DrawEllipse(sel, bodyRect);

        string mat = string.IsNullOrWhiteSpace(f.Material) ? "(empty)" : f.Material;
        DrawCentered(g, $"FL {f.FlangeNumber}", _fSmall, VacuumTheme.TextDim,
            new PointF(c.X, c.Y - bodyR * 0.65f));
        // Connected PSU under the flange number, so the full wiring reads without clicking
        // through every flange. Amber = the same PSU is on another flange too.
        if (!string.IsNullOrWhiteSpace(f.PowerSupplyId))
            DrawCentered(g, f.PowerSupplyId!, _fSmall,
                f.PowerSupplyShared ? VacuumTheme.GlowAmber : VacuumTheme.TextPrimary,
                new PointF(c.X, c.Y - bodyR * 0.33f));
        DrawCentered(g, mat, _fMaterial,
            string.IsNullOrWhiteSpace(f.Material) ? VacuumTheme.TextDim : VacuumTheme.TextPrimary,
            new PointF(c.X, c.Y + bodyR * 0.05f));
        if (!string.IsNullOrWhiteSpace(f.MagnetronLabel))
            DrawCentered(g, f.MagnetronLabel!, _fSmall, VacuumTheme.TextDim,
                new PointF(c.X, c.Y + bodyR * 0.52f));
    }

    private static void DrawCentered(Graphics g, string text, Font font, Color color, PointF center)
    {
        var sz = g.MeasureString(text, font);
        using var b = new SolidBrush(color);
        g.DrawString(text, font, b, center.X - sz.Width / 2f, center.Y - sz.Height / 2f);
    }

    private void Canvas_MouseDown(object? sender, MouseEventArgs e)
    {
        var flanges = ActiveFlanges();
        if (flanges.Count == 0) return;
        var positions = Positions(flanges);
        float r = FlangeRadius;
        foreach (var f in flanges)
        {
            if (!positions.TryGetValue(f.FlangeNumber, out var p)) continue;
            float dx = e.X - p.X, dy = e.Y - p.Y;
            if (dx * dx + dy * dy <= r * r)
            {
                SelectedFlange = f.FlangeNumber;
                FlangeClicked?.Invoke(this, f.FlangeNumber);
                return;
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _fMaterial.Dispose();
            _fSmall.Dispose();
            _fChamber.Dispose();
            _tabs?.Dispose();
        }
        base.Dispose(disposing);
    }
}
