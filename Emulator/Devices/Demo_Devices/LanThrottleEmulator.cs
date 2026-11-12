using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using EmulatorCore.Engine;
using EmulatorCore.Interfaces;
using EmulatorCore.Logging;
using EmulatorCore.Models;

namespace APS.Emulator.Devices.Demo;

public sealed class LanThrottleEmulator : IDeviceEmulator, ISnapshotable
{
    public const double DefaultFloorTorr = 1e-5;

    private const string NoError = "0,\"No error\"";
    private const string UndefinedHeader = "-113,\"Undefined header\"";
    private const string OutOfRange = "-222,\"Data out of range\"";
    private const double CloseSentinelTorr = 9999.0;
    private const float MinPosition = 0.5f;
    private const float OpenRate = 33f;
    private const float CloseRate = 20f;
    private const float Kp = 8f;
    private const float Ki = 0.5f;
    private const float IntegralLimit = 50f;
    private const float ReferenceMilliTorr = 3f;

    private readonly object _lock = new();
    private readonly Queue<string> _errors = new();
    private readonly string _chamberId;
    private readonly double _floorTorr;
    private double _targetTorr;
    private float _position = 100f;
    private float _pressureTorr;
    private float _integral;

    public LanThrottleEmulator(string id, string description, string chamberId,
        double floorTorr = DefaultFloorTorr)
    {
        ArgumentException.ThrowIfNullOrEmpty(chamberId);
        if (!double.IsFinite(floorTorr) || floorTorr <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(floorTorr), floorTorr, "Gauge floor must be positive.");
        Id = id;
        Description = description;
        _chamberId = chamberId;
        _floorTorr = floorTorr;
    }

    public static IDeviceEmulator? Create(DeviceCreateContext ctx)
    {
        string id = ctx.Element.Value.Trim();
        string chamber = (ctx.Element.Attribute("chamber")?.Value ?? "").Trim();
        if (chamber.Length == 0)
        {
            EmulatorLog.Log(LogLevel.Warning, "LanThrottleController",
                $"Device '{id}' is missing the required chamber= attribute. Device skipped.");
            return null;
        }
        return new LanThrottleEmulator(id, ctx.Element.Attribute("descr")?.Value ?? "", chamber);
    }

    public string Id { get; }
    public string Description { get; }
    public string Host => "";
    public string ChamberId => _chamberId;
    public DeviceCategory Category => DeviceCategory.Controller;

    public float Position { get { lock (_lock) return _position; } }
    public double TargetTorr { get { lock (_lock) return _targetTorr; } }

    public void RegisterProtocol(IProtocolServer server)
    {
        if (server is ISerialProtocolServer serial && server.Name == Id)
            serial.RegisterHandler(HandleCommand);
    }

    public void Update(float deltaTime, SimulationState state)
    {
        lock (_lock)
        {
            state.Chambers.TryGetValue(_chamberId, out var chamber);
            if (chamber != null)
                _pressureTorr = chamber.Pressure;

            float desired = Math.Clamp(ResolveDesiredPosition(chamber, deltaTime), MinPosition, 100f);
            if (MathF.Abs(_position - desired) > 0.01f)
            {
                float step = (desired > _position ? OpenRate : CloseRate) * deltaTime;
                _position = desired > _position
                    ? MathF.Min(_position + step, desired)
                    : MathF.Max(_position - step, desired);
            }
            else
            {
                _position = desired;
            }

            if (chamber != null)
            {
                chamber.ThrottlePosition = _position;
                chamber.ThrottleSetpointTorr = (float)_targetTorr;
            }
        }
    }

    private float ResolveDesiredPosition(ChamberState? chamber, float deltaTime)
    {
        if (_targetTorr == 0.0)
            return 100f;
        if (_targetTorr >= CloseSentinelTorr)
            return MinPosition;
        if (chamber == null)
            return _position;

        double setpointTorr = Math.Max(_targetTorr, chamber.BasePressure);
        // The error is relative to the setpoint, scaled to a 3 mTorr reference, so the loop
        // gain at a floor-scale target equals the gain at a process-scale target. An error in
        // absolute mTorr is ~1e-5 near the floor and cannot move the valve.
        float error = ReferenceMilliTorr * (float)((chamber.Pressure - setpointTorr) / setpointTorr);
        _integral = Math.Clamp(_integral + error * deltaTime, -IntegralLimit, IntegralLimit);
        return _position + Kp * error + Ki * _integral;
    }

    public void Reset()
    {
        lock (_lock)
        {
            _targetTorr = 0.0;
            _position = 100f;
            _integral = 0f;
            _errors.Clear();
        }
    }

    public JsonObject GetSnapshot()
    {
        lock (_lock)
            return new JsonObject
            {
                ["targetTorr"] = _targetTorr,
                ["position"] = _position,
                ["integral"] = _integral,
            };
    }

    public void RestoreSnapshot(JsonObject snapshot)
    {
        lock (_lock)
        {
            _targetTorr = snapshot["targetTorr"]?.GetValue<double>() ?? _targetTorr;
            _position = snapshot["position"]?.GetValue<float>() ?? _position;
            _integral = snapshot["integral"]?.GetValue<float>() ?? _integral;
        }
    }

    public object[] GetStatus()
    {
        lock (_lock)
            return [Id, _targetTorr, _position, Description];
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
        EmulatorLog.Log(LogLevel.Debug, $"LanThrottle:{Id}", text);
        int space = text.IndexOf(' ');
        string header = (space < 0 ? text : text[..space]).ToUpperInvariant();
        string arg = space < 0 ? "" : text[(space + 1)..].Trim();

        switch (header)
        {
            case "*IDN?": Reply(reply, "APS,LANTHROT-100,0,1.0"); break;
            case "FLOOR?": Reply(reply, Num(_floorTorr)); break;
            case "TARG": SetTarget(arg); break;
            case "TARG?": Reply(reply, Num(_targetTorr)); break;
            case "PARK": SetTargetValue(0.0); break;
            case "POS?": Reply(reply, Num(_position)); break;
            case "PRES?": Reply(reply, Num(_pressureTorr)); break;
            case "SYST:ERR?": Reply(reply, _errors.Count > 0 ? _errors.Dequeue() : NoError); break;
            default: _errors.Enqueue(UndefinedHeader); break;
        }
    }

    private void SetTarget(string arg)
    {
        if (!double.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out double target)
            || !double.IsFinite(target) || target < 0.0
            || (target > 0.0 && target < _floorTorr * 0.999999))
        {
            _errors.Enqueue(OutOfRange);
            return;
        }
        SetTargetValue(target);
    }

    private void SetTargetValue(double target)
    {
        _targetTorr = target;
        _integral = 0f;
    }

    private static string Num(double value) =>
        value.ToString("R", CultureInfo.InvariantCulture).ToLowerInvariant();

    private static string Num(float value) =>
        value.ToString("R", CultureInfo.InvariantCulture).ToLowerInvariant();

    private static void Reply(StringBuilder reply, string text) => reply.Append(text).Append('\n');
}
