using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using APS.Controls.Theme;
using APS.SystemControl.BaseHWClasses;

namespace APS.SystemControl;

/// <summary>
/// Extended-options popup for a linear PLC stage — the app-side counterpart of
/// the machine's HMI stage screen. Shows the live position and lets the
/// operator issue an arbitrary Move, Jog ±(step), Home and Stop, and edit the
/// persisted presets / soft limits / jog step. Motion is issued through the
/// supplied callbacks (which route to the "Stage"/"StageStop" worker commands);
/// config edits mutate the live <see cref="BaseStage"/> and persist via its
/// <see cref="BaseStage.ConfigChanged"/> event on Save.
/// </summary>
public sealed class frmStageSettings : Form
{
    private readonly BaseStage _stage;
    private readonly Action<float> _moveTo;
    private readonly Action _stop;
    private readonly System.Windows.Forms.Timer _poll;

    private Label _lblLive = null!;
    private DarkNumericUpDown _target = null!;
    private DarkNumericUpDown _jogStep = null!;
    private DataGridView _grid = null!;
    private CheckBox _chkMin = null!, _chkMax = null!;
    private DarkNumericUpDown _softMin = null!, _softMax = null!, _home = null!, _speed = null!;

    private const decimal PosMin = -100000m, PosMax = 100000m;

    public frmStageSettings(BaseStage stage, Action<float> moveTo, Action stop)
    {
        _stage = stage;
        _moveTo = moveTo;
        _stop = stop;

        Text = $"Stage {stage.Params.ID} — {stage.Params.Description}";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false; MaximizeBox = false;
        ClientSize = new Size(DpiScale.Px(420), DpiScale.Px(360));

        var tabs = new DarkTabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildControlTab());
        tabs.TabPages.Add(BuildPresetsTab());
        tabs.TabPages.Add(BuildSettingsTab());
        Controls.Add(tabs);
        Controls.Add(BuildButtonBar());

