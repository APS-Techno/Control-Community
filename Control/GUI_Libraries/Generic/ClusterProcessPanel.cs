using System.Globalization;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using APS.ProcessPanel.Contracts;
using APS.SystemControl.Controls.Theme;
using APS.SystemControl.GUIControls.Process;
using Contracts = APS.ProcessPanel.Contracts;

namespace APS.Controls.Generic;

public sealed class ClusterProcessPanel : BaseProcessPanel, IClusterProcessPanel
{
    private ClusterSnapshot? _snapshot;
    private string _summary = "Cluster";

    private FlowLayoutPanel? _leftStackRef;
    private FlowLayoutPanel? _rightStackRef;
    private bool _extrasBuilt;
    private readonly Dictionary<string, ClusterValveLed> _valveLeds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Label> _drivePills = new(StringComparer.Ordinal);
    private const string RobotPillKey = "ROBOT";

    private static readonly Color TileBorder  = Color.FromArgb(34, 51, 76);
    private static readonly Color TextPrimary = Color.FromArgb(200, 214, 229);
    private static readonly Color TextDim     = Color.FromArgb(119, 136, 153);

    private static readonly Color TileGradTop = Color.FromArgb(27, 41, 64);
    private static readonly Color TileGradBot = Color.FromArgb(17, 28, 46);
    private static readonly Color TileEdge    = Color.FromArgb(43, 74, 107);
    private static readonly Color HubGradTop  = Color.FromArgb(29, 45, 69);
    private static readonly Color HubGradBot  = Color.FromArgb(14, 24, 40);
    private static readonly Color HubEdge     = Color.FromArgb(58, 90, 138);
    private static readonly Color ArmCyan     = Color.FromArgb(34, 211, 238);
    private static readonly Color AccentBlue  = Color.FromArgb(72, 169, 254);
    private static readonly Color GateOpen    = Color.FromArgb(80, 200, 120);
    private static readonly Color GateClosed  = Color.FromArgb(58, 74, 90);
    private static readonly Color ErrorRed    = Color.FromArgb(239, 83, 80);

    private const double PmFullScaleLo = 1e-7;
    private const double PmFullScaleHi = 1e-2;
    private const double RfFullScaleW  = 600;
    private const double TempFullScaleC = 300;
    private const double FlowFullScaleSccm = 50;

    // Multiplies font point sizes only; pixel geometry uses ui = UiScale * dpi.
    // ClusterTilePlan row heights are matched to this value.
    private const float UiScale = 1.4f;

    private float DpiF => DeviceDpi / 96f;

    protected override int LeftPanelWidth   => (int)(288 * DpiF);
    protected override int RightPanelWidth  => (int)(360 * DpiF);
    protected override int BottomPanelHeight => 0;

    public ClusterProcessPanel()
    {
        ID = "ClusterProcess";
        Caption = "Cluster";
    }

    protected override IEnumerable<ReadingSpec> DefineReadings() => ProcessPanelSpec.Current.Readings;

    // Rows are owned by UpdateCluster; the host's telemetry feed would overwrite them with Off.
    public override void UpdateReadings(ProcessReading[] readings) { }

    protected override void PopulateExtraSections(FlowLayoutPanel? leftStack, FlowLayoutPanel? rightStack)
    {
        _leftStackRef  = leftStack;
        _rightStackRef = rightStack;
    }

