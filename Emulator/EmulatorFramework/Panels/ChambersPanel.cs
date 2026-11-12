using APS.Controls.Theme;
using APS.SystemEmulator.Engine;
using EmulatorCore.Interfaces;

namespace APS.SystemEmulator.Panels;

internal sealed class ChambersPanel : UserControl
{
    private static readonly Color StatusGreen  = Color.FromArgb(0, 255, 136);
    private static readonly Color StatusOrange = Color.FromArgb(255, 170, 51);
    private static readonly Color StatusRed    = Color.FromArgb(255, 68, 102);
    private static readonly Color StatusGray   = Color.FromArgb(119, 136, 153);

    private readonly GroupBox _group;
    private readonly TableLayoutPanel _grid;
    private readonly ToolTip _tip = new();

    private SimulationEngine? _engine;
    private readonly List<ChamberRow> _rows = new();

    public int RowCount => _rows.Count;
    public IReadOnlyList<string> RowNames => _rows.Select(r => r.Id).ToList();
    public bool HasContent => _rows.Count > 0;

    public ChambersPanel()
    {
        Dock = DockStyle.Fill;
        _grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
        };
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));

        _group = new GroupBox
        {
            Text = "Chambers",
            Dock = DockStyle.Fill,
            Padding = new Padding(4),
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
        };
        _group.Controls.Add(_grid);
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
        var state = _engine.State;
        foreach (var row in _rows)
        {
            if (!state.Chambers.TryGetValue(row.Id, out var chamber)) continue;
            row.Pressure.Text = FormatPressure(chamber.Pressure);
            if (row.Throttle != null)
            {
                row.Throttle.Text = chamber.ThrottleSetpointTorr > 0f && chamber.ThrottleSetpointTorr < 9999f
                    ? $"Throttle {chamber.ThrottlePosition:F0}% → {FormatPressure(chamber.ThrottleSetpointTorr)}"
                    : $"Throttle {chamber.ThrottlePosition:F0}%";
            }
            if (row.Temperature != null)
                row.Temperature.Text = $"Temp {state.Thermal.Temperature:F1} C";
            if (row.RobotLabel != null && row.Robot != null)
            {
                var (text, color) = FormatRobot(row.Robot);
                row.RobotLabel.Text = text;
                row.RobotLabel.ForeColor = color;
                _tip.SetToolTip(row.RobotLabel,
                    $"Phase: {row.Robot.GetPhase()}  ·  Last cmd: {row.Robot.LastCmdId}  ·  Error: {row.Robot.LastErrorCode}");
            }
        }
    }

    private void Rebuild()
    {
        // Dispose existing controls to release GDI Font handles
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

        // Iterate the chamber dict (insertion order preserved).
        foreach (var (id, chamber) in _engine.State.Chambers)
        {
            bool showThrottle    = chamber.Type == ChamberType.Process;
            bool showTemperature = chamber.Type == ChamberType.Process;

            int rowIdx = _grid.RowCount;
            _grid.RowCount = rowIdx + 1;
            _grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

            var nameLbl     = MakeLabel(id, bold: true);
            var pressureLbl = MakeLabel("---", bold: false);
            var throttleLbl = showThrottle    ? MakeLabel("---", bold: false) : null;
            var tempLbl     = showTemperature ? MakeLabel("---", bold: false) : null;

            _grid.Controls.Add(nameLbl,     0, rowIdx);
            _grid.Controls.Add(pressureLbl, 1, rowIdx);
            if (throttleLbl != null) _grid.Controls.Add(throttleLbl, 2, rowIdx);
            if (tempLbl     != null) _grid.Controls.Add(tempLbl,     3, rowIdx);

            // A transfer robot living in this chamber renders its status in the row's
            // unused throttle/temperature columns (transfer modules have neither).
            IRobotStatus? robot = ResolveRobotFor(id);
            Label? robotLbl = null;
            if (robot != null && throttleLbl == null && tempLbl == null)
            {
                robotLbl = MakeLabel("---", bold: false);
                _grid.Controls.Add(robotLbl, 2, rowIdx);
                _grid.SetColumnSpan(robotLbl, 2);
            }

            _rows.Add(new ChamberRow(id, nameLbl, pressureLbl, throttleLbl, tempLbl, robot, robotLbl));
        }

        UpdateValues();
    }

    private IRobotStatus? ResolveRobotFor(string chamberId)
    {
        if (_engine == null) return null;
        var chamberTypes = _engine.State.Chambers.ToDictionary(
            kv => kv.Key, kv => kv.Value.Type, StringComparer.OrdinalIgnoreCase);
        foreach (var robot in _engine.Devices.OfType<IRobotStatus>())
        {
            var home = RobotChamberResolver.Resolve(
                robot.SlitValveIds, _engine.State.SlitConnections, chamberTypes);
            if (string.Equals(home, chamberId, StringComparison.OrdinalIgnoreCase))
                return robot;
        }
        return null;
    }

    /// <summary>Test seam: the robot-status text shown in a chamber row, or null if none.</summary>
    internal string? RobotStatusText(string chamberId) =>
        _rows.FirstOrDefault(r => string.Equals(r.Id, chamberId, StringComparison.OrdinalIgnoreCase))
             ?.RobotLabel?.Text;

    private static (string text, Color color) FormatRobot(IRobotStatus r)
    {
        string wafer = r.HasWafer ? "  [wafer]" : "";
        return r.GetState() switch
        {
            RobotState.Idle         => ($"◈ {r.Id}  Idle · {r.FaceName(r.GetCurrentFace())}{wafer}", StatusGreen),
            RobotState.Transferring => ($"◈ {r.Id}  Transferring  {r.FaceName(r.TransferSrc)}→{r.FaceName(r.TransferDst)}{wafer}", StatusOrange),
            RobotState.Homing       => ($"◈ {r.Id}  Homing…{wafer}", StatusOrange),
            RobotState.Error        => ($"◈ {r.Id}  Error: {r.LastErrorCode}{wafer}", StatusRed),
            _                       => ($"◈ {r.Id}  Not homed{wafer}", StatusGray),
        };
    }

    private static string FormatPressure(float torr)
    {
        if (torr >= 1f)    return $"{torr:F1} Torr";
        if (torr >= 1e-3f) return $"{torr * 1000f:F2} mTorr";
        return $"{torr:E2} Torr";
    }

    private static Label MakeLabel(string text, bool bold) => new()
    {
        Text = text,
        AutoSize = false,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        Font = new Font(bold ? "Segoe UI" : "Consolas", bold ? 9f : 10f, bold ? FontStyle.Bold : FontStyle.Regular),
    };

    private sealed record ChamberRow(
        string Id, Label NameLabel, Label Pressure, Label? Throttle, Label? Temperature,
        IRobotStatus? Robot, Label? RobotLabel);
}
