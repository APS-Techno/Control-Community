using APS.Controls.Theme;
using APS.SystemEmulator.Engine;
using APS.Emulator.Devices.S7;
using EmulatorCore.Interfaces;

namespace APS.SystemEmulator.Panels;

internal sealed class ValvesPanel : UserControl
{
    private static readonly Color StatusGreen = Color.FromArgb(0, 255, 136);
    private static readonly Color StatusGray  = Color.FromArgb(119, 136, 153);

    private readonly GroupBox _group;
    private readonly FlowLayoutPanel _flow;
    private readonly ToolTip _tip = new();

    private SimulationEngine? _engine;
    private readonly Dictionary<string, ValveChip> _chips = new();

    public int ChipCount => _chips.Count;
    public bool HasContent => _chips.Count > 0;

    public ValvesPanel()
    {
        Dock = DockStyle.Fill;
        _flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
        };

        _group = new GroupBox
        {
            Text = "Valves",
            Dock = DockStyle.Fill,
            Padding = new Padding(DpiScale.Px(4)),
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
        };
        _group.Controls.Add(new DarkScrollHost(_flow) { Dock = DockStyle.Fill });
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
        foreach (var chip in _chips.Values)
            chip.Label.BackColor = chip.IsOpen() ? StatusGreen : StatusGray;
    }

    internal void SimulateChipClick(string deviceId)
    {
        if (_chips.TryGetValue(deviceId, out var chip)) chip.InvokeClick();
    }

    private void Rebuild()
    {
        // Dispose existing controls to release GDI Font handles
        while (_flow.Controls.Count > 0)
        {
            var c = _flow.Controls[0];
            _flow.Controls.RemoveAt(0);
            c.Dispose();
        }
        _chips.Clear();
        if (_engine == null) return;

        foreach (var valve in _engine.Devices.OfType<ValveEmulator>())
            AddChip(valve.Id, () => valve.IsOpen, () => valve.SetState(!valve.IsOpen), $"Click to toggle {valve.Id}");

        foreach (var input in _engine.Devices.OfType<InputValveEmulator>())
            AddChip(input.Id, () => input.IsOpen, () => input.SetState(!input.IsOpen), $"Click to toggle {input.Id} (gate valve sensor)");

        UpdateValues();
    }

    private void AddChip(string id, Func<bool> isOpen, Action toggle, string tooltip)
    {
        var lbl = new Label
        {
            Text = id,
            AutoSize = false,
            Size = new Size(DpiScale.Px(60), DpiScale.Px(28)),
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = StatusGray,
            ForeColor = Color.White,
            Margin = new Padding(DpiScale.Px(2)),
            Font = new Font(Font.FontFamily, 8f, FontStyle.Bold),
            Cursor = Cursors.Hand,
        };

        var chip = new ValveChip(lbl, isOpen, toggle, IsReplayMode);
        lbl.Click += (_, _) =>
        {
            if (IsReplayMode()) return;
            toggle();
            lbl.BackColor = isOpen() ? StatusGreen : StatusGray;
        };
        _tip.SetToolTip(lbl, tooltip);

        _flow.Controls.Add(lbl);
        _chips[id] = chip;
    }

    private bool IsReplayMode() =>
        _engine?.State.DataSource.Mode == DataSourceMode.LogReplay;

    private sealed class ValveChip
    {
        public Label Label { get; }
        private readonly Func<bool> _isOpen;
        private readonly Action _toggle;
        private readonly Func<bool> _isReplayMode;

        public ValveChip(Label label, Func<bool> isOpen, Action toggle, Func<bool> isReplayMode)
        {
            Label = label; _isOpen = isOpen; _toggle = toggle; _isReplayMode = isReplayMode;
        }

        public bool IsOpen() => _isOpen();

        public void InvokeClick()
        {
            if (_isReplayMode()) return;
            _toggle();
            Label.BackColor = _isOpen() ? Color.FromArgb(0, 255, 136) : Color.FromArgb(119, 136, 153);
        }
    }
}
