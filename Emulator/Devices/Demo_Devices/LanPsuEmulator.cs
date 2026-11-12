using System.Globalization;
using System.Text;
using EmulatorCore.Interfaces;
using EmulatorCore.Logging;
using EmulatorCore.Models;

namespace APS.Emulator.Devices.Demo;

public sealed class LanPsuEmulator : IPowerSupplyEmulator, IPowerReadout
{
    private const string NoError = "0,\"No error\"";
    private const string UndefinedHeader = "-113,\"Undefined header\"";
    private const string SettingsConflict = "-221,\"Settings conflict\"";
    private const string OutOfRange = "-222,\"Data out of range\"";
    private const float DefaultFrequencyKHz = 100f;
    private const float DefaultDutyPercent = 50f;
    private const float DischargeImpedanceOhm = 800f;

    private readonly object _lock = new();
    private readonly Queue<string> _errors = new();
    private readonly bool _rf;
    private readonly bool _dc;
    private bool _outputOn;
    private float _power;
    private float _currentMilliamps;
    private float _frequency = DefaultFrequencyKHz;
    private float _duty = DefaultDutyPercent;
    private float _actualVoltage;
    private float _actualCurrent;
    private float _actualPower;

    public LanPsuEmulator(string id, string mode, string description)
    {
        Id = id;
        Description = description;
        _rf = mode.Equals("RF", StringComparison.OrdinalIgnoreCase);
        _dc = mode.Equals("DC", StringComparison.OrdinalIgnoreCase);
    }

    public string Id { get; }
    public string Description { get; }
    public string Host => "";
    public DeviceCategory Category => DeviceCategory.PowerSupply;

    public PowerRegulationMode RegulationMode
    {
        get { lock (_lock) return !_rf && _currentMilliamps > 0f ? PowerRegulationMode.Current : PowerRegulationMode.Power; }
    }

    public float CommandedCurrent { get { lock (_lock) return _currentMilliamps / 1000f; } }
    public float CommandedPower { get { lock (_lock) return _power; } }
    public bool OutputEnabled { get { lock (_lock) return _outputOn; } }
    public float OpenCircuitVoltage => _rf ? 300f : 800f;

    string IPowerReadout.TypeName => "LanPsu";
    string IPowerReadout.VoltageText => Format("F1", v => v.Voltage);
    string IPowerReadout.CurrentText => Format("F3", v => v.Current);
    string IPowerReadout.PowerText => Format("F1", v => v.Power);
    string IPowerReadout.OutputText => OutputEnabled ? "ON" : "OFF";
    bool? IPowerReadout.OutputOn => OutputEnabled;

    void IPowerReadout.ToggleOutput()
    {
        lock (_lock) _outputOn = !_outputOn;
    }

    public void SetActualOutput(float voltage, float current, float power)
    {
        lock (_lock)
        {
            _actualVoltage = voltage;
            _actualCurrent = current;
            _actualPower = power;
        }
    }

    public void RegisterProtocol(IProtocolServer server)
    {
        if (server is ISerialProtocolServer serial && server.Name == Id)
            serial.RegisterHandler(HandleCommand);
    }

    public void Update(float deltaTime, SimulationState state)
    {
    }

    public void Reset()
    {
        lock (_lock)
        {
            _outputOn = false;
            _power = 0f;
            _currentMilliamps = 0f;
            _frequency = DefaultFrequencyKHz;
            _duty = DefaultDutyPercent;
            _actualVoltage = _actualCurrent = _actualPower = 0f;
            _errors.Clear();
        }
    }

    public object[] GetStatus()
    {
        lock (_lock)
            return [Id, _outputOn, _power, _frequency, _duty, _actualVoltage, _actualCurrent, _actualPower, Description];
    }

    public byte[] HandleCommand(byte[] data)
    {
        var reply = new StringBuilder();
        foreach (var line in Encoding.ASCII.GetString(data).Split('\n'))
        {
            string text = line.Trim();
            if (text.Length == 0)
                continue;
            lock (_lock)
                ExecuteLine(text, reply);
        }
        return Encoding.ASCII.GetBytes(reply.ToString());
    }

    private void ExecuteLine(string text, StringBuilder reply)
    {
        EmulatorLog.Log(LogLevel.Debug, $"LanPsu:{Id}", text);
        int space = text.IndexOf(' ');
        string header = (space < 0 ? text : text[..space]).ToUpperInvariant();
        string arg = space < 0 ? "" : text[(space + 1)..].Trim();

        switch (header)
        {
            case "*IDN?": Reply(reply, "APS,LANPSU-1000,0,1.0"); break;
            case "SYST:MODE?": Reply(reply, _rf ? "RF" : _dc ? "DC" : "PDC"); break;
            case "SYST:ERR?": Reply(reply, _errors.Count > 0 ? _errors.Dequeue() : NoError); break;
            case "OUTP": SetOutput(arg); break;
            case "OUTP?": Reply(reply, _outputOn ? "1" : "0"); break;
            case "POW": SetValue(arg, 0f, 1000f, v => _power = v); break;
            case "POW?": Reply(reply, Num(_power)); break;
            case "CURR": if (CheckCurrent()) SetValue(arg, 0f, 1000f, v => _currentMilliamps = v); break;
            case "CURR?": if (CheckCurrent()) Reply(reply, Num(_currentMilliamps)); break;
            case "FREQ": if (CheckPdc()) SetValue(arg, 1f, 350f, v => _frequency = v); break;
            case "FREQ?": if (CheckPdc()) Reply(reply, Num(_frequency)); break;
            case "DUTY": if (CheckPdc()) SetValue(arg, 5f, 95f, v => _duty = v); break;
            case "DUTY?": if (CheckPdc()) Reply(reply, Num(_duty)); break;
            case "MEAS:VOLT?": Reply(reply, Num(Effective().Voltage)); break;
            case "MEAS:CURR?": Reply(reply, Num(Effective().Current * 1000f)); break;
            case "MEAS:POW?": Reply(reply, Num(Effective().Power)); break;
            default: _errors.Enqueue(UndefinedHeader); break;
        }
    }

    private bool CheckPdc()
    {
        if (!_rf && !_dc)
            return true;
        _errors.Enqueue(SettingsConflict);
        return false;
    }

    private bool CheckCurrent()
    {
        if (!_rf)
            return true;
        _errors.Enqueue(SettingsConflict);
        return false;
    }

    private void SetOutput(string arg)
    {
        if (arg == "1") _outputOn = true;
        else if (arg == "0") _outputOn = false;
        else _errors.Enqueue(OutOfRange);
    }

    private void SetValue(string arg, float min, float max, Action<float> apply)
    {
        if (float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            && float.IsFinite(value) && value >= min && value <= max)
            apply(value);
        else
            _errors.Enqueue(OutOfRange);
    }

    private string Format(string format, Func<(float Voltage, float Current, float Power), float> pick)
    {
        lock (_lock)
        {
            var values = Effective();
            return pick(values).ToString(format, CultureInfo.InvariantCulture);
        }
    }

    private (float Voltage, float Current, float Power) Effective()
    {
        if (!_outputOn)
            return (0f, 0f, 0f);
        float v = _actualVoltage, i = _actualCurrent, p = _actualPower;
        if (!_rf && p > 0f && v == 0f)
        {
            v = MathF.Min(OpenCircuitVoltage, MathF.Sqrt(p * DischargeImpedanceOhm));
            i = p / v;
        }
        return (v, i, p);
    }

    private static string Num(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static void Reply(StringBuilder reply, string text) => reply.Append(text).Append('\n');
}
