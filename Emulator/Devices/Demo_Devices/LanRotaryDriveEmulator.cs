using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using EmulatorCore.Engine;
using EmulatorCore.Interfaces;
using EmulatorCore.Logging;
using EmulatorCore.Models;

namespace APS.Emulator.Devices.Demo;

public sealed class LanRotaryDriveEmulator : IShutterEmulator, ISnapshotable
{
    private const string NoError = "0,\"No error\"";
    private const string UndefinedHeader = "-113,\"Undefined header\"";
    private const string InvalidPosition = "-222,\"Invalid position\"";
    private const int MaxFlange = 8;
    private const float StepsPerFlange = 4024f;
    private const float DefaultFlangesPerSecond = 3000f / StepsPerFlange;

    private readonly object _lock = new();
    private readonly Queue<string> _errors = new();
    private readonly float _flangesPerSecond;
    private float _position;
    private int _target;

    public LanRotaryDriveEmulator(string id, string description, float flangesPerSecond = DefaultFlangesPerSecond)
    {
        Id = id;
        Description = description;
        _flangesPerSecond = flangesPerSecond > 0f ? flangesPerSecond : DefaultFlangesPerSecond;
    }

    public static IDeviceEmulator? Create(DeviceCreateContext ctx)
    {
        float speed = float.TryParse(ctx.Element.Attribute("speed")?.Value, NumberStyles.Float,
            CultureInfo.InvariantCulture, out float s) ? s : DefaultFlangesPerSecond;
        return new LanRotaryDriveEmulator(ctx.Element.Value.Trim(), ctx.Element.Attribute("descr")?.Value ?? "", speed);
    }

    public string Id { get; }
    public string Description { get; }
    public string Host => "";
    public DeviceCategory Category => DeviceCategory.Stage;

    public bool IsHomed => true;
    public float Position { get { lock (_lock) return _position * StepsPerFlange; } }
    public bool IsInMotion { get { lock (_lock) return Moving; } }

    private bool Moving => _position != _target;

    public void RegisterProtocol(IProtocolServer server)
    {
        if (server is ISerialProtocolServer serial && server.Name == Id)
            serial.RegisterHandler(HandleCommand);
    }

    public void Update(float deltaTime, SimulationState state)
    {
        lock (_lock)
        {
            if (!Moving)
                return;
            float step = _flangesPerSecond * deltaTime;
            float distance = _target - _position;
            _position = MathF.Abs(distance) <= step ? _target : _position + MathF.Sign(distance) * step;
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _position = 0f;
            _target = 0;
            _errors.Clear();
        }
    }

    public object[] GetStatus()
    {
        lock (_lock)
            return [Id, _position, Moving ? "Moving" : "Idle", PositionToken(), Description];
    }

    public JsonObject GetSnapshot()
    {
        lock (_lock)
            return new JsonObject { ["position"] = _position, ["target"] = _target };
    }

    public void RestoreSnapshot(JsonObject snapshot)
    {
        lock (_lock)
        {
            _target = Math.Clamp(snapshot["target"]?.GetValue<int>() ?? _target, 0, MaxFlange);
            _position = Math.Clamp(snapshot["position"]?.GetValue<float>() ?? _position, 0f, MaxFlange);
            _errors.Clear();
        }
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
        EmulatorLog.Log(LogLevel.Debug, $"LanDrive:{Id}", text);
        int space = text.IndexOf(' ');
        string header = (space < 0 ? text : text[..space]).ToUpperInvariant();
        string arg = space < 0 ? "" : text[(space + 1)..].Trim();

        switch (header)
        {
            case "*IDN?": Reply(reply, "APS,LANDRIVE-100,0,1.0"); break;
            case "SYST:ERR?": Reply(reply, _errors.Count > 0 ? _errors.Dequeue() : NoError); break;
            case "POS": MoveTo(arg); break;
            case "POS?": Reply(reply, PositionToken()); break;
            case "BUSY?": Reply(reply, Moving ? "1" : "0"); break;
            default: _errors.Enqueue(UndefinedHeader); break;
        }
    }

    private void MoveTo(string token)
    {
        if (token.Equals("Home", StringComparison.OrdinalIgnoreCase))
            _target = 0;
        else if (int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out int flange)
                 && flange <= MaxFlange)
            _target = flange;
        else
            _errors.Enqueue(InvalidPosition);
    }

    private string PositionToken()
    {
        int flange = (int)MathF.Round(_position);
        return flange == 0 ? "Home" : flange.ToString(CultureInfo.InvariantCulture);
    }

    private static void Reply(StringBuilder reply, string text) => reply.Append(text).Append('\n');
}
