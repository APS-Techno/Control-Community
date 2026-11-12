using System.Globalization;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.HWClasses.Base;

namespace APS.Hardware.Demo;

public sealed class LanThrottleController : BaseController, IPressureController, IDisposable
{
    private const string IdentityPrefix = "APS,LANTHROT-100";
    private const float CloseSentinelTorr = 9999f;
    private const float MilliTorrPerTorr = 1000f;

    private readonly BaseSocketPort _socket;
    private readonly object _setGate = new();
    private int _readInFlight;
    private volatile float _pressureTorr;
    private volatile float _positionPercent;
    private volatile float _targetTorr;
    private float? _lastTarget;

    public LanThrottleController(string id, string host, int port, string description)
        : base(ParamsConverter.Params(id, 0, description))
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(host);
        _socket = new BaseSocketPort(host, port);
    }

    public float PressureTorr => _pressureTorr;
    public float ValvePositionPercent => _positionPercent;

    public double GaugeFloorTorr { get; private set; }

    public bool IsResponding { get; private set; }
    public string? LastCommFault { get; private set; }

    public int TimeoutMs
    {
        get => _socket.TimeoutMs;
        set => _socket.TimeoutMs = value;
    }

    public override bool Connect()
    {
        GaugeFloorTorr = 0;
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

            if (!TryParseDouble(_socket.SendCommand("FLOOR?"), out double floor) || floor <= 0)
            {
                _socket.Disconnect();
                Fail("unusable FLOOR? reply");
                return false;
            }
            GaugeFloorTorr = floor;

            if (!ReadState())
            {
                _socket.Disconnect();
                return false;
            }
        }
        catch (Exception ex)
        {
            _socket.Disconnect();
            Fail($"{ex.GetType().Name}: {ex.Message}");
            return false;
        }

        _connected = true;
        IsResponding = true;
        LastCommFault = null;
        return true;
    }

    public override void Disconnect()
    {
        _socket.Disconnect();
        _connected = false;
    }

    public void Dispose() => _socket.Dispose();

    public override object[] Measure()
    {
        if (Interlocked.CompareExchange(ref _readInFlight, 1, 0) != 0)
            return Snapshot();

        try
        {
            if (!_socket.IsConnected && !_socket.Connect())
            {
                Fail("socket is not connected");
                return [];
            }
            if (!ReadState())
                return [];
            IsResponding = true;
            LastCommFault = null;
            return Snapshot();
        }
        catch (Exception ex)
        {
            Fail($"{ex.GetType().Name}: {ex.Message}");
            return [];
        }
        finally
        {
            Interlocked.Exchange(ref _readInFlight, 0);
        }
    }

    public override void Command(object[] args)
    {
        if (args.Length == 0)
            return;
        switch (args[0] as string)
        {
            case "Open":
                SendTarget(0f);
                break;
            case "Close":
                SendTarget(CloseSentinelTorr);
                break;
            case "Stop":
                if (_lastTarget is float held)
                    SendTarget(held);
                break;
            case "SetPressure":
            {
                if (args.Length < 2)
                    throw new ArgumentException(
                        $"LanThrottleController[{Params?.ID}]: SetPressure needs a target in Torr.", nameof(args));
                float pressure = ToSingle(args[1]);
                if (!float.IsFinite(pressure) || pressure < 0f)
                    throw new ArgumentOutOfRangeException(
                        nameof(args), pressure,
                        "LanThrottleController target pressure must be a finite non-negative number");
                SendTarget(pressure);
                break;
            }
        }
    }

    private object[] Snapshot() =>
        ["Data", _pressureTorr * MilliTorrPerTorr, _positionPercent, _targetTorr * MilliTorrPerTorr];

    private bool ReadState()
    {
        if (!TryParseFloat(_socket.SendCommand("PRES?"), out float pressure) ||
            !TryParseFloat(_socket.SendCommand("POS?"), out float position) ||
            !TryParseFloat(_socket.SendCommand("TARG?"), out float target))
        {
            Fail("unparseable measurement reply");
            return false;
        }
        _pressureTorr = pressure;
        _positionPercent = position;
        _targetTorr = target;
        return true;
    }

    private void SendTarget(float targetTorr)
    {
        string command = targetTorr == 0f
            ? "PARK"
            : $"TARG {targetTorr.ToString("R", CultureInfo.InvariantCulture)}";
        if (!SendSet(command))
            throw new IOException($"LanThrottleController[{Params?.ID}]: '{command}' not delivered: {LastCommFault}");
        _lastTarget = targetTorr;
        _targetTorr = targetTorr;
    }

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
                    $"LanThrottleController[{Params?.ID}]: '{command}' rejected by device: {reply}");
            return true;
        }
    }

    private void Fail(string fault)
    {
        IsResponding = false;
        LastCommFault = fault;
        System.Diagnostics.Trace.TraceWarning($"LanThrottleController[{Params?.ID}] {fault}");
    }

    private static float ToSingle(object value) =>
        value is string text
            ? float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture)
            : Convert.ToSingle(value, CultureInfo.InvariantCulture);

    private static bool TryParseFloat(string text, out float value) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static bool TryParseDouble(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
}
