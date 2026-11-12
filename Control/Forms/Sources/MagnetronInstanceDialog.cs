using APS.Controls.Theme;
using APS.Production.Domain;
using APS.SystemControl.Controls.General;
using APS.SystemControl.Controls.Theme;
using APS.SystemControl.Core.Classes;

namespace APS.SystemControl.Forms.Sources;

public sealed class MagnetronInstanceDialog : Form
{
    public List<MagnetronInstance> Instances { get; private set; }

    private readonly List<MagnetronDef>    _models;
    private readonly List<SourceFormat>    _formats;
    private int _selectedIdx = -1;
    private bool _suppressDetail;      // block re-entrant saves during SelectInstance
    private bool _syncingList;         // block SelectedIndexChanged during in-place item rewrites

    private readonly ListBox _list;
    private readonly Button  _btnAdd;
    private readonly Button  _btnRemove;

    private readonly TextBox  _txtLabel;
    private readonly TextBox  _txtSerial;
    private readonly DarkComboBox _cboModel;
    private readonly DarkComboBox _cboFormat;
    private readonly DarkComboBox _cboState;
    private readonly TextBox  _txtPhysics;
    private readonly TextBox  _txtComment;
    private readonly Label    _lblDetailTitle;

    private DarkNumericUpDown _numPeakRadius = null!;
    private DarkNumericUpDown _numWidth = null!;
    private DarkComboBox      _cboShape = null!;
    private Label             _lblHealth = null!;
    private Label             _lblService = null!;
    private HintManager       _hints = null!;

    private static Color ColorBg     => VacuumTheme.PanelBackground;
    private static Color ColorText   => VacuumTheme.TextPrimary;
    private static Color ColorDim    => VacuumTheme.TextDim;
    private static Color ColorBorder => VacuumTheme.PanelBorder;
    private static Color ColorField  => VacuumTheme.PanelField;

