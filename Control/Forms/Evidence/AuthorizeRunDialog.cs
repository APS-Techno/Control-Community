using System.Globalization;
using System.Text;
using APS.Controls.Theme;
using APS.SystemControl.Core.Evidence;

namespace APS.SystemControl.Forms.Evidence;

/// <summary>
/// L1 sign-off: shows the captured conditions + fingerprint, takes the specimen name,
/// lets the operator confirm/adjust the lift (and see the resulting target-substrate
/// distance) for each source that has geometry data, and on "Authorize &amp; run" returns
/// OK (the caller stamps the logged-in operator).
/// </summary>
public sealed class AuthorizeRunDialog : Form
{
    private readonly TextBox _specimen = new();
    private readonly List<DarkNumericUpDown> _liftEditors = new();
    private HintManager _hints = null!;

    public string Specimen => _specimen.Text.Trim();

    public AuthorizeRunDialog(DepositionConditions c)
    {
        Text = "Authorize & run";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;

        // Sources with a computed zero distance get an editable geometry row below the
        // summary; sources without geometry stay as plain lines inside the text summary
        // (see BuildSummary) so nothing is rendered twice.
        var geometrySources = c.Sources.Where(s => s.ZeroDistanceMm.HasValue).ToList();
        bool hasGeometry = geometrySources.Count > 0;

        var specimenLabel = new Label { Text = "Specimen:", AutoSize = true,
            Location = new Point(DpiScale.Px(12), DpiScale.Px(12)) };
        _specimen.Text = "Deposition";
        _specimen.Location = new Point(DpiScale.Px(90), DpiScale.Px(9));
        _specimen.Width = DpiScale.Px(410);

        int summaryHeight = hasGeometry ? DpiScale.Px(160) : DpiScale.Px(350);
        var summary = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Location = new Point(DpiScale.Px(12), DpiScale.Px(40)),
            Size = new Size(DpiScale.Px(496), summaryHeight),
            Text = BuildSummary(c, hasGeometry),
        };

        var controlsToAdd = new List<Control> { specimenLabel, _specimen, summary };
        int nextY = DpiScale.Px(40) + summaryHeight + DpiScale.Px(10);

        if (hasGeometry)
        {
            var geometryLabel = new Label
            {
                Text = "Target-substrate distance — confirm/adjust lift (mm):",
                AutoSize = true,
                Location = new Point(DpiScale.Px(12), nextY),
            };
            nextY += DpiScale.Px(20);

            var grid = new TableLayoutPanel
            {
                ColumnCount = 3,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            foreach (var s in geometrySources)
            {
                var label = new Label
                {
                    Text = $"flange {s.Flange?.ToString() ?? "—"}: {s.Material}",
                    AutoSize = true,
                    Anchor = AnchorStyles.Left,
                    Margin = new Padding(DpiScale.Px(4), DpiScale.Px(8), DpiScale.Px(12), 0),
                };
                var lift = new DarkNumericUpDown
                {
                    Minimum = 0, Maximum = 10000, DecimalPlaces = 1, Increment = 0.5m,
                    Value = (decimal)(s.LiftMm ?? 0), Width = DpiScale.Px(90),
                    Margin = new Padding(0, DpiScale.Px(4), DpiScale.Px(12), 0),
                };
                var dist = new Label
                {
                    AutoSize = true, Anchor = AnchorStyles.Left,
                    Margin = new Padding(0, DpiScale.Px(8), 0, 0),
                };

                void Recompute()
                {
                    s.LiftMm = (double)lift.Value;
                    s.TargetToSubstrateDistanceMm = APS.SystemControl.Core.Production.TargetSubstrateDistance.Effective(
                        s.ZeroDistanceMm, s.LiftMm, s.TargetThicknessMm, s.TargetBackingMm ?? 0, s.ErosionDepthMm ?? 0);
                    dist.Text = s.TargetToSubstrateDistanceMm is { } v
                        ? "→ " + v.ToString("0.0", CultureInfo.InvariantCulture) + " mm" : "→ —";
                }
                lift.ValueChanged += (_, _) => Recompute();
                Recompute();

                int row = grid.RowCount;
                grid.Controls.Add(label, 0, row);
                grid.Controls.Add(lift, 1, row);
                grid.Controls.Add(dist, 2, row);
                grid.RowCount++;

                _liftEditors.Add(lift);
            }

            var geometryScroll = new Panel
            {
                Location = new Point(DpiScale.Px(12), nextY),
                Size = new Size(DpiScale.Px(496), DpiScale.Px(160)),
                AutoScroll = true,
                BorderStyle = BorderStyle.FixedSingle,
            };
            geometryScroll.Controls.Add(grid);
            nextY += DpiScale.Px(160) + DpiScale.Px(10);

            controlsToAdd.Add(geometryLabel);
            controlsToAdd.Add(geometryScroll);
        }

        int buttonY = nextY;
        var ok = new Button { Text = "Authorize && run", DialogResult = DialogResult.OK,
            Location = new Point(DpiScale.Px(300), buttonY), Width = DpiScale.Px(120),
            Height = DpiScale.Px(28) };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel,
            Location = new Point(DpiScale.Px(428), buttonY), Width = DpiScale.Px(80),
            Height = DpiScale.Px(28) };
        controlsToAdd.Add(ok);
        controlsToAdd.Add(cancel);

        ClientSize = new Size(DpiScale.Px(520), buttonY + DpiScale.Px(28) + DpiScale.Px(12));

        Controls.AddRange(controlsToAdd.ToArray());
        AcceptButton = ok; CancelButton = cancel;

        DarkModeHelper.FixControls(this); // MANDATORY per project theming rule — after all controls added

        _hints = new HintManager(this);
        _hints.Set(_specimen, "Name for this deposition run — recorded in the signed evidence file.");
        _hints.Set(summary,   "Captured run conditions: recipe, operator, software versions, PLC serials"
                              + (hasGeometry ? ", and sources without target geometry." : " and source erosion depths (mm)."));
        foreach (var lift in _liftEditors)
            _hints.Set(lift, "Lift gauge reading for this run; lifting raises the target away from the substrate, increasing the working distance.");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _hints?.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>
    /// Non-geometry lines only (recipe/operator/software/PLC + sources that have no
    /// computed zero distance). Sources with geometry are rendered in the editable grid
    /// instead, so they are intentionally omitted here to avoid showing them twice.
    /// </summary>
    private static string BuildSummary(DepositionConditions c, bool hasGeometry)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Recipe:   {c.Recipe.Name} ({c.Recipe.Format})");
        sb.AppendLine($"Operator: {c.Signature.Name} [{c.Signature.Role ?? "—"}] ({c.Signature.Intent})");
        sb.AppendLine($"Software: control {c.Software.GetValueOrDefault("control")}, worker {c.Software.GetValueOrDefault("worker")}");
        if (c.PlcSerials.Length > 0) sb.AppendLine($"PLC:      {string.Join(", ", c.PlcSerials)}");

        var plainSources = hasGeometry ? c.Sources.Where(s => !s.ZeroDistanceMm.HasValue).ToList() : c.Sources.ToList();
        if (plainSources.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Sources:");
            foreach (var s in plainSources)
                sb.AppendLine($"  flange {s.Flange?.ToString() ?? "—"}: {s.Material} / {s.Format ?? "—"} / {s.MagnetronModel ?? "—"}"
                              + (s.ErosionDepthMm is { } e ? $"  (erosion {e.ToString("0.###", CultureInfo.InvariantCulture)} mm)" : ""));
        }
        return sb.ToString();
    }
}
