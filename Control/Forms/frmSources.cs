using APS.Controls.Theme;
using APS.RecipeModel.Validation;
using APS.SystemControl.Controls.General;
using APS.SystemControl.Controls.Theme;
using APS.SystemControl.Core.Classes;
using APS.SystemControl.Hosting;
using CoreClass = APS.SystemControl.Core.Core;

namespace APS.SystemControl.Forms;

public class frmSources : Form
{
    private readonly SourceManager _sourceManager;
    private readonly string _filePath;
    private int _selectedFlange = 1;

    private readonly ISourceMountExtension? _mountExtension;
    private readonly CoreClass? _core;
    private readonly ISourceMountPanel? _mountPanel;

    // When true the form is opened during a deposition: config edits are locked
    // (banner shown) but the live read-outs still render and tick.
    private readonly bool _readOnly;
    private readonly Action? _onSourcesCommitted;

    // Snapshot of each source's editable fields, taken on open. SaveFieldToSource
    // mutates the live Source objects on every keystroke, so Cancel/X must restore
    // these or abandoned edits silently persist in memory.
    private readonly Dictionary<int, (string Name, string Material, string Description, string Comment,
        string MagnetronId, string Model, string PowerSupplyId, string MagnetronInstanceId,
        double? ZeroDistanceMm, double? LiftPositionMm)> _originalSources = new();

    private List<MagnetronDef> _origMagnetrons = [];
    private List<string> _origPowerSources = [];
    private List<SourceFormat> _origFormats = [];
    private List<MagnetronInstance> _origInstances = [];

    /// <summary>Re-take the rollback snapshot from the live SourceManager — on open and
    /// after every Save, so Close only discards edits made since the last save.</summary>
    private void Rebaseline()
    {
        _originalSources.Clear();
        for (int f = 1; f <= 6; f++)
        {
            var s = _sourceManager.GetSource(f);
            if (s != null)
                _originalSources[f] = (s.Name, s.Material, s.Description, s.Comment,
                    s.MagnetronId, s.Model, s.PowerSupplyId, s.MagnetronInstanceId,
                    s.ZeroDistanceMm, s.LiftPositionMm);
        }
        // Catalog lists are replaced wholesale by the Edit dialogs (each works on a copy
        // and assigns a new list on OK), so shallow snapshots are enough to undo them.
        _origMagnetrons   = _sourceManager.Magnetrons.ToList();
        _origPowerSources = _sourceManager.PowerSources.ToList();
        _origFormats      = _sourceManager.Formats.ToList();
        _origInstances    = _sourceManager.MagnetronInstances.ToList();
    }

    private bool IsDirty()
    {
        if (!_sourceManager.Magnetrons.SequenceEqual(_origMagnetrons)) return true;
        if (!_sourceManager.PowerSources.SequenceEqual(_origPowerSources)) return true;
        if (!_sourceManager.Formats.SequenceEqual(_origFormats)) return true;
        if (!_sourceManager.MagnetronInstances.SequenceEqual(_origInstances)) return true;
        foreach (var (f, o) in _originalSources)
        {
            var s = _sourceManager.GetSource(f);
            if (s == null) continue;
            if (o != (s.Name, s.Material, s.Description, s.Comment, s.MagnetronId, s.Model,
                      s.PowerSupplyId, s.MagnetronInstanceId, s.ZeroDistanceMm, s.LiftPositionMm))
                return true;
        }
        return false;
    }

    private void RollBack()
    {
        _sourceManager.Magnetrons         = _origMagnetrons;
        _sourceManager.PowerSources       = _origPowerSources;
        _sourceManager.Formats            = _origFormats;
        _sourceManager.MagnetronInstances = _origInstances;
        foreach (var (f, o) in _originalSources)
        {
            var s = _sourceManager.GetSource(f);
            if (s == null) continue;
            s.Name = o.Name; s.Material = o.Material; s.Description = o.Description; s.Comment = o.Comment;
            s.MagnetronId = o.MagnetronId; s.Model = o.Model; s.PowerSupplyId = o.PowerSupplyId;
            s.MagnetronInstanceId = o.MagnetronInstanceId;
            // Geometry feeds BuildConditions/the signed .depo.json — an abandoned
            // edit must not survive Close (code audit 2026-08-29, C6).
            s.ZeroDistanceMm = o.ZeroDistanceMm; s.LiftPositionMm = o.LiftPositionMm;
        }
    }

    private ChamberSourceSchematic _schematic = null!;
    private Label _lblChamberShutter = null!;
    private HintManager _hints = null!;

    // Flange -> (life consumed, warn fraction), filled by one DB query in RefreshLifeCache.
    // Keeps the per-keystroke view-model rebuild DB-free.
    private readonly Dictionary<int, (double Life, double Warn)> _lifeCache = new();

