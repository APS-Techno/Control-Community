using System.Globalization;
using APS.Controls.General;
using APS.Controls.Theme;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Controls.General;
using APS.SystemControl.Plugin;

namespace APS.Controls.Generic;

public class LanPsuPanel : ScalablePanel, IBaseCustomPanel, IHardwareAwarePanel
{
    private readonly TableLayoutPanel _table;
    private readonly LEDDisplay _lddVoltage = new();
    private readonly LEDDisplay _lddCurrent = new();
    private readonly LEDDisplay _lddPower = new();
    private readonly DarkNumericUpDown _nudPower = new();
    private readonly DarkNumericUpDown _nudFrequency = new();
    private readonly DarkNumericUpDown _nudDuty = new();
    private readonly List<Control> _pdcOnly = [];
    private readonly LedButton _btnOutput = new();
    private readonly PsuStatusLed _ledPwr = new() { Caption = "PWR", OnColor = Color.OrangeRed };
    private readonly PsuStatusLed _ledCom = new() { Caption = "COM", OnColor = Color.DeepSkyBlue };
    private BasePowerSupply? _supply;
    private Label? _currentUnitLabel;
    private bool _seeding;
    private bool _seeded;
    private readonly Color _liveInk;

    public string ID { get; set; } = "PSU";
    public DeviceType DeviceType => DeviceType.Power;

    public event EventHandler<CustomEventArgs>? SendCommandEvent;

    public LanPsuPanel()
    {
        _table = new TableLayoutPanel
        {
            Padding = new Padding((int)(2 * Dpi)),
            ColumnCount = 7,
            RowCount = 3,
            Dock = DockStyle.Fill,
            AutoSize = true,
        };

        InitializeComponents();
        ScaleControls();
        DarkModeHelper.FixControls(this);
        _liveInk = _lddPower.ForeColor;
    }

    internal DarkNumericUpDown PowerInput => _nudPower;
    internal DarkNumericUpDown FrequencyInput => _nudFrequency;
    internal DarkNumericUpDown DutyInput => _nudDuty;
    internal LedButton OutputButton => _btnOutput;
    internal LEDDisplay VoltageDisplay => _lddVoltage;
    internal LEDDisplay CurrentDisplay => _lddCurrent;
    internal LEDDisplay PowerDisplay => _lddPower;
    internal PsuStatusLed ComLed => _ledCom;

    protected override bool DrawStandardBorder => true;