    public MagnetronInstanceDialog(
        List<MagnetronInstance> current,
        List<MagnetronDef>      models,
        List<SourceFormat>      formats)
    {
        Instances = current.ToList();   // work on a copy; caller replaces on OK
        _models   = models;
        _formats  = formats;

        APS.SystemControl.Core.UIScaling.ApplyToForm(this);
        Text             = "Edit Magnetron Instances";
        Size             = new Size(DpiScale.Px(680), DpiScale.Px(520));
        MinimumSize      = new Size(DpiScale.Px(580), DpiScale.Px(420));
        StartPosition    = FormStartPosition.CenterParent;
        MaximizeBox      = false;
        MinimizeBox      = false;
        FormBorderStyle  = FormBorderStyle.Sizable;
        BackColor        = ColorBg;
        ForeColor        = ColorText;

        var btnPanel = new Panel { Dock = DockStyle.Bottom, Height = DpiScale.Px(45), BackColor = ColorBg };
        int btnY = DpiScale.Px(8);
        var btnSize = new Size(DpiScale.Px(90), DpiScale.Px(28));

        var btnOK = new Button
        {
            Text = "OK", Size = btnSize, Location = new Point(DpiScale.Px(10), btnY),
            FlatStyle = FlatStyle.Flat, ForeColor = ColorText, BackColor = ColorField,
            DialogResult = DialogResult.OK
        };
        btnOK.FlatAppearance.BorderColor = ColorBorder;
        btnOK.Click += BtnOK_Click;

        var btnCancel = new Button
        {
            Text = "Cancel", Size = btnSize, Location = new Point(DpiScale.Px(110), btnY),
            FlatStyle = FlatStyle.Flat, ForeColor = ColorText, BackColor = ColorField,
            DialogResult = DialogResult.Cancel
        };
        btnCancel.FlatAppearance.BorderColor = ColorBorder;
        AcceptButton = btnOK;
        CancelButton = btnCancel;

        btnPanel.Controls.AddRange([btnOK, btnCancel]);

        var listPanel = new Panel
        {
            Dock = DockStyle.Left, Width = DpiScale.Px(230), BackColor = ColorBg,
            Padding = new Padding(DpiScale.Px(10), DpiScale.Px(10), DpiScale.Px(6), DpiScale.Px(6))
        };

        _list = new ListBox
        {
            Left = DpiScale.Px(10), Top = DpiScale.Px(10),
            Width = DpiScale.Px(200), Height = DpiScale.Px(380),
            Font      = ScaledFont.Create(10f),
            BackColor = ColorField, ForeColor = ColorText,
            BorderStyle = BorderStyle.FixedSingle,
            ScrollAlwaysVisible = false
        };
        _list.SelectedIndexChanged += List_SelectedIndexChanged;

        _btnAdd = new Button
        {
            Text = "Add", Left = DpiScale.Px(10), Top = DpiScale.Px(398),
            Size = new Size(DpiScale.Px(94), DpiScale.Px(28)),
            FlatStyle = FlatStyle.Flat, ForeColor = ColorText, BackColor = ColorField
        };
        _btnAdd.FlatAppearance.BorderColor = ColorBorder;
        _btnAdd.Click += BtnAdd_Click;

        _btnRemove = new Button
        {
            Text = "Remove", Left = DpiScale.Px(116), Top = DpiScale.Px(398),
            Size = new Size(DpiScale.Px(94), DpiScale.Px(28)),
            FlatStyle = FlatStyle.Flat, ForeColor = ColorText, BackColor = ColorField
        };
        _btnRemove.FlatAppearance.BorderColor = ColorBorder;
        _btnRemove.Click += BtnRemove_Click;

        listPanel.Controls.AddRange([_list, _btnAdd, _btnRemove]);

        var detailPanel = new Panel
        {
            Dock = DockStyle.Fill, BackColor = ColorBg,
            Padding = new Padding(DpiScale.Px(10), DpiScale.Px(10), DpiScale.Px(12), DpiScale.Px(6))
        };

        _lblDetailTitle = new Label
        {
            Text = "(select an instance)", Font = ScaledFont.Create(10.5f, FontStyle.Bold),
            ForeColor = Color.Cyan, Dock = DockStyle.Top, Height = DpiScale.Px(26)
        };

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true, BackColor = ColorBg
        };
        flow.Controls.Add(MakeLabel("Label"));
        _txtLabel = MakeTextBox(); flow.Controls.Add(_txtLabel);

        flow.Controls.Add(MakeLabel("Serial"));
        _txtSerial = MakeTextBox(); flow.Controls.Add(_txtSerial);

        flow.Controls.Add(MakeLabel("Model"));
        _cboModel = MakeCombo(); flow.Controls.Add(_cboModel);

        flow.Controls.Add(MakeLabel("Format"));
        _cboFormat = MakeCombo(); flow.Controls.Add(_cboFormat);

        flow.Controls.Add(MakeLabel("State"));
        _cboState = MakeCombo(); flow.Controls.Add(_cboState);

        // Not added to the flow: racetrack editing goes through the structured controls
        // below and PhysicsJson is written via MagnetronPhysics.ToJson().
        _txtPhysics = MakeTextBox();

        flow.Controls.Add(MakeLabel("Racetrack peak radius (mm)"));
        _numPeakRadius = MakeNud(0, 200, 2);
        flow.Controls.Add(_numPeakRadius);

        flow.Controls.Add(MakeLabel("Racetrack width (mm)"));
        _numWidth = MakeNud(0, 100, 2);
        flow.Controls.Add(_numWidth);

        flow.Controls.Add(MakeLabel("Racetrack shape"));
        _cboShape = MakeCombo();
        _cboShape.Items.AddRange(new object[] { "gaussian", "uniform" });
        flow.Controls.Add(_cboShape);

