using APS.Controls.Theme;
using APS.SystemEmulator.Engine;
using APS.Emulator.Devices.S7;
using EmulatorCore.Interfaces;

namespace APS.SystemEmulator.Panels;

internal sealed class PumpsPanel : UserControl
{
    private static readonly Color StatusGreen = Color.FromArgb(0, 255, 136);
    private static readonly Color StatusGray  = Color.FromArgb(119, 136, 153);

    private readonly GroupBox _group;
    private readonly TableLayoutPanel _grid;
    private readonly ToolTip _tip = new();

    private SimulationEngine? _engine;
    private readonly Dictionary<string, PumpRow> _rows = new();

    public int RowCount => _rows.Count;
    public bool HasContent => _rows.Count > 0;

    public PumpsPanel()
    {
        Dock = DockStyle.Fill;
        _grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            AutoScroll = true,
        };
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));   // name
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));   // kind
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));    // state
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));    // detail

        _group = new GroupBox
        {
            Text = "Pumps",
            Dock = DockStyle.Fill,
            Padding = new Padding(4),
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
        };
        _group.Controls.Add(new DarkScrollHost(_grid) { Dock = DockStyle.Fill });
        Controls.Add(_group);

        DarkModeHelper.FixControls(this);
    }

    public void AttachEngine(SimulationEngine engine)
    {
        _engine = engine;
        Rebuild();
    }

    public void UpdateValues()
    {
        if (_engine == null) return;
        foreach (var device in _engine.Devices)
        {
            if (!_rows.TryGetValue(device.Id, out var row)) continue;
            switch (device)
            {
                case TurboPumpEmulator t:
                    row.State.Text = t.IsAtSpeed ? "At Speed" : t.IsRunning ? "Spinning Up" : "Stopped";
                    row.State.ForeColor = t.IsRunning ? StatusGreen : StatusGray;
                    row.Detail.Text = t.IsRunning ? $"{(int)(t.SpeedFraction * 100)}%" : "";
                    break;
                case PumpEmulator p:
                    row.State.Text = p.IsRunning ? "Running" : "Stopped";
                    row.State.ForeColor = p.IsRunning ? StatusGreen : StatusGray;
                    row.Detail.Text = "";
                    break;
            }
        }
    }

    internal void SimulateRowClick(string deviceId)
    {
        if (_rows.TryGetValue(deviceId, out var row))
            row.InvokeClick();
    }

    private void Rebuild()
    {
        while (_grid.Controls.Count > 0)
        {
            var c = _grid.Controls[0];
            _grid.Controls.RemoveAt(0);
            c.Dispose();
        }
        _grid.RowStyles.Clear();
        _grid.RowCount = 0;
        _rows.Clear();

        if (_engine == null) return;

        foreach (var device in _engine.Devices)
        {
            PumpRow? row = device switch
            {
                TurboPumpEmulator t => BuildRow(t.Id, "Turbo", () =>
                {
                    if (IsReplayMode()) return;
                    t.Command(!t.IsRunning);
                }),
                PumpEmulator p => BuildRow(p.Id, "Scroll", () =>
                {
                    if (IsReplayMode()) return;
                    p.SetRunning(!p.IsRunning);
                }),
                _ => null,
            };
            if (row != null) _rows[device.Id] = row;
        }

        UpdateValues();
    }

    private PumpRow BuildRow(string id, string kind, Action onClick)
    {
        int rowIdx = _grid.RowCount;
        _grid.RowCount = rowIdx + 1;
        _grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

        var name   = MakeLabel(id,   bold: true);
        var kindLb = MakeLabel(kind, bold: false);
        var state  = MakeLabel("---", bold: false);
        var detail = MakeLabel("",   bold: false);

        foreach (var lbl in new[] { name, kindLb, state, detail })
        {
            lbl.Cursor = Cursors.Hand;
            lbl.Click += (_, _) => onClick();
        }
        _tip.SetToolTip(name, $"Click to toggle {id}");

        _grid.Controls.Add(name,   0, rowIdx);
        _grid.Controls.Add(kindLb, 1, rowIdx);
        _grid.Controls.Add(state,  2, rowIdx);
        _grid.Controls.Add(detail, 3, rowIdx);

        return new PumpRow(name, kindLb, state, detail, onClick);
    }

    private bool IsReplayMode() =>
        _engine?.State.DataSource.Mode == DataSourceMode.LogReplay;

    private static Label MakeLabel(string text, bool bold) => new()
    {
        Text = text,
        AutoSize = false,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        Font = new Font(bold ? "Segoe UI" : "Consolas", bold ? 9f : 10f, bold ? FontStyle.Bold : FontStyle.Regular),
    };

    private sealed class PumpRow
    {
        public Label Name { get; }
        public Label Kind { get; }
        public Label State { get; }
        public Label Detail { get; }
        private readonly Action _onClick;

        public PumpRow(Label name, Label kind, Label state, Label detail, Action onClick)
        {
            Name = name; Kind = kind; State = state; Detail = detail; _onClick = onClick;
        }

        public void InvokeClick() => _onClick();
    }
}