    private void InitializeComponents()
    {
        Height = 96 + HeaderHeight;
        Width = 460;
        Padding = new Padding((int)(3 * Dpi), HeaderHeightPx + (int)(3 * Dpi), (int)(3 * Dpi), (int)(3 * Dpi));
        BackColor = Color.FromArgb(13, 27, 42);
        ForeColor = Color.FromArgb(200, 214, 229);

        _table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (int)(80 * Dpi)));
        _table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (int)(80 * Dpi)));
        _table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 3; i++)
            _table.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));

        AddReadout("Voltage", _lddVoltage, "0.0", "V", 0);
        _currentUnitLabel = AddReadout("Current", _lddCurrent, "0.00", "A", 1);
        AddReadout("Power", _lddPower, "0", "W", 2);

        ConfigureInput(_nudPower, 0, 1000, 5);
        _nudPower.ValueChanged += (_, _) => { if (!_seeding) OnSendCommand(["SetPower", (float)_nudPower.Value]); };
        AddSetpoint("Power", _nudPower, "W", 0, pdcOnly: false);

        ConfigureInput(_nudFrequency, 1, 350, 1);
        _nudFrequency.ValueChanged += (_, _) => { if (!_seeding) OnSendCommand(["SetFrequency", (int)_nudFrequency.Value]); };
        AddSetpoint("Freq", _nudFrequency, "kHz", 1, pdcOnly: true);

        ConfigureInput(_nudDuty, 5, 95, 1);
        _nudDuty.ValueChanged += (_, _) => { if (!_seeding) OnSendCommand(["SetDutyCycle", (int)_nudDuty.Value]); };
        AddSetpoint("Duty", _nudDuty, "%", 2, pdcOnly: true);

        _btnOutput.Text = "Out";
        _btnOutput.LedColor = Color.Lime;
        _btnOutput.Anchor = AnchorStyles.Right;
        _btnOutput.Size = new Size(80, 40);
        _btnOutput.Click += BtnOutput_Click;
        _table.Controls.Add(_btnOutput, 6, 0);
        _table.SetRowSpan(_btnOutput, 2);

        SetPdcInputsVisible(false);

        HeaderOverlay.Controls.Add(_ledCom);
        HeaderOverlay.Controls.Add(_ledPwr);
        _ledCom.Margin = _ledPwr.Margin = new Padding(2, 6, 2, 0);

        Controls.Add(_table);
    }

    private Label AddReadout(string caption, LEDDisplay display, string initial, string unit, int row)
    {
        _table.Controls.Add(MakeLabel(caption, ContentAlignment.MiddleRight), 0, row);
        display.Text = initial;
        display.Dock = DockStyle.Fill;
        _table.Controls.Add(display, 1, row);
        var unitLabel = MakeLabel(unit, ContentAlignment.MiddleLeft);
        _table.Controls.Add(unitLabel, 2, row);
        return unitLabel;
    }

    private void AddSetpoint(string caption, DarkNumericUpDown input, string unit, int row, bool pdcOnly)
    {
        var captionLabel = MakeLabel(caption, ContentAlignment.MiddleRight);
        var unitLabel = MakeLabel(unit, ContentAlignment.MiddleLeft);
        _table.Controls.Add(captionLabel, 3, row);
        _table.Controls.Add(input, 4, row);
        _table.Controls.Add(unitLabel, 5, row);
        if (pdcOnly)
            _pdcOnly.AddRange([captionLabel, input, unitLabel]);
    }

    private static Label MakeLabel(string text, ContentAlignment align) => new()
    {
        Text = text,
        TextAlign = align,
        Font = new Font("Arial", 9, FontStyle.Bold),
        Dock = DockStyle.Fill,
        AutoSize = true,
    };

    private static void ConfigureInput(DarkNumericUpDown input, decimal min, decimal max, decimal increment)
    {
        input.DecimalPlaces = 0;
        input.Minimum = min;
        input.Maximum = max;
        input.Increment = increment;
        input.Value = min;
        input.Size = new Size(72, 22);
        input.Anchor = AnchorStyles.Left;
    }

    private void SetPdcInputsVisible(bool visible)
    {
        foreach (var control in _pdcOnly)
            control.Visible = visible;
    }

    public void OnHardwareAvailable(IHardwareRegistry hardware)
    {
        _supply = hardware.GetDevice<BasePowerSupply>(ID);
        if (_supply is not null && _currentUnitLabel is not null)
            _currentUnitLabel.Text = _supply.CurrentUnit;
        SetPdcInputsVisible(_supply?.SetpointParameterNames.Contains("Frequency") == true);
        _seeded = SeedFromSupply();
    }

    private bool SeedFromSupply()
    {
        if (_supply is null)
            return false;
        bool all = Seed(_nudPower, "Power");
        if (_supply.SetpointParameterNames.Contains("Frequency"))
            all &= Seed(_nudFrequency, "Frequency") & Seed(_nudDuty, "DutyCycle");
        return all;
    }

    private bool Seed(DarkNumericUpDown input, string parameter)
    {
        if (!_supply!.TryGetSetpoint(parameter, out float value) || !float.IsFinite(value))
            return false;
        _seeding = true;
        try
        {
            input.Value = Math.Clamp((decimal)Math.Round(value), input.Minimum, input.Maximum);
        }
        finally
        {
            _seeding = false;
        }
        return true;
    }

    private void BtnOutput_Click(object? sender, EventArgs e)
    {
        _btnOutput.Selected = !_btnOutput.Selected;
        OnSendCommand([_btnOutput.Selected ? "OutputOn" : "OutputOff"]);
    }

    protected virtual void OnSendCommand(object[] data)
    {
        SendCommandEvent?.Invoke(this, new CustomEventArgs(data));
    }

    public void Update(object[] args)
    {
        if (args.Length < 4)
            return;

        if (_supply is { Connected: false })
            _seeded = false;
        else if (!_seeded)
            _seeded = SeedFromSupply();

        _lddVoltage.Text = Convert.ToDouble(args[0], CultureInfo.InvariantCulture).ToString("F1", CultureInfo.InvariantCulture);
        _lddCurrent.Text = Convert.ToDouble(args[1], CultureInfo.InvariantCulture).ToString("F2", CultureInfo.InvariantCulture);
        _lddPower.Text = Convert.ToDouble(args[2], CultureInfo.InvariantCulture).ToString("F0", CultureInfo.InvariantCulture);

        bool outputOn = args[3] is bool on && on;
        _btnOutput.Selected = outputOn;
        _ledPwr.LedOn = outputOn;

        bool responding = args.Length > 4 && args[4] is bool r && r;
        _ledCom.LedOn = responding;
        Color ink = responding ? _liveInk : VacuumTheme.TextDim;
        _lddVoltage.ForeColor = _lddCurrent.ForeColor = _lddPower.ForeColor = ink;
    }
}