        flow.Controls.Add(MakeLabel("Health (runtime)"));
        _lblHealth = MakeReadonlyLabel();
        flow.Controls.Add(_lblHealth);
        _lblService = MakeReadonlyLabel();
        flow.Controls.Add(_lblService);

        flow.Controls.Add(MakeLabel("Comment"));
        _txtComment = MakeTextBox();
        _txtComment.Height    = DpiScale.Px(60);
        _txtComment.Multiline = true;
        flow.Controls.Add(_txtComment);

        detailPanel.Controls.Add(flow);
        detailPanel.Controls.Add(_lblDetailTitle);

        // Dock order matters: add the Fill detail panel FIRST so the Left list and
        // Bottom button bar carve their edges and the detail fills the remainder.
        // (Otherwise the Fill panel spans full width and the list overlaps its labels.)
        Controls.Add(detailPanel);
        Controls.Add(listPanel);
        Controls.Add(btnPanel);

        _cboModel.Items.Add("(none)");
        foreach (var m in _models)
            _cboModel.Items.Add(m.DisplayName);

        _cboFormat.Items.Add("(none)");
        foreach (var f in _formats)
            _cboFormat.Items.Add(f.Label);

        _cboState.Items.Add("Active");
        _cboState.Items.Add("Retired");

        foreach (var inst in Instances)
            _list.Items.Add(inst.Label);

        _txtLabel.TextChanged               += (_, _) => SaveDetailToInstance();
        _txtSerial.TextChanged              += (_, _) => SaveDetailToInstance();
        _cboModel.SelectedIndexChanged      += (_, _) => SaveDetailToInstance();
        _cboFormat.SelectedIndexChanged     += (_, _) => SaveDetailToInstance();
        _cboState.SelectedIndexChanged      += (_, _) => SaveDetailToInstance();
        _numPeakRadius.ValueChanged         += (_, _) => SaveDetailToInstance();
        _numWidth.ValueChanged              += (_, _) => SaveDetailToInstance();
        _cboShape.SelectedIndexChanged      += (_, _) => SaveDetailToInstance();
        _txtComment.TextChanged             += (_, _) => SaveDetailToInstance();

        DarkModeHelper.FixControls(this);

        _hints = new HintManager(this);
        _hints.Set(_cboModel,      "Magnetron model (from the model catalog).");
        _hints.Set(_cboFormat,     "Target shape/size this magnetron accepts; used for mount-fit checks.");
        _hints.Set(_cboState,      "Retired instances stay on record but can no longer be assigned to a flange.");
        _hints.Set(_numPeakRadius, "Racetrack peak-erosion radius (mm) — where the plasma ring erodes the target deepest.");
        _hints.Set(_numWidth,      "Racetrack ring width (mm).");
        _hints.Set(_cboShape,      "Erosion-profile shape: gaussian (peaked ring) or uniform (flat).");
        _hints.Set(_lblHealth,     "Accrued automatically from delivered PSU power; editing the racetrack does not reset it.");

        HandleCreated += (_, _) =>
        {
            var bg = VacuumTheme.PanelBackground;
            int colorRef = bg.R | (bg.G << 8) | (bg.B << 16);
            DwmSetWindowAttribute(Handle, 35, ref colorRef, sizeof(int));
        };

