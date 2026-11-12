using APS.Controls.Theme;
using APS.SystemEmulator.Setup;
using EmulatorCore.Logging;
using EmulatorCore.Models;

namespace APS.SystemEmulator;

public sealed class FrmPrepareSystem : Form
{
    private readonly EmulatorConfig _config;
    private readonly DataGridView _grid;
    private readonly TextBox _log;
    private readonly Button _btnRefresh;
    private readonly Button _btnFixAll;
    private readonly Button _btnClose;
    private SystemCheck.Summary? _summary;

    public FrmPrepareSystem(EmulatorConfig config)
    {
        _config = config;
        Text = "Prepare System";
        Size = new(820, 620);
        MinimumSize = new(700, 500);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;

        var title = new Label
        {
            Text = "System Prerequisites",
            Font = new("Segoe UI", 12, System.Drawing.FontStyle.Bold),
            Dock = DockStyle.Top,
            Height = 28,
            Padding = new(8, 4, 0, 0),
        };
        var subtitle = new Label
        {
            Text = "Green = OK. Red = needs action. Click Fix to repair one row, or Fix All to apply everything in a single elevation prompt.",
            Dock = DockStyle.Top,
            Height = 22,
            Padding = new(8, 0, 0, 0),
        };

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            ReadOnly = false,
            MultiSelect = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
        };
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Status", HeaderText = "", Width = 32, ReadOnly = true,
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "CheckName", HeaderText = "Check", Width = 220, ReadOnly = true,
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Detail", HeaderText = "Detail", ReadOnly = true,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
        });
        _grid.Columns.Add(new DataGridViewButtonColumn
        {
            Name = "Fix", HeaderText = "", Text = "Fix", UseColumnTextForButtonValue = true, Width = 70,
        });
        _grid.CellContentClick += Grid_CellContentClick;

        _log = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Bottom, Height = 160,
            Font = new("Consolas", 9),
        };

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.LeftToRight,
            Height = 40,
            Padding = new(8, 6, 8, 6),
        };
        _btnRefresh = new Button { Text = "Refresh All", AutoSize = true };
        _btnFixAll = new Button { Text = "Fix All", AutoSize = true };
        _btnClose = new Button { Text = "Close", AutoSize = true };
        _btnRefresh.Click += async (_, _) => await RefreshAsync();
        _btnFixAll.Click += async (_, _) => await FixAllAsync();
        _btnClose.Click += (_, _) => Close();
        buttonPanel.Controls.AddRange([_btnRefresh, _btnFixAll, _btnClose]);

        Controls.Add(new DarkScrollHost(_grid) { Dock = DockStyle.Fill });
        Controls.Add(buttonPanel);
        Controls.Add(new DarkScrollHost(_log) { Dock = DockStyle.Bottom, Height = 160 });
        Controls.Add(subtitle);
        Controls.Add(title);

        DarkModeHelper.FixControls(this);

        Shown += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        SetBusy(true);
        AppendLog("Running checks...");
        try
        {
            _summary = await Task.Run(() => SystemCheck.RunAll(_config));
            RenderRows(_summary);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void RenderRows(SystemCheck.Summary s)
    {
        _grid.Rows.Clear();
        foreach (var r in s.Rows)
        {
            int i = _grid.Rows.Add(
                StatusIcon(r.Status), r.Name, r.Detail,
                r.Fixable ? "Fix" : "");
            var fixCell = (DataGridViewButtonCell)_grid.Rows[i].Cells["Fix"];
            fixCell.ReadOnly = !r.Fixable;
            _grid.Rows[i].Cells["Status"].Style.ForeColor =
                r.Status == CheckStatus.Ok ? System.Drawing.Color.LimeGreen :
                r.Status == CheckStatus.Missing ? System.Drawing.Color.Tomato :
                System.Drawing.Color.Goldenrod;
        }

        _btnFixAll.Enabled = s.Fixable.Count > 0;
    }

    private static string StatusIcon(CheckStatus s) => s switch
    {
        CheckStatus.Ok => "✓",
        CheckStatus.Missing => "✗",
        _ => "?",
    };

    private async void Grid_CellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (_grid.Columns[e.ColumnIndex].Name != "Fix") return;
        if (_summary == null) return;

        var row = _summary.Rows[e.RowIndex];
        if (!row.Fixable || row.FixCommands.Count == 0) return;

        AppendLog($"--- Fixing: {row.Name} ---");
        SetBusy(true);
        try
        {
            string output = await Task.Run(() => SystemCheck.RunFixBatch(row.FixCommands));
            AppendLog(output);
            await RefreshAsync();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task FixAllAsync()
    {
        if (_summary == null) return;
        var allCommands = _summary.Fixable.SelectMany(r => r.FixCommands).ToList();
        if (allCommands.Count == 0) return;

        AppendLog($"--- Fix All: {allCommands.Count} command(s) ---");
        SetBusy(true);
        try
        {
            string output = await Task.Run(() => SystemCheck.RunFixBatch(allCommands));
            AppendLog(output);
            await RefreshAsync();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _btnRefresh.Enabled = !busy;
        _btnFixAll.Enabled = !busy && (_summary?.Fixable.Count ?? 0) > 0;
        _grid.Enabled = !busy;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
    }

    private void AppendLog(string text)
    {
        if (InvokeRequired) { Invoke(() => AppendLog(text)); return; }
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
        EmulatorLog.Log(LogLevel.Info, "PrepareSystem", text);
    }
}