    private void EnsureExtraControls(ClusterSnapshot snap)
    {
        if (_extrasBuilt) return;
        _extrasBuilt = true;

        if (_rightStackRef != null)
        {
            AddExtraSectionHeader(_rightStackRef, "Slit valves");
            var slitRow = NewLedRow();
            foreach (var c in snap.Chambers)
                AddLed(slitRow, ClusterPanelMapper.SlitId(c.Key), "slit " + c.Key);
            _rightStackRef.Controls.Add(slitRow);

            // Shutter LEDs are keyed by the resolved valve id; a shutterId with no matching valve gets no LED.
            var shutterIds = new HashSet<string>(StringComparer.Ordinal);
            var shutterLeds = new List<(string ValveId, string Descr, string Display)>();
            foreach (var c in snap.Chambers)
                foreach (var s in c.Sources ?? System.Array.Empty<ClusterSourceInfo>())
                {
                    if (s.ShutterId is null) continue;
                    var valve = (c.Valves ?? System.Array.Empty<ClusterValveInfo>())
                        .FirstOrDefault(v => string.Equals(v.Id, s.ShutterId,
                            StringComparison.OrdinalIgnoreCase));
                    if (valve is null) continue;
                    shutterIds.Add(valve.Id);
                    shutterLeds.Add((valve.Id,
                        $"{c.Key}{(string.IsNullOrEmpty(s.Material) ? "" : " " + s.Material)} shutter",
                        $"{c.Key} {(string.IsNullOrEmpty(s.Material) ? "Sh" : s.Material)}"));
                }

            if (shutterLeds.Count > 0)
            {
                AddExtraSectionHeader(_rightStackRef, "Shutters");
                var shutterRow = NewLedRow();
                foreach (var (valveId, descr, display) in shutterLeds)
                    AddLed(shutterRow, valveId, descr, display);
                _rightStackRef.Controls.Add(shutterRow);
            }

            // Built once from the first snapshot, which must carry every chamber's full valve set.
            foreach (var c in snap.Chambers)
            {
                var valves = (c.Valves ?? System.Array.Empty<ClusterValveInfo>())
                    .Where(v => !shutterIds.Contains(v.Id)).ToArray();
                if (valves.Length == 0) continue;
                AddExtraSectionHeader(_rightStackRef, c.Key + " valves");
                var row = NewLedRow();
                foreach (var v in valves)
                    AddLed(row, v.Id, v.Descriptor);
                _rightStackRef.Controls.Add(row);
            }

            AddExtraSectionHeader(_rightStackRef, "Pumps");
            var pumpRow = NewLedRow();
            foreach (var c in snap.Chambers)
            {
                AddLed(pumpRow, ClusterPanelMapper.TurboId(c.Key),  "turbo "  + c.Key);
                AddLed(pumpRow, ClusterPanelMapper.ScrollId(c.Key), "scroll " + c.Key);
            }
            AddLed(pumpRow, ClusterPanelMapper.TurboId("TM"),  "turbo TM");
            AddLed(pumpRow, ClusterPanelMapper.ScrollId("TM"), "scroll TM");
            _rightStackRef.Controls.Add(pumpRow);
        }

        if (_leftStackRef != null)
        {
            AddExtraSectionHeader(_leftStackRef, "Drives");
            _drivePills[RobotPillKey] = AddDrivePill(_leftStackRef, "Robot");
            foreach (var c in snap.Chambers)
                if (c.DrivePosition.HasValue || c.Key.StartsWith("C", StringComparison.Ordinal))
                    _drivePills[c.Key] = AddDrivePill(_leftStackRef, c.Key + " lift");
        }
    }

    private static FlowLayoutPanel NewLedRow() => new()
    {
        FlowDirection = FlowDirection.LeftToRight,
        WrapContents  = true,
        AutoSize      = true,
        AutoSizeMode  = AutoSizeMode.GrowAndShrink,
        Margin        = new Padding(4, 0, 4, 2),
    };

    private void AddLed(FlowLayoutPanel row, string id, string descr, string? display = null)
    {
        if (_valveLeds.ContainsKey(id)) return;
        var led = new ClusterValveLed(id, descr, display);
        _valveLeds[id] = led;
        row.Controls.Add(led);
    }