        SetDetailEnabled(false);
    }

    private static Label MakeLabel(string text) => new()
    {
        Text = text, Font = ScaledFont.Create(8.5f), ForeColor = ColorDim,
        Height = DpiScale.Px(18), Width = DpiScale.Px(390),
        Margin = new Padding(0, DpiScale.Px(6), 0, 0)
    };

    private static TextBox MakeTextBox() => new()
    {
        Width = DpiScale.Px(390), Height = DpiScale.Px(24), Font = ScaledFont.Create(10f),
        BackColor = ColorField, ForeColor = ColorText,
        BorderStyle = BorderStyle.FixedSingle
    };

    private static DarkComboBox MakeCombo() => new DarkComboBox
    {
        Width = DpiScale.Px(390), Height = DpiScale.Px(24), Font = ScaledFont.Create(10f),
        BackColor = ColorField, ForeColor = ColorText,
        FlatStyle = FlatStyle.Flat, DropDownStyle = ComboBoxStyle.DropDownList
    };

    private static DarkNumericUpDown MakeNud(decimal min, decimal max, int dp) => new()
    {
        Minimum = min, Maximum = max, DecimalPlaces = dp,
        Width = DpiScale.Px(390), Height = DpiScale.Px(24),
        Font = ScaledFont.Create(10f), BackColor = ColorField, ForeColor = ColorText
    };

    private static Label MakeReadonlyLabel() => new()
    {
        AutoSize = true, ForeColor = ColorText, Font = ScaledFont.Create(9f)
    };

    private void SetDetailEnabled(bool enabled)
    {
        _txtLabel.Enabled      = enabled;
        _txtSerial.Enabled     = enabled;
        _cboModel.Enabled      = enabled;
        _cboFormat.Enabled     = enabled;
        _cboState.Enabled      = enabled;
        _numPeakRadius.Enabled = enabled;
        _numWidth.Enabled      = enabled;
        _cboShape.Enabled      = enabled;
        _txtComment.Enabled    = enabled;
    }

    private void List_SelectedIndexChanged(object? sender, EventArgs e)
    {
        // Ignore the selection churn caused by in-place ListBox item rewrites
        // (SaveDetailToInstance re-selects after editing the Label).
        if (_syncingList) return;

        // Capture the clicked index up front: the flush below rewrites the old item
        // and momentarily restores its selection, so _list.SelectedIndex is unreliable
        // afterwards.
        int newIdx = _list.SelectedIndex;
        SaveDetailToInstance();
        if (_list.SelectedIndex != newIdx)
        {
            _syncingList = true;
            _list.SelectedIndex = newIdx;
            _syncingList = false;
        }
        SelectInstance(newIdx);
    }

    private void SelectInstance(int idx)
    {
        _selectedIdx = idx;
        if (idx < 0 || idx >= Instances.Count)
        {
            _lblDetailTitle.Text = "(select an instance)";
            SetDetailEnabled(false);
            return;
        }

        _suppressDetail = true;

        var inst = Instances[idx];
        _lblDetailTitle.Text = inst.Label;
        SetDetailEnabled(true);

        _txtLabel.Text  = inst.Label;
        _txtSerial.Text = inst.Serial ?? "";

        _cboModel.SelectedIndex = 0;
        for (int i = 0; i < _models.Count; i++)
        {
            if (_models[i].Id == inst.ModelId)
            {
                _cboModel.SelectedIndex = i + 1;
                break;
            }
        }

        _cboFormat.SelectedIndex = 0;
        if (!string.IsNullOrEmpty(inst.Format))
        {
            for (int i = 0; i < _formats.Count; i++)
            {
                if (_formats[i].Id == inst.Format)
                {
                    _cboFormat.SelectedIndex = i + 1;
                    break;
                }
            }
        }

        _cboState.SelectedIndex = (inst.State == "Retired") ? 1 : 0;

        var phys = MagnetronPhysics.FromJson(inst.PhysicsJson);
        _numPeakRadius.Value = (decimal)Math.Clamp(phys.Racetrack.PeakRadiusMm, 0, 200);
        _numWidth.Value      = (decimal)Math.Clamp(phys.Racetrack.WidthMm, 0, 100);
        _cboShape.SelectedItem = phys.Racetrack.Shape == "uniform" ? "uniform" : "gaussian";

        _lblHealth.Text = $"Cumulative: {phys.Health.CumulativeKWh:0.#} kWh · " +
                          $"{phys.Health.HotHours:0.#} h · {phys.Health.PowerCycles} cycles";
        var st = AssetHealth.ServiceStatus(phys.Health);
        _lblService.Text = $"Service: {st}  (limits {phys.Health.ServiceLimits.KWh:0} kWh" +
                           $" / {phys.Health.ServiceLimits.HotHours:0} h)";
        _lblService.ForeColor = st == AssetStatus.Ok ? ColorText : Color.FromArgb(229, 192, 123);

        _txtComment.Text = inst.Comment ?? "";

        _suppressDetail = false;
    }

    private void SaveDetailToInstance()
    {
        if (_suppressDetail) return;
        if (_selectedIdx < 0 || _selectedIdx >= Instances.Count) return;

        var old = Instances[_selectedIdx];

        string modelId = (_cboModel.SelectedIndex > 0)
            ? _models[_cboModel.SelectedIndex - 1].Id
            : "";

        string? formatId = (_cboFormat.SelectedIndex > 0)
            ? _formats[_cboFormat.SelectedIndex - 1].Id
            : null;

        string state = (_cboState.SelectedIndex == 1) ? "Retired" : "Active";

        // Re-parse PhysicsJson to preserve Health, then overwrite only the racetrack fields.
        var phys = MagnetronPhysics.FromJson(old.PhysicsJson);
        phys.Racetrack.PeakRadiusMm = (double)_numPeakRadius.Value;
        phys.Racetrack.WidthMm      = (double)_numWidth.Value;
        phys.Racetrack.Shape        = (_cboShape.SelectedItem as string) ?? "gaussian";
        string physicsJson = phys.ToJson();

        var updated = old with
        {
            Label       = _txtLabel.Text,
            Serial      = string.IsNullOrWhiteSpace(_txtSerial.Text) ? null : _txtSerial.Text.Trim(),
            ModelId     = modelId,
            Format      = formatId,
            State       = state,
            PhysicsJson = physicsJson,
            Comment     = string.IsNullOrWhiteSpace(_txtComment.Text) ? null : _txtComment.Text
        };
        Instances[_selectedIdx] = updated;

        // Sync the label in the listbox. Reassigning a ListBox item deletes+reinserts
        // it in the native control, which clears the selection and fires
        // SelectedIndexChanged → SelectInstance(-1) → the detail panel gets disabled
        // mid-keystroke. Guard that handler and restore the selection so editing the
        // Label doesn't strand the user after the first character.
        if (_list.Items[_selectedIdx] as string != _txtLabel.Text)
        {
            _syncingList = true;
            int sel = _selectedIdx;
            _list.Items[sel]    = _txtLabel.Text;
            _list.SelectedIndex = sel;
            _syncingList = false;
            _lblDetailTitle.Text = _txtLabel.Text;
        }
    }

    private void BtnAdd_Click(object? sender, EventArgs e)
    {
        SaveDetailToInstance();

        var newInst = new MagnetronInstance(
            Id:          Guid.NewGuid().ToString(),
            Label:       "New Magnetron",
            Serial:      null,
            ModelId:     _models.Count > 0 ? _models[0].Id : "",
            Format:      null,
            PhysicsJson: null,
            Comment:     null,
            State:       "Active");

        Instances.Add(newInst);
        _list.Items.Add(newInst.Label);

        _list.SelectedIndex = Instances.Count - 1;
    }

    private void BtnRemove_Click(object? sender, EventArgs e)
    {
        int idx = _list.SelectedIndex;
        if (idx < 0) return;

        Instances.RemoveAt(idx);
        _list.Items.RemoveAt(idx);

        _selectedIdx = -1;
        if (Instances.Count > 0)
            _list.SelectedIndex = Math.Min(idx, Instances.Count - 1);
        else
            SelectInstance(-1);
    }

    private void BtnOK_Click(object? sender, EventArgs e)
    {
        SaveDetailToInstance();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _hints?.Dispose();
        base.Dispose(disposing);
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
