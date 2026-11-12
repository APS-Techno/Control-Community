using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Sockets;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.HWClasses.Base;

namespace APS.Hardware.Demo;

public sealed class LanPowerSupply : BasePowerSupply, IDisposable
{
    private const string IdentityPrefix = "APS,LANPSU-1000";

    private static readonly string[] s_dcNames = ["Power", "Current"];
    private static readonly string[] s_pdcNames = ["Power", "Current", "Frequency", "DutyCycle"];
    private static readonly string[] s_rfNames = ["Power"];

    private readonly BaseSocketPort _socket;
    private readonly string _mode;
    private readonly object _setGate = new();
    private int _readInFlight;
    private readonly ConcurrentDictionary<string, float> _setpoints = new();

    private static readonly (string Parameter, string Query)[] SetpointQueries =
        [("Power", "POW?"), ("Current", "CURR?"), ("Frequency", "FREQ?"), ("DutyCycle", "DUTY?")];

    public LanPowerSupply(string id, string host, int port, string mode, string description)
        : base(ParamsConverter.Params(id, 0, description))
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(host);
        if (string.Equals(mode, "DC", StringComparison.OrdinalIgnoreCase))
            _mode = "DC";
        else if (string.Equals(mode, "PDC", StringComparison.OrdinalIgnoreCase))
            _mode = "PDC";
        else if (string.Equals(mode, "RF", StringComparison.OrdinalIgnoreCase))
            _mode = "RF";
        else
            throw new ArgumentException(
                $"LanPowerSupply[{id}]: mode '{mode}' is not DC, PDC or RF.", nameof(mode));
        _socket = new BaseSocketPort(host, port);
    }

    private void ReadSetpoints()
    {
        foreach (var (parameter, query) in SetpointQueries)
        {
            if (!SetpointParameterNames.Contains(parameter))
                continue;
            if (!TryParseFloat(_socket.SendCommand(query), out float value))
                continue;
            _setpoints[parameter] = value;
            if (parameter == "Power")
                SetPower = value;
            else if (parameter == "Current")
                SetCurrent = value;
        }
    }

    public int TimeoutMs
    {
        get => _socket.TimeoutMs;
        set => _socket.TimeoutMs = value;
    }

    public override bool SupportsPowerSetpoint => true;

    public override IReadOnlyCollection<string> SetpointParameterNames =>
        _mode switch { "DC" => s_dcNames, "PDC" => s_pdcNames, _ => s_rfNames };

    public override bool TryGetSetpoint(string parameter, out float value) =>
        _setpoints.TryGetValue(parameter, out value);

    public override bool Connect()
    {
        _setpoints.Clear();
        try
        {
            if (!_socket.Connect())
            {
                Fail($"TCP connect to {_socket.Host}:{_socket.Port} failed");
                return false;
            }

            string identity = _socket.SendCommand("*IDN?");
            if (!identity.StartsWith(IdentityPrefix, StringComparison.Ordinal))
            {
                _socket.Disconnect();
                Fail($"unexpected identity '{identity}'");
                return false;
            }

            string deviceMode = _socket.SendCommand("SYST:MODE?").Trim();
            if (!string.Equals(deviceMode, _mode, StringComparison.OrdinalIgnoreCase))
            {
                _socket.Disconnect();
                Fail($"device reports mode '{deviceMode}', configured mode is '{_mode}'");
                return false;
            }

            ReadSetpoints();
        }
        catch (Exception ex)
        {
            _socket.Disconnect();
            Fail($"{ex.GetType().Name}: {ex.Message}");
            return false;
        }

        Connected = true;
        IsResponding = true;
        LastCommFault = null;
        return true;
    }

    public override void Disconnect()
    {
        _socket.Disconnect();
        _setpoints.Clear();
        Connected = false;
    }

    public override bool Reconnect()
    {
        Disconnect();
        return Connect();
    }

    public void Dispose() => _socket.Dispose();

    public override void ReadFeedback()
    {
        if (Interlocked.CompareExchange(ref _readInFlight, 1, 0) != 0)
            return;

        try
        {
            if (!_socket.IsConnected)
            {
                Fail("socket is not connected");
                return;
            }

            if (!TryParseFloat(_socket.SendCommand("MEAS:VOLT?"), out float volt) ||
                !TryParseFloat(_socket.SendCommand("MEAS:CURR?"), out float curr) ||
                !TryParseFloat(_socket.SendCommand("MEAS:POW?"), out float pow))
            {
                Fail("unparseable measurement reply");
                return;
            }

            string outp = _socket.SendCommand("OUTP?").Trim();
            if (outp != "0" && outp != "1")
            {
                Fail($"unparseable OUTP? reply '{outp}'");
                return;
            }

            Voltage = volt;
            Current = curr;
            Power = pow;
            IsOutputOn = outp == "1";
            IsResponding = true;
            LastCommFault = null;
        }
        catch (Exception ex)
        {
            Fail($"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _readInFlight, 0);
        }
    }

    public override void ApplySetpoint(string parameter, float value)
    {
        string command = parameter switch
        {
            "Power" => "POW",
            "Current" => "CURR",
            "Frequency" => "FREQ",
            "DutyCycle" => "DUTY",
            _ => "",
        };
        if (command.Length == 0 || !SetpointParameterNames.Contains(parameter))
            throw new ArgumentException(
                $"LanPowerSupply[{Params?.ID}]: unknown setpoint parameter '{parameter}'. " +
                $"Valid: {string.Join(", ", SetpointParameterNames)}.", nameof(parameter));
        if (!float.IsFinite(value))
            throw new ArgumentException(
                $"LanPowerSupply[{Params?.ID}]: {parameter} setpoint must be a finite number.",
                nameof(value));

        if (SendSet($"{command} {value.ToString("R", CultureInfo.InvariantCulture)}"))
        {
            _setpoints[parameter] = value;
            if (command == "POW")
                SetPower = value;
            else if (command == "CURR")
                SetCurrent = value;
        }
    }

    protected override void WriteOutputState(bool on) => SendSet(on ? "OUTP 1" : "OUTP 0");

    private bool SendSet(string command)
    {
        lock (_setGate)
        {
            string reply;
            try
            {
                _socket.Send(command);
                reply = _socket.SendCommand("SYST:ERR?").Trim();
            }
            catch (Exception ex)
            {
                Fail($"'{command}' not delivered: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
            int comma = reply.IndexOf(',');
            string codeText = comma < 0 ? reply : reply[..comma];
            if (!int.TryParse(codeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int code))
            {
                Fail($"'{command}' answered unusable error reply '{reply}'");
                return false;
            }
            if (code != 0)
                throw new ArgumentException(
                    $"LanPowerSupply[{Params?.ID}]: '{command}' rejected by device: {reply}");
            return true;
        }
    }

    public override object[] GetReadback() => [Voltage, Current, Power, IsOutputOn, IsResponding];

    private void Fail(string fault)
    {
        IsResponding = false;
        LastCommFault = fault;
        System.Diagnostics.Trace.TraceWarning($"LanPowerSupply[{Params?.ID}] {fault}");
    }

    private static bool TryParseFloat(string text, out float value) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
