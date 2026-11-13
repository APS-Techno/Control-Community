using APS.ProcessPanel.Contracts;
using APS.SystemControl.Controls.General;
using APS.SystemControl.Controls.Theme;
using APS.SystemControl.GUIControls.Process;
using Contracts = APS.ProcessPanel.Contracts;

namespace APS.Controls.Generic;

public class ChamberProcessPanel : BaseProcessPanel, IAnimatedSchematicPanel
{
    private const string HolderId = "Holder";
    private const string ShutterId = "Shutter";

    public float HolderFlangeSeconds { set => _schematic.HolderFlangeSeconds = value; }
    public float ShutterFlangeSeconds { set => _schematic.ShutterFlangeSeconds = value; }

    private readonly ChamberSchematicControl _schematic = new();
    private readonly Dictionary<string, ValveLedControl> _valveLeds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Label> _drivePills = new(StringComparer.OrdinalIgnoreCase);

    private float DpiF => DeviceDpi / 96f;

    protected override int LeftPanelWidth => (int)(320 * DpiF);
    protected override int RightPanelWidth => (int)(280 * DpiF);
    protected override int CenterPanelWidth => (int)(280 * DpiF);
    protected override int BottomPanelHeight => 0;

    public ChamberProcessPanel()
    {
        _schematic.Dock = DockStyle.Fill;
        SchematicPanel.Controls.Add(_schematic);
        _schematic.ShowDepositionTimes = true;
        _schematic.ActiveFlanges = ProcessPanelSpec.Current.Flanges;
    }

    protected override IEnumerable<ReadingSpec> DefineReadings() => ProcessPanelSpec.Current.Readings;

    public const string DrivesOverlayName = "DrivesOverlay";

    protected override void PopulateExtraSections(
        FlowLayoutPanel? leftStack,
        FlowLayoutPanel? rightStack)
    {
        var spec = ProcessPanelSpec.Current;

        if (leftStack != null && spec.ValveGroups.Count > 0)
        {
            AddExtraSectionHeader(leftStack, "Valves");
            foreach (var group in spec.ValveGroups)
                AddValveGroup(leftStack, group);
        }

        if (spec.Drives.Count == 0) return;

        float s = DpiF;
        var overlay = new FlowLayoutPanel
        {
            Name          = DrivesOverlayName,
            FlowDirection = FlowDirection.TopDown,
            WrapContents  = false,
            AutoSize      = true,
            AutoSizeMode  = AutoSizeMode.GrowAndShrink,
            BackColor     = Color.FromArgb(13, 27, 42),
            Location      = new Point((int)(12 * s), (int)(8 * s)),
            Anchor        = AnchorStyles.Top | AnchorStyles.Left,
        };
        AddExtraSectionHeader(overlay, "Drives");
        foreach (var drive in spec.Drives)
        {
            var pill = CreateDrivePillRow(drive.Label);
            pill.Name = drive.Id + "Pill";
            _drivePills[drive.Id] = pill;
            overlay.Controls.Add(pill.Parent!);
        }
        SchematicPanel.Controls.Add(overlay);
        SchematicPanel.Controls.SetChildIndex(overlay, 0);
    }

    private void AddValveGroup(FlowLayoutPanel stack, ValveGroupSpec group)
    {
        var sub = new Label
        {
            Text      = group.Header,
            Font      = ScaledFont.Create(8f),
            ForeColor = Color.FromArgb(119, 136, 153),
            AutoSize  = true,
            Margin    = new Padding((int)(4 * DpiF), (int)(6 * DpiF), 0, (int)(2 * DpiF)),
        };
        stack.Controls.Add(sub);

        var row = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents  = true,
            AutoSize      = true,
            AutoSizeMode  = AutoSizeMode.GrowAndShrink,
            Margin        = new Padding(4, 0, 4, 2),
        };

        foreach (var valve in group.Valves)
        {
            var led = new ValveLedControl(valve.Id, valve.Description, GasAccent(valve.Description));
            _valveLeds[valve.Id] = led;
            row.Controls.Add(led);
        }