        _poll = new System.Windows.Forms.Timer { Interval = 200 };
        _poll.Tick += (_, _) => _lblLive.Text = $"{_stage.LastPosition:F1} mm";
    }

    private TabPage BuildControlTab()
    {
        var tab = new TabPage("Control");
        var t = NewTable(2);

        _lblLive = new Label
        {
            Text = $"{_stage.LastPosition:F1} mm",
            Font = new Font("Consolas", DpiScale.Px(20), FontStyle.Bold),
            ForeColor = VacuumTheme.LcdForeground,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, DpiScale.Px(8)),
        };
        AddSpanning(t, LabelFor("Position"));
        AddSpanning(t, _lblLive);

        _target = AddNumeric(t, "Target (mm)", (decimal)_stage.LastPosition, 1);
        var btnGo = MakeButton("Go", VacuumTheme.GlowBlue);
        btnGo.Click += (_, _) => MoveValidated((float)_target.Value);
        AddControlRow(t, "", btnGo);

        _jogStep = AddNumeric(t, "Jog step (mm)",
            (decimal)(_stage.JogStep <= 0 ? 1f : _stage.JogStep), 2, min: 0m);
        var jogPanel = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0) };
        var btnJogDown = MakeButton("− Jog", VacuumTheme.GlowBlue);
        var btnJogUp = MakeButton("+ Jog", VacuumTheme.GlowBlue);
        btnJogDown.Click += (_, _) => MoveValidated(_stage.LastPosition - (float)_jogStep.Value);
        btnJogUp.Click += (_, _) => MoveValidated(_stage.LastPosition + (float)_jogStep.Value);
        jogPanel.Controls.Add(btnJogDown);
        jogPanel.Controls.Add(btnJogUp);
        AddControlRow(t, "Jog", jogPanel);

        var btnHome = MakeButton("⌂ Seek Home", VacuumTheme.GlowBlue);
        btnHome.Click += (_, _) => MoveValidated(_stage.HomePosition);
        var btnStop = MakeButton("STOP", VacuumTheme.GlowRed);
        btnStop.Click += (_, _) => _stop();
        var actPanel = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0) };
        actPanel.Controls.Add(btnHome);
        actPanel.Controls.Add(btnStop);
        AddControlRow(t, "", actPanel);

        tab.Controls.Add(t);
        return tab;
    }

    private void MoveValidated(float target)
    {
        float min = _chkMin.Checked ? (float)_softMin.Value : float.NaN;
        float max = _chkMax.Checked ? (float)_softMax.Value : float.NaN;
        if (!float.IsNaN(min) && target < min)
        {
            MessageBox.Show(this, $"Target {target:F1} mm is below the soft limit {min:F1} mm.",
                "Soft limit", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!float.IsNaN(max) && target > max)
        {
            MessageBox.Show(this, $"Target {target:F1} mm is above the soft limit {max:F1} mm.",
                "Soft limit", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _moveTo(target);
    }

    private TabPage BuildPresetsTab()
    {
        var tab = new TabPage("Presets");

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = true,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            BackgroundColor = VacuumTheme.PanelBackground,
            BorderStyle = BorderStyle.None,
            EnableHeadersVisualStyles = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        };
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Name", FillWeight = 50 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Position", HeaderText = "Position (mm)", FillWeight = 35 });
        _grid.Columns.Add(new DataGridViewButtonColumn { Name = "Go", HeaderText = "", Text = "Go", UseColumnTextForButtonValue = true, FillWeight = 15 });
        StyleGrid(_grid);

        foreach (var p in _stage.Presets)
            _grid.Rows.Add(p.Name, p.Position.ToString("0.###", CultureInfo.InvariantCulture), "Go");

        _grid.CellContentClick += (s, e) =>
        {
            if (e.RowIndex < 0 || _grid.Columns[e.ColumnIndex].Name != "Go") return;
            if (TryReadRow(e.RowIndex, out _, out float pos))
                MoveValidated(pos);
        };

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = DpiScale.Px(36), FlowDirection = FlowDirection.LeftToRight };
        var btnRemove = MakeButton("Remove", VacuumTheme.GlowAmber);
        btnRemove.Click += (_, _) =>
        {
            if (_grid.CurrentRow is { IsNewRow: false } r) _grid.Rows.Remove(r);
        };
        bar.Controls.Add(btnRemove);

        tab.Controls.Add(_grid);
        tab.Controls.Add(bar);
        return tab;
    }

    private bool TryReadRow(int row, out string name, out float pos)
    {
        name = _grid.Rows[row].Cells["Name"].Value?.ToString()?.Trim() ?? "";
        pos = 0f;
        string? posStr = _grid.Rows[row].Cells["Position"].Value?.ToString();
        return !string.IsNullOrWhiteSpace(name)
            && float.TryParse(posStr, NumberStyles.Float, CultureInfo.InvariantCulture, out pos);
    }

    private TabPage BuildSettingsTab()
    {
        var tab = new TabPage("Settings");
        var t = NewTable(2);

        _home = AddNumeric(t, "Home position (mm)", (decimal)_stage.HomePosition, 1);

        _chkMin = new CheckBox { Text = "Enforce min", Checked = !float.IsNaN(_stage.SoftMin), AutoSize = true, Anchor = AnchorStyles.Left };
        _softMin = MakeNumeric(float.IsNaN(_stage.SoftMin) ? 0m : (decimal)_stage.SoftMin, 1, PosMin, PosMax);
        _softMin.Enabled = _chkMin.Checked;
        _chkMin.CheckedChanged += (_, _) => _softMin.Enabled = _chkMin.Checked;
        AddControlRow(t, _chkMin, _softMin);

        _chkMax = new CheckBox { Text = "Enforce max", Checked = !float.IsNaN(_stage.SoftMax), AutoSize = true, Anchor = AnchorStyles.Left };
        _softMax = MakeNumeric(float.IsNaN(_stage.SoftMax) ? 0m : (decimal)_stage.SoftMax, 1, PosMin, PosMax);
        _softMax.Enabled = _chkMax.Checked;
        _chkMax.CheckedChanged += (_, _) => _softMax.Enabled = _chkMax.Checked;
        AddControlRow(t, _chkMax, _softMax);

        _speed = AddNumeric(t, "Speed (0 = unset)", (decimal)_stage.Speed, 1, min: 0m);

        AddSpanning(t, new Label
        {
            Text = "Speed is applied only if the driver exposes a speed register.",
            ForeColor = VacuumTheme.TextDim, AutoSize = true, Margin = new Padding(0, DpiScale.Px(6), 0, 0),
        });

        tab.Controls.Add(t);
        return tab;
    }

    private Panel BuildButtonBar()
    {
        var pnl = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            Height = DpiScale.Px(44),
            Padding = new Padding(DpiScale.Px(8)),
        };
        var btnClose = new Button { Text = "Close", Size = new Size(DpiScale.Px(90), DpiScale.Px(28)), DialogResult = DialogResult.Cancel };
        var btnSave = new Button { Text = "Save", Size = new Size(DpiScale.Px(90), DpiScale.Px(28)) };
        btnSave.Click += (_, _) => SaveConfig();
        pnl.Controls.Add(btnClose);
        pnl.Controls.Add(btnSave);
        CancelButton = btnClose;
        return pnl;
    }

    private void SaveConfig()
    {
        var presets = new List<StagePreset>();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.IsNewRow) continue;
            if (TryReadRow(row.Index, out string name, out float pos))
                presets.Add(new StagePreset(name, pos));
        }

        _stage.HomePosition = (float)_home.Value;
        _stage.JogStep = (float)_jogStep.Value;
        _stage.SoftMin = _chkMin.Checked ? (float)_softMin.Value : float.NaN;
        _stage.SoftMax = _chkMax.Checked ? (float)_softMax.Value : float.NaN;
        _stage.Speed = (float)_speed.Value;
        _stage.SetPresets(presets, persist: false);
        _stage.RaiseConfigChanged();   // single persist for all edits
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        DarkModeHelper.FixControls(this);
        StyleGrid(_grid);   // re-assert dark grid colors after FixControls
        _poll.Start();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _poll.Stop();
        _poll.Dispose();
        base.OnFormClosed(e);
    }

    private static TableLayoutPanel NewTable(int cols) => new()
    {
        ColumnCount = cols,
        Dock = DockStyle.Fill,
        AutoSize = true,
        Padding = new Padding(DpiScale.Px(12)),
    };

    private static Label LabelFor(string text) => new()
    {
        Text = text + ":", Anchor = AnchorStyles.Left, AutoSize = true, Margin = new Padding(0, DpiScale.Px(4), DpiScale.Px(8), 0),
    };

    private static void AddControlRow(TableLayoutPanel t, string label, Control c)
        => AddControlRow(t, LabelFor(label), c);

    private static void AddControlRow(TableLayoutPanel t, Control label, Control c)
    {
        int row = t.RowCount;
        t.Controls.Add(label, 0, row);
        t.Controls.Add(c, 1, row);
        t.RowCount++;
    }

    private static void AddSpanning(TableLayoutPanel t, Control c)
    {
        int row = t.RowCount;
        t.Controls.Add(c, 0, row);
        t.SetColumnSpan(c, 2);
        t.RowCount++;
    }

    private DarkNumericUpDown AddNumeric(TableLayoutPanel t, string label, decimal value, int decimals, decimal? min = null)
    {
        var nud = MakeNumeric(value, decimals, min ?? PosMin, PosMax);
        AddControlRow(t, label, nud);
        return nud;
    }

    private static DarkNumericUpDown MakeNumeric(decimal value, int decimals, decimal min, decimal max) => new()
    {
        Minimum = min,
        Maximum = max,
        DecimalPlaces = decimals,
        Increment = 1m,
        Value = Math.Clamp(value, min, max),
        Anchor = AnchorStyles.Left,
        Width = DpiScale.Px(100),
    };

    private static Button MakeButton(string text, Color accent)
    {
        var b = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            AutoSize = true,
            MinimumSize = new Size(DpiScale.Px(74), DpiScale.Px(28)),
            ForeColor = accent,
            Margin = new Padding(DpiScale.Px(2)),
        };
        b.FlatAppearance.BorderColor = accent;
        b.FlatAppearance.BorderSize = 1;
        return b;
    }

    private static void StyleGrid(DataGridView g)
    {
        g.BackgroundColor = VacuumTheme.PanelBackground;
        g.GridColor = VacuumTheme.PanelBorder;
        g.DefaultCellStyle.BackColor = VacuumTheme.PanelField;
        g.DefaultCellStyle.ForeColor = VacuumTheme.TextPrimary;
        g.DefaultCellStyle.SelectionBackColor = VacuumTheme.GlowBlue;
        g.DefaultCellStyle.SelectionForeColor = Color.White;
        g.ColumnHeadersDefaultCellStyle.BackColor = VacuumTheme.MetalDark;
        g.ColumnHeadersDefaultCellStyle.ForeColor = VacuumTheme.TextPrimary;
        g.ColumnHeadersDefaultCellStyle.SelectionBackColor = VacuumTheme.MetalDark;
    }
}