    private readonly Label _lblTitle;
    private readonly TextBox _txtName;
    private readonly TextBox _txtMaterial;
    private readonly ComboBox _cboMagnetron;
    private readonly ComboBox _cboPowerSupply;
    private readonly TextBox _txtDescription;
    private readonly TextBox _txtComment;
    private readonly Label _lblInstalled;
    private readonly Label _lblDepTime;
    private DarkNumericUpDown _numZero = null!;
    private DarkNumericUpDown _numLift = null!;
    internal string DistanceLabelTextForTest => _lblDistance.Text;

    private Label _lblDistance = null!;
    private Label _lblSaved = null!;

    // Colors — route through VacuumTheme so Palette.xml overrides apply here too.
    private static Color ColorBg     => VacuumTheme.PanelBackground;
    private static Color ColorText   => VacuumTheme.TextPrimary;
    private static Color ColorDim    => VacuumTheme.TextDim;
    private static Color ColorBorder => VacuumTheme.PanelBorder;
    private static Color ColorField  => VacuumTheme.PanelField;

    internal frmSources(SourceManager sourceManager,
        ISourceMountExtension? mountExtension = null,
        CoreClass? core = null,
        string filePath = "",
        Action? onSourcesCommitted = null,
        bool readOnly = false)
    {
        _sourceManager        = sourceManager;
        _filePath             = filePath;
        _mountExtension       = mountExtension;
        _core                 = core;
        _onSourcesCommitted   = onSourcesCommitted;
        _readOnly             = readOnly;
        _mountPanel           = mountExtension?.CreatePanel(core, readOnly);

        // Snapshot before any edit so Close can roll back the live Source objects.
        Rebaseline();
        FormClosing += (_, e) =>
        {
            if (DialogResult == DialogResult.OK) return;   // Save already committed
            // Only a shown dialog asks; tests raise FormClosing on an unshown form.
            if (Visible && IsDirty()
                && ThemedMessageBox.Show(this, "Discard unsaved source changes?", "Sources",
                       MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            RollBack();
        };

        APS.SystemControl.Core.UIScaling.ApplyToForm(this);
        Text = "Deposition Sources";
        Size = new Size(DpiScale.Px(900), DpiScale.Px(700));
        // The detail panel is 440 px wide in Networked mode (groove plot + schematic) and
        // 360 px in Standalone (the two-column config grid needs the extra width). Heights
        // are tall enough that the config grid never scrolls below the mounted-target panel.
        MinimumSize = _mountPanel != null
            ? new Size(DpiScale.Px(860), DpiScale.Px(620))
            : new Size(DpiScale.Px(700), DpiScale.Px(480));
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = ColorBg;
        ForeColor = ColorText;

        // Read-only banner: shown only when the form is opened during a deposition.
        // Built here but added to the form at the very end, so Dock resolution stays
        // correct (WinForms docks in reverse add order — see the ordered block below).
        Label? banner = null;
        if (_readOnly)
        {
            banner = new Label
            {
                Dock = DockStyle.Top,
                Height = DpiScale.Px(26),
                Text = "Read-only — deposition in progress",
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = VacuumTheme.PanelField,
                ForeColor = VacuumTheme.GlowYellow,
                Font = new Font(Font, FontStyle.Bold),
            };
        }

        _hints = new HintManager(this);

        var btnPanel = new Panel { Dock = DockStyle.Bottom, Height = DpiScale.Px(45), BackColor = ColorBg };
        var btnSize = new Size(DpiScale.Px(90), DpiScale.Px(30));
        int btnY = DpiScale.Px(7);
        var btnSave = new Button
        {
            Text = "Save", Size = btnSize, Location = new Point(DpiScale.Px(10), btnY),
            FlatStyle = FlatStyle.Flat, ForeColor = ColorText, BackColor = ColorField
        };
        btnSave.FlatAppearance.BorderColor = ColorBorder;
        btnSave.Click += BtnSave_Click;

        var btnCancel = new Button
        {
            Text = "Close", Size = btnSize, Location = new Point(DpiScale.Px(110), btnY),
            FlatStyle = FlatStyle.Flat, ForeColor = ColorText, BackColor = ColorField,
            DialogResult = DialogResult.Cancel
        };
        btnCancel.FlatAppearance.BorderColor = ColorBorder;
        CancelButton = btnCancel;

        var btnEditModels = new Button
        {
            Text = "Edit Models...", Size = new Size(DpiScale.Px(120), DpiScale.Px(30)), Location = new Point(DpiScale.Px(10), btnY),
            FlatStyle = FlatStyle.Flat, ForeColor = ColorText, BackColor = ColorField
        };
        btnEditModels.FlatAppearance.BorderColor = ColorBorder;
        btnEditModels.Click += BtnEditMagnetrons_Click;

        var btnEditInstances = new Button
        {
            Text = "Edit Magnetrons...", Size = new Size(DpiScale.Px(140), DpiScale.Px(30)), Location = new Point(DpiScale.Px(140), btnY),
            FlatStyle = FlatStyle.Flat, ForeColor = ColorText, BackColor = ColorField
        };
        btnEditInstances.FlatAppearance.BorderColor = ColorBorder;
        btnEditInstances.Click += BtnEditInstances_Click;

        var btnEditFmt = new Button
        {
            Text = "Edit Formats...", Size = new Size(DpiScale.Px(120), DpiScale.Px(30)), Location = new Point(DpiScale.Px(290), btnY),
            FlatStyle = FlatStyle.Flat, ForeColor = ColorText, BackColor = ColorField
        };
        btnEditFmt.FlatAppearance.BorderColor = ColorBorder;
        btnEditFmt.Click += BtnEditFormats_Click;

        var btnEditPS = new Button
        {
            Text = "Edit Power Sources...", Size = new Size(DpiScale.Px(150), DpiScale.Px(30)), Location = new Point(DpiScale.Px(420), btnY),
            FlatStyle = FlatStyle.Flat, ForeColor = ColorText, BackColor = ColorField
        };
        btnEditPS.FlatAppearance.BorderColor = ColorBorder;
        btnEditPS.Click += BtnEditPowerSources_Click;

        _hints.Set(btnEditModels,    "Edit the magnetron model catalog (display names).");
        _hints.Set(btnEditInstances, "Manage physical magnetron instances — serial, model, format.");
        _hints.Set(btnEditFmt,       "Edit the target format/size catalog used for mount-fit checks.");
        _hints.Set(btnEditPS,        "Edit the list of power supplies that can be assigned to a flange.");

        _lblSaved = new Label { AutoSize = true, ForeColor = ColorDim, Font = ScaledFont.Create(8.5f), Text = "" };
        btnPanel.Controls.AddRange([btnSave, btnCancel, _lblSaved, btnEditModels, btnEditInstances, btnEditFmt, btnEditPS]);

        // Runs on every resize so Save/Cancel track the right edge. btnPanel is added to
        // the form later (see the ordered Dock block).
        void LayoutSaveCancel()
        {
            int gap = DpiScale.Px(10);
            btnSave.Location   = new Point(btnPanel.ClientSize.Width - DpiScale.Px(10) - btnSave.Width, btnY);
            btnCancel.Location = new Point(btnSave.Left - gap - btnCancel.Width, btnY);
            _lblSaved.Location = new Point(btnCancel.Left - gap - _lblSaved.Width, btnY + (btnSave.Height - _lblSaved.Height) / 2);
        }
        btnPanel.Resize += (_, _) => LayoutSaveCancel();
        LayoutSaveCancel();

        if (_readOnly)
            foreach (var b in new[] { btnSave, btnEditModels, btnEditInstances, btnEditFmt, btnEditPS })
                b.Enabled = false;

        int detailWidth = _mountPanel != null ? DpiScale.Px(440) : DpiScale.Px(360);
        var detailPanel = new Panel
        {
            Dock = DockStyle.Right, Width = detailWidth, BackColor = ColorBg,
            Padding = new Padding(DpiScale.Px(16), DpiScale.Px(12), DpiScale.Px(16), DpiScale.Px(12))
        };

        _lblTitle = new Label
        {
            Text = "FL 1 — Configuration", Font = ScaledFont.Create(11f, FontStyle.Bold),
            ForeColor = Color.Cyan, Dock = DockStyle.Top, Height = DpiScale.Px(28)
        };
        _lblChamberShutter = new Label
        {
            Text = "", Font = ScaledFont.Create(8.5f), ForeColor = ColorDim,
            Dock = DockStyle.Top, Height = DpiScale.Px(18), Visible = false
        };

        _txtName        = MakeTextBox();
        _txtMaterial    = MakeTextBox();
        _cboMagnetron   = MakeCombo();
        _cboPowerSupply = MakeCombo();
        _txtDescription = MakeTextBox();
        _txtComment     = MakeTextBox(); _txtComment.Multiline = true; _txtComment.Height = DpiScale.Px(50);
        _lblInstalled   = MakeLabel(""); _lblInstalled.ForeColor = ColorDim;
        _lblDepTime     = MakeLabel(""); _lblDepTime.ForeColor = ColorDim;
        _numZero        = MakeNumeric();
        _numLift        = MakeNumeric();
        _lblDistance    = MakeLabel(""); _lblDistance.ForeColor = ColorDim;

        var cfg = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, BackColor = ColorBg, AutoScroll = true,
            ColumnCount = 2, RowCount = 6,
        };
        cfg.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        cfg.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        cfg.RowStyles.Add(new RowStyle(SizeType.Absolute, DpiScale.Px(50)));
        cfg.RowStyles.Add(new RowStyle(SizeType.Absolute, DpiScale.Px(50)));
        cfg.RowStyles.Add(new RowStyle(SizeType.Absolute, DpiScale.Px(50)));
        cfg.RowStyles.Add(new RowStyle(SizeType.Absolute, DpiScale.Px(50)));  // Zero distance / Lift position
        cfg.RowStyles.Add(new RowStyle(SizeType.Absolute, DpiScale.Px(50)));  // Target-substrate distance
        cfg.RowStyles.Add(new RowStyle(SizeType.Absolute, DpiScale.Px(78)));  // Comment

        // Name falls back to Material when blank (SourceNames.Effective), so it sits
        // directly beside the Material field it defaults from.
        var topRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, BackColor = ColorBg, Margin = new Padding(0),
            ColumnCount = 3, RowCount = 1,
        };
        topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34f));
        topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33f));
        topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33f));
        topRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        topRow.Controls.Add(MakeField("Name",      _txtName),      0, 0);
        topRow.Controls.Add(MakeField("Material",  _txtMaterial),  1, 0);
        topRow.Controls.Add(MakeField("Magnetron", _cboMagnetron), 2, 0);
        cfg.Controls.Add(topRow, 0, 0);
        cfg.SetColumnSpan(topRow, 2);

        cfg.Controls.Add(MakeField("Power Supply",          _cboPowerSupply), 0, 1);
        cfg.Controls.Add(MakeField("Description",           _txtDescription), 1, 1);
        cfg.Controls.Add(MakeField("Installed",             _lblInstalled),   0, 2);
        cfg.Controls.Add(MakeField("Total Deposition Time", _lblDepTime),     1, 2);
        cfg.Controls.Add(MakeField("Zero distance (mm)", _numZero), 0, 3);
        cfg.Controls.Add(MakeField("Lift position (mm)", _numLift), 1, 3);
        var distField = MakeField("Target–substrate distance (mm)", _lblDistance);
        cfg.Controls.Add(distField, 0, 4);
        cfg.SetColumnSpan(distField, 2);
        var commentField = MakeField("Comment", _txtComment);
        cfg.Controls.Add(commentField, 0, 5);
        cfg.SetColumnSpan(commentField, 2);

        detailPanel.Controls.Add(cfg);
        detailPanel.Controls.Add(_lblChamberShutter);
        detailPanel.Controls.Add(_lblTitle);

        if (_mountPanel != null)
        {
            _mountPanel.View.Dock   = DockStyle.Bottom;
            _mountPanel.View.Height = DpiScale.Px(300);
            _mountPanel.MountChanged += (_, _) => OnMountChanged();
            detailPanel.Controls.Add(_mountPanel.View);

            // Tick the live read-outs (burn-through ETA from live PSU watts; profile
            // re-read from the DB) so they update while watching a running deposition.
            var liveTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            liveTimer.Tick += (_, _) => _mountPanel.RefreshLive();
            liveTimer.Start();
            FormClosed += (_, _) => { liveTimer.Stop(); liveTimer.Dispose(); };
        }

        _schematic = new ChamberSourceSchematic { Dock = DockStyle.Fill };
        _schematic.LayoutResolver = ch =>
        {
            var l = _sourceManager.GetLayout(ch);
            return (l.StartDegrees, l.Clockwise);
        };
        _schematic.FlangeClicked += (_, flange) => SelectFlange(flange);

        // Dock resolution order is critical: WinForms resolves Dock in REVERSE add order,
        // the LAST control added claims its edge first, and a Dock=Fill control fills
        // whatever remains. Adding the schematic last makes it seize the entire client area
        // and draw its flange circles under the detail panel. SendToBack does NOT fix it.
        Controls.Add(_schematic);                  // Dock=Fill   → left remainder
        Controls.Add(detailPanel);                 // Dock=Right
        Controls.Add(btnPanel);                    // Dock=Bottom → full width
        if (banner != null) Controls.Add(banner);  // Dock=Top    → full width

        RebuildMagnetronCombo();

        _cboPowerSupply.Items.Add("(none)");
        foreach (var ps in _sourceManager.PowerSources)
            _cboPowerSupply.Items.Add(ps);

        _txtName.TextChanged += (_, _) => SaveFieldToSource();
        _txtMaterial.TextChanged += (_, _) => SaveFieldToSource();
        _cboMagnetron.SelectedIndexChanged += (_, _) => SaveFieldToSource();
        _cboPowerSupply.SelectedIndexChanged += (_, _) => SaveFieldToSource();
        _txtDescription.TextChanged += (_, _) => SaveFieldToSource();
        _txtComment.TextChanged += (_, _) => SaveFieldToSource();
        _numZero.ValueChanged += (_, _) => SaveFieldToSource();
        _numLift.ValueChanged += (_, _) => SaveFieldToSource();

        DarkModeHelper.FixControls(this);
        RefreshSchematic();

        _hints.Set(_txtName,        "Operator-facing recipe handle for this source (deposit/holder/shutter). " +
                                     "Leave blank to use Material. Uniqueness across sources is checked on " +
                                     "Save (blocks) and warned about on target mount — not per keystroke.");
        _hints.Set(_txtMaterial,    "Target material on this flange. Locked when a target is mounted from inventory.");
        _hints.Set(_cboMagnetron,   "Magnetron (cathode) instance installed on this flange.");
        _hints.Set(_cboPowerSupply, "Power supply that drives this flange's magnetron.");
        _hints.Set(_txtDescription, "Free-text label shown for this flange.");
        _hints.Set(_txtComment,     "Notes about this source (kept here only).");
        _hints.Set(_numZero,        "Bare magnetron-face-to-substrate distance measured at install, lift at 0.");
        _hints.Set(_numLift,        "Lift gauge reading; lifting the magnetron raises the target away from the substrate, increasing the working distance.");
        _hints.Set(_schematic,      "Chamber schematic — click a flange to select it; the ring colour shows target erosion life. The PSU under the flange number turns amber when that PSU is also assigned to another flange.");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _hints?.Dispose();
        base.Dispose(disposing);
    }

    private static Label MakeLabel(string text) => new()
    {
        Text = text, Font = ScaledFont.Create(8.5f), ForeColor = ColorDim,
        Height = DpiScale.Px(18), Width = DpiScale.Px(260), Margin = new Padding(0, DpiScale.Px(6), 0, 0)
    };

    private static TextBox MakeTextBox() => new()
    {
        Width = DpiScale.Px(260), Height = DpiScale.Px(24), Font = ScaledFont.Create(10f),
        BackColor = ColorField, ForeColor = ColorText,
        BorderStyle = BorderStyle.FixedSingle
    };

    // DarkComboBox (not stock ComboBox): Windows paints the dropdown-arrow chrome
    // grey regardless of BackColor/FlatStyle, so the dark theme needs the custom
    // widget here. (audit M-16)
    private static ComboBox MakeCombo() => new DarkComboBox
    {
        Width = DpiScale.Px(260), Height = DpiScale.Px(24), Font = ScaledFont.Create(10f),
        BackColor = ColorField, ForeColor = ColorText,
        FlatStyle = FlatStyle.Flat, DropDownStyle = ComboBoxStyle.DropDownList
    };

    private static DarkNumericUpDown MakeNumeric() => new()
    {
        Minimum = 0, Maximum = 10000, DecimalPlaces = 1, Increment = 0.5m, Dock = DockStyle.Top,
    };

    private static Panel MakeField(string labelText, Control input)
    {
        var p = new Panel
        {
            Dock      = DockStyle.Fill,
            Margin    = new Padding(0, DpiScale.Px(2), DpiScale.Px(14), DpiScale.Px(4)),
            BackColor = ColorBg,
        };
        input.Dock = DockStyle.Top;
        var lbl = MakeLabel(labelText);
        lbl.Dock = DockStyle.Top;
        p.Controls.Add(input);   // added first → docks below the label
        p.Controls.Add(lbl);     // added last  → claims the top edge
        return p;
    }

    private bool _suppressSave;

    /// <summary>
    /// A NumericUpDown only turns typed text into Value when it loses focus, and the
    /// schematic canvas never takes focus, so a distance typed and then followed by a
    /// click on another flange is overwritten by that flange's value. Push any pending
    /// edit text through Value (which fires ValueChanged → SaveFieldToSource) before the
    /// current flange changes.
    /// </summary>
    private void CommitPendingNumericEdits()
    {
        foreach (var n in new[] { _numZero, _numLift })
        {
            if (n is null || !decimal.TryParse(n.Text, System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.CurrentCulture, out var typed)) continue;
            typed = Math.Clamp(typed, n.Minimum, n.Maximum);
            if (typed != n.Value) n.Value = typed;
        }
    }

    private void SelectFlange(int flangeNum)
    {
        if (_selectedFlange != flangeNum) CommitPendingNumericEdits();
        _selectedFlange = flangeNum;
        var src = _sourceManager.GetSource(flangeNum);
        if (src == null) return;

        _suppressSave = true;

        _lblTitle.Text = $"FL {flangeNum} — Configuration";
        _txtName.Text = src.Name;
        _txtMaterial.Text = src.Material;

        _cboMagnetron.SelectedIndex = 0;
        for (int i = 0; i < _sourceManager.MagnetronInstances.Count; i++)
        {
            if (_sourceManager.MagnetronInstances[i].Id == src.MagnetronInstanceId)
            {
                _cboMagnetron.SelectedIndex = i + 1;
                break;
            }
        }

        _cboPowerSupply.SelectedIndex = 0;
        int psIdx = _sourceManager.PowerSources.IndexOf(src.PowerSupplyId);
        if (psIdx >= 0) _cboPowerSupply.SelectedIndex = psIdx + 1;

        _txtDescription.Text = src.Description;
        _txtComment.Text = src.Comment;
        _lblInstalled.Text = src.InstalledDate.ToString("yyyy-MM-dd");
        _lblDepTime.Text = src.TotalDepositionTime.ToString(@"hh\:mm\:ss");
        _numZero.Value = (decimal)(src.ZeroDistanceMm ?? 0);
        _numLift.Value = (decimal)(src.LiftPositionMm ?? 0);

        _suppressSave = false;

        if (_mountPanel != null)
        {
            SourceMountSelection? info = null;
            if (!string.IsNullOrEmpty(src.MagnetronInstanceId))
            {
                var inst = _sourceManager.MagnetronInstances
                    .FirstOrDefault(m => m.Id == src.MagnetronInstanceId);
                if (inst != null && Guid.TryParse(src.MagnetronInstanceId, out var instanceGuid))
                    info = new SourceMountSelection(
                        instanceGuid,
                        inst.Label,
                        inst.Format,
                        flangeNum,
                        src.PowerSupplyId,
                        inst.PhysicsJson);
            }
            _mountPanel.ShowFor(info);
        }

        // Live target-substrate distance for this flange, using the mounted target's
        // thickness/backing/erosion (freshly loaded by ShowFor above).
        UpdateDistanceLabel(src);

        UpdateMaterialLock();

        _lblChamberShutter.Visible = !string.IsNullOrEmpty(src.Chamber);
        _lblChamberShutter.Text = string.IsNullOrEmpty(src.Chamber) ? ""
            : $"Chamber: {src.Chamber}"
              + (string.IsNullOrEmpty(src.ShutterId) ? "" : $"    ·    Shutter: {src.ShutterId}");

        if (_schematic != null) _schematic.SelectedFlange = flangeNum;

        // During a deposition every editable field is locked (overrides the
        // mount-driven Material lock above).
        if (_readOnly)
        {
            _txtName.ReadOnly        = true;
            _txtMaterial.ReadOnly    = true;
            _txtDescription.ReadOnly = true;
            _txtComment.ReadOnly     = true;
            _cboMagnetron.Enabled    = false;
            _cboPowerSupply.Enabled  = false;
            // Geometry too — these mutate the live Source mid-deposition and feed
            // the authorize gate (code audit 2026-08-29, C7).
            _numZero.Enabled         = false;
            _numLift.Enabled         = false;
        }
    }

    /// <summary>
    /// Queries the DB (if available) for an open Installation on the selected flange's
    /// magnetron, then locks/unlocks the Material field accordingly.
    /// READ-ONLY: does not call SaveFieldToSource, does not write Sources.xml, and does
    /// not rebaseline _originalSources. Safe to call on open and on every flange click.
    /// </summary>
    private void UpdateMaterialLock()
    {
        if (_mountExtension == null) return;
        var src = _sourceManager.GetSource(_selectedFlange);
        if (src == null || string.IsNullOrEmpty(src.MagnetronInstanceId)
            || !Guid.TryParse(src.MagnetronInstanceId, out var magGuid))
        {
            _txtMaterial.ReadOnly = false;
            return;
        }

        _mountExtension.GetMountStatus().TryGetValue(magGuid, out var mounted);

        _suppressSave = true;
        if (mounted != null)
        {
            _txtMaterial.Text     = mounted.Material;
            _txtMaterial.ReadOnly = true;
        }
        else
        {
            _txtMaterial.ReadOnly = false;
        }
        _suppressSave = false;
    }

    /// <summary>
    /// Handles a real Mount/Unmount/Record action from <see cref="ISourceMountPanel.MountChanged"/>.
    /// Derives/locks the Material field, then immediately persists Sources.xml and rebaselines
    /// _originalSources for this flange so Cancel cannot undo the DB-committed mount state.
    /// NOT called on open or on flange selection — only on actual mount events.
    /// </summary>
    private void OnMountChanged()
    {
        // If the operator entered a zero distance in the Mount dialog, push it into the UI
        // field so the SaveFieldToSource() call below persists it into the flange's Source
        // exactly like a manual edit would — MountedTargetPanel has no SourceManager reference
        // of its own to write Sources.xml directly (see TakeLastMountZeroDistanceMm()).
        if (_mountPanel?.TakeLastMountZeroDistanceMm() is { } z)
            _numZero.Value = (decimal)z;

        UpdateMaterialLock();

        // Persist the derived material immediately so it survives a later dialog Cancel.
        SaveFieldToSource();

        // A mount can silently give two flanges the same effective name (e.g. mounting the
        // same material onto a second nameless flange). Unlike BtnSave_Click we do NOT block
        // here — the physical mount already happened and Sources.xml must reflect the
        // machine's real state, so we warn the operator and still persist (the runtime
        // worker degrades an ambiguous handle safely; a stale file would not).
        var dupes = FindDuplicateSourceNames();
        if (dupes.Count > 0)
        {
            MessageBox.Show(this,
                $"Duplicate source name(s) after mount: {string.Join(", ", dupes)}.\n" +
                "Open Sources and give the affected sources distinct Names so recipes can " +
                "tell them apart.",
                "Source names are ambiguous", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        if (!string.IsNullOrEmpty(_filePath))
            _sourceManager.Save(_filePath);

        // Refresh the rollback baseline for this flange so that a subsequent Cancel does
        // not revert the mount-derived Material (the file already has the new value).
        var committed = _sourceManager.GetSource(_selectedFlange);
        if (committed != null)
            _originalSources[_selectedFlange] = (committed.Name, committed.Material, committed.Description,
                committed.Comment, committed.MagnetronId, committed.Model,
                committed.PowerSupplyId, committed.MagnetronInstanceId,
                committed.ZeroDistanceMm, committed.LiftPositionMm);

        // Immediately refresh the live runtime (process panel, PSU visibility, magnetron
        // mirror, worker source map) so the in-place mount takes effect without waiting
        // for the dialog to close with OK.
        _onSourcesCommitted?.Invoke();

        // A mount changed life %, so refresh the cache and repaint the rings.
        RefreshLifeCache();
        RefreshSchematicKeepSelection();
    }

    private void SaveFieldToSource()
    {
        if (_suppressSave) return;
        // Defense in depth for read-only mode: the editors are disabled, but a
        // programmatic value change still fires ValueChanged — never mutate the
        // live Source during a deposition (code audit 2026-08-29, C7).
        if (_readOnly) return;
        var src = _sourceManager.GetSource(_selectedFlange);
        if (src == null) return;

        // Zero distance / lift: treat 0 as "unset" so a blank field stays null rather than
        // storing a nonsensical zero working distance.
        src.ZeroDistanceMm = _numZero.Value > 0 ? (double)_numZero.Value : null;
        src.LiftPositionMm = _numLift.Value > 0 ? (double)_numLift.Value : null;
        UpdateDistanceLabel(src);

        // Name is trimmed (it's the recipe-facing handle — leading/trailing whitespace would
        // make two visually-identical names compare unequal); Material is stored verbatim to
        // match the mount-derived value exactly. Intentional asymmetry — don't "fix" it.
        src.Name = _txtName.Text.Trim();
        src.Material = _txtMaterial.Text;
        src.Description = _txtDescription.Text;
        src.Comment = _txtComment.Text;

        if (_cboMagnetron.SelectedIndex > 0)
        {
            var inst = _sourceManager.MagnetronInstances[_cboMagnetron.SelectedIndex - 1];
            src.MagnetronInstanceId = inst.Id;
            src.MagnetronId = inst.ModelId;   // model id for the emulator contract
            src.Model = _sourceManager.Magnetrons.FirstOrDefault(m => m.Id == inst.ModelId)?.DisplayName ?? inst.ModelId;
        }
        else
        {
            src.MagnetronInstanceId = "";
            src.MagnetronId = "";
            src.Model = "";
        }

        if (_cboPowerSupply.SelectedIndex > 0)
            src.PowerSupplyId = _sourceManager.PowerSources[_cboPowerSupply.SelectedIndex - 1];
        else
            src.PowerSupplyId = "";

        if (_schematic != null) RefreshSchematicKeepSelection();
    }

    /// <summary>
    /// Recomputes the live target-substrate distance for <paramref name="src"/> from its
    /// zero/lift fields plus the mounted target's thickness/backing/erosion (via the hosted
    /// MountedTargetPanel), and updates the read-out label. Guarded with <c>?.</c> throughout
    /// so a missing panel/target (Standalone mode, or no target mounted) reads "—" rather than
    /// crashing or fabricating a number.
    /// </summary>
    private void UpdateDistanceLabel(Source src)
    {
        var t = _mountPanel?.CurrentTarget;
        double? d = APS.SystemControl.Core.Production.TargetSubstrateDistance.Effective(
            src.ZeroDistanceMm, src.LiftPositionMm,
            t?.ThicknessMm, t?.BackingThicknessMm ?? 0, t?.CurrentErosionDepthMm ?? 0);
        _lblDistance.Text = d is { } v
            ? v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " mm"
            : "—";
    }

    private void RefreshSchematic()
    {
        RefreshLifeCache();
        _schematic.Flanges = BuildFlangeViews();
        int first = _sourceManager.Sources.Keys.DefaultIfEmpty(1).Min();
        _schematic.SelectedFlange = first;
        SelectFlange(first);
    }

    // Rebuilds the view-model from current Source state + the (already-queried) life cache.
    // DB-free — safe to call on every keystroke. Preserves the current selection highlight.
    private void RefreshSchematicKeepSelection()
    {
        _schematic.Flanges = BuildFlangeViews();
        _schematic.SelectedFlange = _selectedFlange;
    }

    private IReadOnlyList<ChamberSourceSchematic.FlangeView> BuildFlangeViews() =>
        SourceSchematicMapper.Build(
            _sourceManager.Sources,
            _sourceManager.MagnetronInstances,
            flange => _lifeCache.TryGetValue(flange, out var lc) ? lc : ((double Life, double Warn)?)null);

    // One DB pass: for each flange with a mounted target, cache life % + warn fraction.
    private void RefreshLifeCache()
    {
        _lifeCache.Clear();
        if (_mountExtension == null) return;
        var status = _mountExtension.GetMountStatus();
        foreach (var (flange, src) in _sourceManager.Sources)
        {
            if (string.IsNullOrEmpty(src.MagnetronInstanceId)
                || !Guid.TryParse(src.MagnetronInstanceId, out var magGuid)) continue;
            if (status.TryGetValue(magGuid, out var s))
                _lifeCache[flange] = (s.LifeFraction, s.WarnFraction);
        }
    }

    // Edits the model catalog (DisplayName, Id), not magnetron instances.
    private void BtnEditMagnetrons_Click(object? sender, EventArgs e)
    {
        var items = _sourceManager.Magnetrons.Select(m => $"{m.Id}|{m.DisplayName}").ToList();
        using var dlg = new frmListEditor("Edit Magnetron Models", items);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _sourceManager.Magnetrons = dlg.Items
                .Select(s => { var p = s.Split('|', 2); return new Core.Classes.MagnetronDef(p[0], p.Length > 1 ? p[1] : p[0]); })
                .ToList();
            // Magnetron combo lists instances (not models), so no rebuild needed.
            // But we do re-select in case the chosen instance's derived Model label changed.
            SelectFlange(_selectedFlange);
        }
    }

    private void BtnEditInstances_Click(object? sender, EventArgs e)
    {
        using var dlg = new Sources.MagnetronInstanceDialog(
            _sourceManager.MagnetronInstances, _sourceManager.Magnetrons, _sourceManager.Formats);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _sourceManager.MagnetronInstances = dlg.Instances;
            RebuildMagnetronCombo();
            SelectFlange(_selectedFlange);
            RefreshSchematicKeepSelection();
        }
    }

    private void BtnEditFormats_Click(object? sender, EventArgs e)
    {
        var items = _sourceManager.Formats.Select(f => $"{f.Id}|{f.Label}").ToList();
        using var dlg = new frmListEditor("Edit Formats", items);
        if (dlg.ShowDialog(this) == DialogResult.OK)
            _sourceManager.Formats = dlg.Items
                .Select(s => { var p = s.Split('|', 2); return new Core.Classes.SourceFormat(p[0], p.Length > 1 ? p[1] : p[0]); })
                .ToList();
    }

    private void RebuildMagnetronCombo()
    {
        _cboMagnetron.Items.Clear();
        _cboMagnetron.Items.Add("(none)");
        foreach (var inst in _sourceManager.MagnetronInstances)
            _cboMagnetron.Items.Add(inst.Label);
    }

    private void BtnEditPowerSources_Click(object? sender, EventArgs e)
    {
        using var dlg = new frmListEditor("Edit Power Sources", _sourceManager.PowerSources);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _sourceManager.PowerSources = dlg.Items;
            _cboPowerSupply.Items.Clear();
            _cboPowerSupply.Items.Add("(none)");
            foreach (var ps in _sourceManager.PowerSources)
                _cboPowerSupply.Items.Add(ps);
            SelectFlange(_selectedFlange);
            RefreshSchematicKeepSelection();
        }
    }

    // Effective names (Name, falling back to Material) must be unique — recipes
    // (deposit/holder/shutter) address sources by this handle. Shared by the manual
    // Save gate (BtnSave_Click, blocking) and the mount-path warning (OnMountChanged,
    // non-blocking) so both use the exact same collision rule.
    private IReadOnlyList<string> FindDuplicateSourceNames()
    {
        var eff = _sourceManager.Sources.Values
            .Where(s => !string.IsNullOrWhiteSpace(s.Material) || !string.IsNullOrWhiteSpace(s.Name))
            .Select(s => SourceNames.Effective(s.Name, s.Material));
        return SourceNames.Duplicates(eff);
    }

    /// <summary>
    /// Save gate, re-evaluated AT SAVE TIME (not just at dialog open): readOnly
    /// is captured when the dialog opens, but a recipe can start from the
    /// agent/remote channels while it is up — Sources.xml must never be
    /// rewritten mid-run (code audit 2026-08-29, C28). Returns the blocking
    /// reason, or null when saving is allowed. (Internal for CoreLogicTests.)
    /// </summary>
    internal string? ValidateSave()
    {
        if (_core?.IsDepositionActive == true)
            return "A deposition became active while this dialog was open — " +
                   "sources cannot be saved during a run. Retry after the run completes.";

        var dupes = FindDuplicateSourceNames();
        if (dupes.Count > 0)
            return $"Duplicate source name(s): {string.Join(", ", dupes)}.\n" +
                   "Give same-material sources distinct Names before saving.";

        return null;
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        if (ValidateSave() is string reason)
        {
            MessageBox.Show(this, reason, "Cannot save sources",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        CommitPendingNumericEdits();
        if (!string.IsNullOrEmpty(_filePath))
            _sourceManager.Save(_filePath);

        // Save does not close the dialog: an operator configuring several flanges needs it
        // open. Push the change into the running system now, re-take the rollback snapshot
        // so Close keeps what was saved, and re-bind the detail panel in case the reload
        // touched the live objects.
        _onSourcesCommitted?.Invoke();
        Rebaseline();
        if (_selectedFlange > 0) SelectFlange(_selectedFlange);
        RefreshLifeCache();
        if (_schematic != null) RefreshSchematicKeepSelection();
        _lblSaved.Text = $"Saved {DateTime.Now:HH:mm:ss}";
        _lblSaved.Parent?.PerformLayout();
    }
}