        stack.Controls.Add(row);
    }

    private static Color? GasAccent(string descr)
    {
        if (string.IsNullOrWhiteSpace(descr)) return null;
        int space = descr.IndexOf(' ');
        return ReadingPalette.ForGas(space < 0 ? descr : descr[..space]);
    }

    private Label CreateDrivePillRow(string driveLabel)
    {
        float s = DpiF;
        int rowH = (int)(26 * s), labelH = (int)(22 * s), pillW = (int)(92 * s);
        var row = new Panel
        {
            Height = rowH,
            Width = (int)(220 * s),
            BackColor = Color.FromArgb(13, 27, 42),
            Margin = new Padding(0, 0, 0, 0),
        };

        var nameLabel = new Label
        {
            Text = driveLabel,
            Font = ScaledFont.Create(10f),
            ForeColor = Color.FromArgb(200, 214, 229),
            AutoSize = false,
            Height = labelH,
            Location = new Point((int)(6 * s), (int)(2 * s)),
            TextAlign = ContentAlignment.MiddleLeft,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Width = row.Width - (int)(6 * s) - pillW - (int)(6 * s),
        };
        row.Controls.Add(nameLabel);

        var pill = new Label
        {
            Text = "—",
            Font = ScaledFont.Create(9f, FontStyle.Bold),
            ForeColor = Color.FromArgb(119, 136, 153),
            BackColor = Color.FromArgb(34, 51, 76),
            AutoSize = false,
            Width = pillW,
            Height = labelH,
            Location = new Point(row.Width - pillW - (int)(6 * s), (int)(2 * s)),
            TextAlign = ContentAlignment.MiddleCenter,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        row.Controls.Add(pill);

        return pill;
    }

    protected override void OnDrivesChanged(Contracts.DriveInfo[] drives)
    {
        foreach (var d in drives)
        {
            if (d.Id.Equals(HolderId, StringComparison.OrdinalIgnoreCase))
                _schematic.SetHolderState(d.Position, d.IsMoving, d.IsHomed);
            else if (d.Id.Equals(ShutterId, StringComparison.OrdinalIgnoreCase))
                _schematic.SetShutterState(d.Position, d.IsMoving, d.IsHomed);

            if (_drivePills.TryGetValue(d.Id, out var pill))
                UpdatePill(pill, d);
        }
    }

    private static void UpdatePill(Label pill, Contracts.DriveInfo d)
    {
        Color bg, fg;
        string text;

        // UserRegister 51 = "arrived at position": the queue still reports moving
        // while it dwells, but the drive is stationary.
        bool physicallyMoving = d.IsMoving && d.UserRegister != 51;

        if (!d.IsHomed && !physicallyMoving)
        {
            text = "NOT HOMED";
            bg = Color.FromArgb(80, 30, 30);
            fg = Color.FromArgb(255, 100, 100);
        }
        else if (physicallyMoving && !d.IsHomed)
        {
            text = "HOMING";
            bg = Color.FromArgb(80, 55, 10);
            fg = Color.FromArgb(255, 165, 0);
        }
        else if (physicallyMoving)
        {
            text = "MOVING";
            bg = Color.FromArgb(80, 55, 10);
            fg = Color.FromArgb(255, 165, 0);
        }
        else if (d.FlangeNumber > 0)
        {
            text = $"FL {d.FlangeNumber}";
            bg = Color.FromArgb(10, 58, 74);
            fg = Color.FromArgb(0, 255, 255);
        }
        else
        {
            text = "BETWEEN";
            bg = Color.FromArgb(34, 51, 76);
            fg = Color.FromArgb(119, 136, 153);
        }

        pill.Text = text;
        pill.BackColor = bg;
        pill.ForeColor = fg;
    }

    protected override void OnValvesChanged(Contracts.ValveInfo[] valves)
    {
        foreach (var v in valves)
        {
            if (_valveLeds.TryGetValue(v.Id, out var led))
                led.IsOpen = v.IsOpen;
        }
    }

    protected override void OnSourcesChanged(
        IReadOnlyDictionary<int, Contracts.FlangeSourceInfo> flangeSources)
    {
        for (int f = 1; f <= _schematic.ActiveFlanges; f++)
        {
            if (flangeSources.TryGetValue(f, out var info))
            {
                _schematic.SetFlangeMaterial(f - 1, info.Effective);
                _schematic.SetFlangePowerSource(f - 1, info.PowerSupplyId, info.PowerType);
            }
            else
            {
                _schematic.SetFlangeMaterial(f - 1, null);
                _schematic.SetFlangePowerSource(f - 1, null, null);
            }
        }
    }

    protected override void OnStepChanged(ProcessStepInfo info)
    {
        if (info.Parameters.TryGetValue(ProcessParameterKeys.DepositFlange, out var flangeParam) &&
            info.Parameters.TryGetValue(ProcessParameterKeys.DepositSeconds, out var secondsParam))
        {
            int flange = (int)flangeParam.Value;
            double seconds = secondsParam.Value;
            if (flange >= 1 && flange <= _schematic.ActiveFlanges)
                _schematic.SetFlangeDepositionSeconds(flange - 1, seconds);
        }
    }
}