    private Label AddDrivePill(FlowLayoutPanel stack, string label)
    {
        float s = DpiF;
        int rowH = (int)(26 * s), labelH = (int)(22 * s), pillW = (int)(92 * s);
        var row = new Panel { Height = rowH, Width = (int)(220 * s), BackColor = Color.FromArgb(13, 27, 42) };
        var name = new Label
        {
            Text = label, Font = ScaledFont.Create(10f),
            ForeColor = TextPrimary, AutoSize = false, Height = labelH,
            Location = new Point(6, 2), TextAlign = ContentAlignment.MiddleLeft,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Width = row.Width - 6 - pillW - 6,
        };
        row.Controls.Add(name);
        var pill = new Label
        {
            Text = "—", Font = ScaledFont.Create(9f, FontStyle.Bold),
            ForeColor = TextDim, BackColor = TileBorder,
            AutoSize = false, Width = pillW, Height = labelH,
            Location = new Point(row.Width - pillW - 6, 2),
            TextAlign = ContentAlignment.MiddleCenter,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        row.Controls.Add(pill);
        stack.Controls.Add(row);
        return pill;
    }

    public void UpdateCluster(ClusterSnapshot snapshot)
    {
        _snapshot = snapshot;
        EnsureExtraControls(snapshot);
        bool recipeActive = CurrentRecipe?.State
            is RecipeState.Running or RecipeState.Paused or RecipeState.Aborting;
        base.UpdateReadings(ClusterPanelMapper.ToReadings(snapshot, recipeActive));
        UpdateValves(ClusterPanelMapper.ToValves(snapshot));
        UpdateDrivePills(snapshot);
        string summary = ClusterPanelMapper.ToHeaderSummary(snapshot);
        if (summary != _summary)
        {
            _summary = summary;
            HeaderPanel.Invalidate();
        }
        SchematicPanel.Invalidate();
    }

    protected override void OnValvesChanged(Contracts.ValveInfo[] valves)
    {
        foreach (var v in valves)
            if (_valveLeds.TryGetValue(v.Id, out var led))
                led.IsOpen = v.IsOpen;
    }

    private void UpdateDrivePills(ClusterSnapshot snap)
    {
        if (_drivePills.TryGetValue(RobotPillKey, out var rp))
        {
            rp.Text = $"{snap.Robot.State} ▸ {snap.Robot.CurrentFace}";
            rp.ForeColor = snap.Robot.ErrorActive
                ? Color.FromArgb(255, 100, 100) : Color.FromArgb(0, 255, 255);
        }
        foreach (var c in snap.Chambers)
            if (_drivePills.TryGetValue(c.Key, out var p))
                p.Text = c.DrivePosition is double d ? d.ToString("F0", CultureInfo.InvariantCulture) : "—";
    }

    protected override void OnPaintHeader(Graphics g, Rectangle bounds)
    {
        using var title = ScaledFont.Create(11f, FontStyle.Bold);
        using var tb = new SolidBrush(TextPrimary);
        g.DrawString(_summary, title, tb, 12f * (g.DpiY / 96f),
            (bounds.Height - title.GetHeight(g)) / 2f);
    }

    protected override void OnPaintSchematic(Graphics g, Rectangle bounds)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var snap = _snapshot;
        if (snap is null || snap.Chambers.Length == 0)
        {
            using var f = new Font("Segoe UI", 10f);
            using var db = new SolidBrush(TextDim);
            g.DrawString("No cluster data", f, db, bounds.X + 12, bounds.Y + 12);
            return;
        }

        float dpi = g.DpiY / 96f;
        float ui  = UiScale * dpi;

        var layout = new ClusterLayout(bounds.Size,
            snap.Chambers.Select(c => (c.Key, c.FaceAngle)).ToList(), dpi);

        using (var spokePen = new Pen(TileEdge, 3f * ui)
               { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            foreach (var c in snap.Chambers)
            {
                var inner = layout.HexEdgeToward(c.Key);
                var tile  = layout.TileFor(c.Key);
                var outer = new Point(tile.X + tile.Width / 2, tile.Y + tile.Height / 2);
                g.DrawLine(spokePen, inner, outer);
            }
        }

        DrawHex(g, layout, snap.Robot, dpi);

        foreach (var c in snap.Chambers)
        {
            var inner = layout.HexEdgeToward(c.Key);
            double dx = inner.X - layout.Center.X, dy = inner.Y - layout.Center.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            float off = 10f * ui;
            var mid = len < 1
                ? new PointF(inner.X, inner.Y)
                : new PointF(inner.X + (float)(dx / len * off), inner.Y + (float)(dy / len * off));
            float pw = 18f * ui, ph = 9f * ui, pr = 4.5f * ui;
            float angleDeg = (float)(Math.Atan2(dy, dx) * 180.0 / Math.PI) + 90f;
            var pill = new RectangleF(-pw / 2f, -ph / 2f, pw, ph);
            var savedPill = g.Save();
            g.TranslateTransform(mid.X, mid.Y);
            g.RotateTransform(angleDeg);
            if (c.SlitOpen)
            {
                GlowRoundRect(g, pill, pr, GateOpen, ui);
            }
            else
            {
                using var b = new SolidBrush(GateClosed);
                using var path = RoundedRect(pill, pr);
                g.FillPath(b, path);
            }
            g.Restore(savedPill);
        }

        foreach (var c in snap.Chambers)
            DrawTile(g, layout.TileFor(c.Key), c, dpi);
    }

    private static Color StateColor(SystemStateCode s) => s switch
    {
        SystemStateCode.Startup     => Color.FromArgb(119, 136, 153),
        SystemStateCode.Idle        => Color.FromArgb(72, 169, 254),
        SystemStateCode.Maintenance => Color.FromArgb(178, 145, 47),
        SystemStateCode.Processing  => Color.FromArgb(255, 183, 77),
        SystemStateCode.Stopped     => Color.FromArgb(239, 83, 80),
        _                           => TextPrimary,
    };

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        // GDI+ rejects arc sizes at or below its REAL_EPSILON (1.192092896e-7) with
        // InvalidParameter — a plain <= 0 guard misses sub-epsilon fill widths (e.g.
        // a mini-bar at norm ~1e-12 just after recipe start). NaN passes every
        // <=/>= comparison, so require finite-and-above-epsilon via !(a > b).
        const float GdipEps = 1.192092896e-07f;
        if (!(r.Width > GdipEps) || !(r.Height > GdipEps) ||
            !float.IsFinite(r.X) || !float.IsFinite(r.Y) ||
            !float.IsFinite(r.Width) || !float.IsFinite(r.Height))
            return p;
        // Corner diameter can never exceed the smaller side, else the arcs overlap
        // into degenerate (figure-8) geometry for very thin rects (e.g. tiny bar fills).
        float d = Math.Min(radius * 2f, Math.Min(r.Width, r.Height));
        if (d <= GdipEps) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    // GDI+ has no blur: the soft glow is a few expanding translucent passes.
    private static void GlowEllipse(Graphics g, float cx, float cy, float r, Color c, float ui)
    {
        for (int i = 3; i >= 1; i--)
        {
            using var b = new SolidBrush(Color.FromArgb(30 / i, c));
            float rr = r + i * 2.5f * ui;
            g.FillEllipse(b, cx - rr, cy - rr, rr * 2, rr * 2);
        }
        using var core = new SolidBrush(c);
        g.FillEllipse(core, cx - r, cy - r, r * 2, r * 2);
    }

    private static double PmNorm(double torr)
    {
        if (torr <= 0) return 0;
        double lo = Math.Log10(PmFullScaleLo), hi = Math.Log10(PmFullScaleHi);
        return Math.Clamp((Math.Log10(torr) - lo) / (hi - lo), 0, 1);
    }

    private static void MiniBar(Graphics g, RectangleF track, double norm, Color fill)
    {
        norm = Math.Clamp(norm, 0, 1);
        using (var path = RoundedRect(track, track.Height / 2f))
        using (var tb = new SolidBrush(TileBorder))
            g.FillPath(tb, path);
        if (norm <= 0) return;
        var fr = new RectangleF(track.X, track.Y, (float)(track.Width * norm), track.Height);
        using (var path = RoundedRect(fr, track.Height / 2f))
        using (var fb = new SolidBrush(fill))
            g.FillPath(fb, path);
    }

    private static void GlowLine(Graphics g, PointF a, PointF b, float w, Color c, float ui)
    {
        for (int i = 3; i >= 1; i--)
        {
            using var p = new Pen(Color.FromArgb(28 / i, c), w + i * 3f * ui)
            {
                StartCap = LineCap.Round,
                EndCap   = LineCap.Round,
            };
            g.DrawLine(p, a, b);
        }
        using var core = new Pen(c, w) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(core, a, b);
    }

    private static void GlowRoundRect(Graphics g, RectangleF r, float radius, Color c, float ui)
    {
        for (int i = 3; i >= 1; i--)
        {
            var rr = RectangleF.Inflate(r, i * 2f * ui, i * 2f * ui);
            using var path = RoundedRect(rr, radius + i * 2f * ui);
            using var b = new SolidBrush(Color.FromArgb(26 / i, c));
            g.FillPath(b, path);
        }
        using var core = RoundedRect(r, radius);
        using var cb = new SolidBrush(c);
        g.FillPath(cb, core);
    }

    // GDI+ has no blur: the soft shadow is a few expanding translucent passes drawn before the body fill.
    private static void DropShadowRect(Graphics g, RectangleF r, float radius, float dy, float ui)
    {
        for (int i = 4; i >= 1; i--)
        {
            var rr = RectangleF.Inflate(r, i * 1.4f * ui, i * 1.4f * ui);
            rr.Offset(0, dy);
            using var path = RoundedRect(rr, radius + i * 1.4f * ui);
            using var b = new SolidBrush(Color.FromArgb(40 / i, 0, 0, 0));
            g.FillPath(b, path);
        }
    }

    private static void DropShadowPolygon(Graphics g, PointF[] pts, PointF c, float dy)
    {
        for (int i = 4; i >= 1; i--)
        {
            float s = 1f + i * 0.04f;
            var sp = new PointF[pts.Length];
            for (int k = 0; k < pts.Length; k++)
                sp[k] = new PointF(c.X + (pts[k].X - c.X) * s, c.Y + (pts[k].Y - c.Y) * s + dy);
            using var path = new GraphicsPath();
            path.AddPolygon(sp);
            using var b = new SolidBrush(Color.FromArgb(40 / i, 0, 0, 0));
            g.FillPath(b, path);
        }
    }

    private static void DrawTile(Graphics g, Rectangle r, ClusterChamberInfo c, float dpi)
    {
        if (r.Width < 2 || r.Height < 2) return;
        float ui = UiScale * dpi;
        var rf = new RectangleF(r.X, r.Y, r.Width - 1, r.Height - 1);
        float radius = 9f * ui;

        DropShadowRect(g, rf, radius, 4f * ui, ui);

        using (var path = RoundedRect(rf, radius))
        {
            using (var grad = new LinearGradientBrush(rf, TileGradTop, TileGradBot, 90f))
                g.FillPath(grad, path);
            using (var pen = new Pen(TileEdge, ui))
                g.DrawPath(pen, path);
        }

        Color state = StateColor(c.State);

        using (var bodyPath = RoundedRect(rf, radius))
        {
            var saved = g.Save();
            g.SetClip(bodyPath, CombineMode.Intersect);
            using (var ab = new SolidBrush(state))
                g.FillRectangle(ab, rf.X, rf.Y, rf.Width, 5f * ui);
            g.Restore(saved);
        }

        using var nameF   = ScaledFont.Create(11f * UiScale, FontStyle.Bold);
        using var smallF  = ScaledFont.Create(8.5f * UiScale);
        using var monoF   = new Font("Consolas", 8.5f * UiScale);
        using var tagF    = ScaledFont.Create(6f * UiScale, FontStyle.Bold);
        using var primary = new SolidBrush(TextPrimary);
        using var dim     = new SolidBrush(TextDim);

        float padX = 8f * ui, padBottom = 6f * ui;
        float x0 = rf.X + padX, x1 = rf.Right - padX;
        float y = rf.Y + 10f * ui;

        // Measure through g: Font.Height reports primary-screen metrics, wrong on a bitmap at another DPI.
        float smallH = smallF.GetHeight(g);
        float nameH  = nameF.GetHeight(g);

        g.DrawString(c.Key, nameF, primary, x0, y);
        string st = c.State.ToString().ToUpperInvariant();
        if (st.Length > 5) st = st.Substring(0, 4);
        var stSize = g.MeasureString(st, smallF);
        float stX = x1 - stSize.Width;
        GlowEllipse(g, stX - 8f * ui, y + nameH / 2f, 3.5f * ui, state, ui);
        g.DrawString(st, smallF, dim, stX, y + 2f * ui);
        y += nameH + 4f * ui;

        using (var dp = new Pen(TileBorder, ui))
            g.DrawLine(dp, x0, y, x1, y);
        y += 5f * ui;

        var metrics = BuildTileMetrics(c, state);

        var plan = TilePlan.For((int)(rf.Bottom - y - padBottom), metrics.Count, dpi);
        float rowH = plan.RowHeight;
        if (metrics.Count == 0) return;

        float colGap = 6f * ui;
        float dotR   = 3f * ui;
        float glyphW = metrics.Any(m => m.SourceOn != null) ? dotR * 2f + 4f * ui : 0f;
        float nameW  = metrics.Max(m => g.MeasureString(m.Name, smallF).Width);
        float unitColW = metrics.Max(m =>
            (m.Unit.Length > 0 ? g.MeasureString(m.Unit, smallF).Width + 3f * ui : 0f)
            + (m.ShutterOpen is bool o
                ? g.MeasureString(o ? "OPEN" : "CLOSED", tagF).Width : 0f));
        float nameX      = x0 + glyphW;
        float supplyX    = nameX + nameW + colGap;
        float unitX      = x1 - unitColW;
        float valueRight = unitX - colGap;

        using var rowClip = RoundedRect(rf, radius);
        var savedRows = g.Save();
        g.SetClip(rowClip, CombineMode.Intersect);
        for (int i = 0; i < plan.MetricRows && i < metrics.Count; i++)
        {
            var m = metrics[i];
            if (m.SourceOn is bool srcOn)
            {
                // The dot tracks delivered power; shutter state is the OPEN/CLOSED tag, since sources keep sputtering behind closed shutters.
                float dotCy = y + smallH / 2f;
                if (srcOn)
                {
                    GlowEllipse(g, x0 + dotR, dotCy, dotR, GateOpen, ui);
                }
                else
                {
                    using var sb = new SolidBrush(GateClosed);
                    g.FillEllipse(sb, x0, dotCy - dotR, dotR * 2f, dotR * 2f);
                }
            }
            bool isSource = m.Supply != null || m.ShutterOpen != null || m.SourceOn != null;
            g.DrawString(m.Name, smallF, isSource ? primary : dim, nameX, y);
            if (m.Supply != null)
                g.DrawString(m.Supply, smallF, dim, supplyX, y);

            var vs = g.MeasureString(m.Value, monoF);
            g.DrawString(m.Value, monoF, primary, valueRight - vs.Width, y);

            float ux = unitX;
            if (m.Unit.Length > 0)
            {
                g.DrawString(m.Unit, smallF, dim, ux, y);
                ux += g.MeasureString(m.Unit, smallF).Width + 3f * ui;
            }
            if (m.ShutterOpen is bool open2)
            {
                string tag = open2 ? "OPEN" : "CLOSED";
                using var tagB = new SolidBrush(open2 ? GateOpen : GateClosed);
                var ts = g.MeasureString(tag, tagF);
                g.DrawString(tag, tagF, tagB, ux, y + (smallH - ts.Height) / 2f);
            }
            if (plan.ShowBars && m.Norm >= 0)
                MiniBar(g, new RectangleF(x0, y + 15f * ui, x1 - x0, 4f * ui), m.Norm, m.Bar);
            y += rowH;
        }
        g.Restore(savedRows);
    }

    internal sealed record TileMetric(string Name, string Value, string Unit,
        double Norm, Color Bar, string? Supply = null, bool? ShutterOpen = null,
        bool? SourceOn = null);

    private static string FormatPressure(double torr) => torr.ToString("0.0e-0", CultureInfo.InvariantCulture);

    internal static List<TileMetric> BuildTileMetrics(ClusterChamberInfo c, Color state)
    {
        var sources = c.Sources ?? System.Array.Empty<ClusterSourceInfo>();
        var metrics = new List<TileMetric>();
        if (c.Pressure is double pm)
            metrics.Add(new("pm", FormatPressure(pm), "Torr", PmNorm(pm), AccentBlue));

        var usedSources = new HashSet<ClusterSourceInfo>();
        foreach (var p in c.Powers ?? System.Array.Empty<ClusterReadingInfo>())
        {
            if (p.Value is not double rfw) continue;
            var src = sources.FirstOrDefault(s =>
                string.Equals(s.PowerSupplyId, p.Id, StringComparison.OrdinalIgnoreCase));
            if (src is null)
            {
                metrics.Add(new(p.Label, rfw.ToString("F0", CultureInfo.InvariantCulture), "W", rfw / RfFullScaleW, state));
                continue;
            }
            usedSources.Add(src);
            bool hasLabel = src.Effective.Length > 0;
            metrics.Add(new(hasLabel ? src.Effective : p.Label,
                rfw.ToString("F0", CultureInfo.InvariantCulture), "W", rfw / RfFullScaleW, state,
                hasLabel ? p.Label : null, ShutterOpenFor(c, src), rfw > 0));
        }
        foreach (var src in sources)
        {
            if (usedSources.Contains(src)) continue;
            // SourceOn stays null without a live reading: unknown, not off.
            string? supply = src.PowerSupplyId is null
                ? null : ClusterReadingInfo.For(src.PowerSupplyId, c.Key, null).Label;
            bool hasLabel = src.Effective.Length > 0;
            metrics.Add(new(hasLabel ? src.Effective : supply ?? src.ShutterId ?? "src",
                "—", "", -1, state,
                hasLabel ? supply : null, ShutterOpenFor(c, src)));
        }

        if (c.Temperature is double tmp)
            metrics.Add(new("tmp", tmp.ToString("F0", CultureInfo.InvariantCulture), "°C", tmp / TempFullScaleC, AccentBlue));

        foreach (var f in c.Flows ?? System.Array.Empty<ClusterReadingInfo>())
            if (f.Value is double sccm)
                metrics.Add(new(f.Label, sccm.ToString("F1", CultureInfo.InvariantCulture), "sccm",
                    sccm / FlowFullScaleSccm, AccentBlue));
        return metrics;
    }

    // IgnoreCase: display-only lookup, unlike the Ordinal shutter-id set in EnsureExtraControls.
    private static bool? ShutterOpenFor(ClusterChamberInfo c, ClusterSourceInfo src)
    {
        if (src.ShutterId is null) return null;
        var v = (c.Valves ?? System.Array.Empty<ClusterValveInfo>())
            .FirstOrDefault(x => string.Equals(x.Id, src.ShutterId, StringComparison.OrdinalIgnoreCase));
        return v?.IsOpen;
    }

    private static void DrawHex(Graphics g, ClusterLayout layout, ClusterRobotInfo robot, float dpi)
    {
        if (layout.HexRect.Width < 2 || layout.HexRect.Height < 2) return;
        float ui = UiScale * dpi;
        var rect = layout.HexRect;
        var c = layout.Center;
        float radius = rect.Width / 2f;
        var pts = new PointF[6];
        for (int i = 0; i < 6; i++)
        {
            double a = i * Math.PI / 3.0;
            pts[i] = new PointF(
                c.X + (float)(radius * Math.Sin(a)),
                c.Y - (float)(radius * Math.Cos(a)));
        }

        DropShadowPolygon(g, pts, c, 4f * ui);

        using (var path = new GraphicsPath())
        {
            path.AddPolygon(pts);
            var gb = new RectangleF(rect.X, rect.Y, rect.Width, rect.Height);
            using (var grad = new LinearGradientBrush(gb, HubGradTop, HubGradBot, 90f))
                g.FillPath(grad, path);
            Color outline = robot.ErrorActive ? ErrorRed : HubEdge;
            using (var pen = new Pen(outline, 2f * ui))
                g.DrawPath(pen, path);
        }

        using var nameF   = ScaledFont.Create(11f * UiScale, FontStyle.Bold);
        using var monoF   = new Font("Consolas", 8.5f * UiScale);
        using var primary = new SolidBrush(TextPrimary);
        using var dim     = new SolidBrush(TextDim);

        const string tmText = "TM";
        string presText  = robot.Pressure is double p ? FormatPressure(p) + " Torr" : "—";
        string stateText = $"{robot.State} ▸ {robot.CurrentFace}";
        var tmS    = g.MeasureString(tmText, nameF);
        var presS  = g.MeasureString(presText, monoF);
        var stateS = g.MeasureString(stateText, monoF);

        float padX = 12f * ui, padY = 8f * ui, lineGap = 2f * ui;
        float contentW = Math.Max(tmS.Width, Math.Max(presS.Width, stateS.Width));
        float chipW = contentW + 2f * padX;
        float chipH = tmS.Height + presS.Height + stateS.Height + 2f * lineGap + 2f * padY;

        chipW = Math.Min(chipW, 2f * 0.82f * radius);
        chipH = Math.Min(chipH, 2f * 0.48f * radius);
        float hw = chipW / 2f, hh = chipH / 2f;
        var chip = new RectangleF(c.X - hw, c.Y - hh, chipW, chipH);

        double armAngle = robot.AngleNorm / 255.0 * 2 * Math.PI;
        float dx = (float)Math.Sin(armAngle), dy = -(float)Math.Cos(armAngle);
        float tExit = float.PositiveInfinity;
        if (Math.Abs(dx) > 1e-4f) tExit = Math.Min(tExit, hw / Math.Abs(dx));
        if (Math.Abs(dy) > 1e-4f) tExit = Math.Min(tExit, hh / Math.Abs(dy));
        if (float.IsInfinity(tExit)) tExit = Math.Max(hw, hh);
        float minStub = 0.18f * radius;
        float inner = tExit + minStub;
        float outer = Math.Max(0.95f * radius, inner);
        float frac  = Math.Clamp(robot.ExtendNorm / 255f, 0f, 1f);
        float armLen = inner + (outer - inner) * frac;
        var tip = new PointF(c.X + dx * armLen, c.Y + dy * armLen);
        GlowLine(g, c, tip, 4.5f * ui, ArmCyan, ui);
        if (robot.HasWafer)
            GlowEllipse(g, tip.X, tip.Y, 6f * ui, ArmCyan, ui);

        using (var chipPath = RoundedRect(chip, 6f * ui))
        {
            using (var fill = new SolidBrush(Color.FromArgb(11, 20, 34)))
                g.FillPath(fill, chipPath);
            using (var pen = new Pen(HubEdge, 1.5f * ui))
                g.DrawPath(pen, chipPath);
        }

        using var stateBr = new SolidBrush(robot.ErrorActive
            ? Color.FromArgb(255, 100, 100) : ArmCyan);
        float ty = chip.Y + padY;
        g.DrawString(tmText, nameF, primary, c.X - tmS.Width / 2f, ty);
        ty += tmS.Height + lineGap;
        g.DrawString(presText, monoF, dim, c.X - presS.Width / 2f, ty);
        ty += presS.Height + lineGap;
        g.DrawString(stateText, monoF, stateBr, c.X - stateS.Width / 2f, ty);
    }
}
