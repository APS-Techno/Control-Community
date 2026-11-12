using System.Globalization;
using System.Text;
using EmulatorCore.Engine;
using EmulatorCore.Interfaces;
using EmulatorCore.Logging;
using EmulatorCore.Models;

namespace APS.Emulator.Devices.Demo;

public sealed class LanTransferRobotEmulator : IRobotStatus
{
    private const string NoError = "0,\"No error\"";
    private const string UndefinedHeader = "-113,\"Undefined header\"";
    private const string SettingsConflict = "-221,\"Settings conflict\"";
    private const string OutOfRange = "-222,\"Data out of range\"";
    private const string ParkAlias = "PARK";

    private readonly record struct Face(string Name, byte Idx, string Slit);

    private readonly object _lock = new();
    private readonly Queue<string> _errors = new();
    private readonly Face[] _faces;
    private readonly int _rotateMs;
    private readonly int _extendMs;
    private readonly int _liftMs;

    private RobotState _state = RobotState.Idle;
    private RobotErrorCode _errorCode = RobotErrorCode.Ok;
    private Face _location;
    private Face _source;
    private Face _target;
    private double _elapsedMs;
    private byte _cmdId;

    private LanTransferRobotEmulator(string id, string description, Face[] faces,
        int rotateMs, int extendMs, int liftMs)
    {
        Id = id;
        Description = description;
        _faces = faces;
        _rotateMs = rotateMs;
        _extendMs = extendMs;
        _liftMs = liftMs;
        _location = _source = _target = faces[0];
    }

    public static IDeviceEmulator? Create(DeviceCreateContext ctx)
    {
        string id = ctx.Element.Value.Trim();
        var faces = new List<Face>();
        foreach (var element in ctx.Element.Elements("face"))
        {
            string name = (element.Attribute("name")?.Value ?? "").Trim();
            string idxText = (element.Attribute("idx")?.Value ?? "").Trim();
            if (name.Length == 0 || !byte.TryParse(idxText, NumberStyles.None, CultureInfo.InvariantCulture, out byte idx))
            {
                EmulatorLog.Log(LogLevel.Warning, "LanTransferRobot",
                    $"Device '{id}' has a face with invalid name='{name}' idx='{idxText}'. Device skipped.");
                return null;
            }
            faces.Add(new Face(name, idx, (element.Attribute("slit")?.Value ?? "").Trim()));
        }
        if (faces.Count == 0)
        {
            EmulatorLog.Log(LogLevel.Warning, "LanTransferRobot", $"Device '{id}' has no <face> elements. Device skipped.");
            return null;
        }

        var timing = ctx.Element.Element("timing");
        return new LanTransferRobotEmulator(id, ctx.Element.Attribute("descr")?.Value ?? "", [.. faces],
            Millis(timing, "rotateMs", 2000), Millis(timing, "extendMs", 1500), Millis(timing, "liftMs", 800));
    }

    public string Id { get; }
    public string Description { get; }
    public string Host => "";
    public DeviceCategory Category => DeviceCategory.Controller;

    public bool HasWafer => false;
    public RobotErrorCode LastErrorCode { get { lock (_lock) return _errorCode; } }
    public byte TransferSrc { get { lock (_lock) return _source.Idx; } }
    public byte TransferDst { get { lock (_lock) return _target.Idx; } }
    public IReadOnlyCollection<string> SlitValveIds =>
        _faces.Where(f => f.Slit.Length > 0).Select(f => f.Slit).ToArray();
    public byte LastCmdId { get { lock (_lock) return _cmdId; } }

    public RobotState GetState() { lock (_lock) return _state; }

    public RobotPhase GetPhase()
    {
        lock (_lock)
        {
            if (_state != RobotState.Transferring)
                return RobotPhase.None;
            if (_elapsedMs < _rotateMs)
                return RobotPhase.RotateToDst;
            return _elapsedMs < _rotateMs + _extendMs ? RobotPhase.ExtendToDst : RobotPhase.LowerAtDst;
        }
    }

    public byte GetCurrentFace() { lock (_lock) return _location.Idx; }

    public string FaceName(byte idx)
    {
        foreach (var face in _faces)
            if (face.Idx == idx)
                return face.Name;
        return idx.ToString(CultureInfo.InvariantCulture);
    }

    public void RegisterProtocol(IProtocolServer server)
    {
        if (server is ISerialProtocolServer serial && server.Name == Id)
            serial.RegisterHandler(HandleCommand);
    }

    public void Update(float deltaTime, SimulationState state)
    {
        lock (_lock)
        {
            if (_state != RobotState.Transferring)
                return;
            _elapsedMs += deltaTime * 1000.0;
            if (_elapsedMs >= _rotateMs + _extendMs + _liftMs)
            {
                _location = _target;
                _state = RobotState.Idle;
            }
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _location = _source = _target = _faces[0];
            _state = RobotState.Idle;
            _errorCode = RobotErrorCode.Ok;
            _elapsedMs = 0;
            _errors.Clear();
        }
    }

    public object[] GetStatus()
    {
        lock (_lock)
            return [Id, _state.ToString(), _location.Name, Description];
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
        EmulatorLog.Log(LogLevel.Debug, $"LanTransferRobot:{Id}", text);
        int space = text.IndexOf(' ');
        string header = (space < 0 ? text : text[..space]).ToUpperInvariant();
        string arg = space < 0 ? "" : text[(space + 1)..].Trim();

        switch (header)
        {
            case "*IDN?": Reply(reply, "APS,LANROBOT-100,0,1.0"); break;
            case "SYST:ERR?": Reply(reply, _errors.Count > 0 ? _errors.Dequeue() : NoError); break;
            case "STAT?": Reply(reply, _state.ToString().ToUpperInvariant()); break;
            case "LOC?": Reply(reply, _location.Name); break;
            case "XFER": Transfer(arg); break;
            case "HOME": Home(); break;
            case "ABOR": Abort(); break;
            default: _errors.Enqueue(UndefinedHeader); break;
        }
    }

    private void Transfer(string arg)
    {
        int comma = arg.IndexOf(',');
        if (comma < 0)
        {
            _errors.Enqueue(OutOfRange);
            return;
        }
        if (_state != RobotState.Idle)
        {
            _errors.Enqueue(SettingsConflict);
            return;
        }

        _cmdId = NextId(_cmdId);
        if (!TryResolve(arg[..comma].Trim(), out Face from) || !TryResolve(arg[(comma + 1)..].Trim(), out Face to))
        {
            _errors.Enqueue(OutOfRange);
            _state = RobotState.Error;
            _errorCode = RobotErrorCode.BadCmd;
            return;
        }

        _source = from;
        _target = to;
        _elapsedMs = 0;
        _errorCode = RobotErrorCode.Ok;
        _state = RobotState.Transferring;
    }

    private void Home()
    {
        _cmdId = NextId(_cmdId);
        _location = _source = _target = _faces[0];
        _elapsedMs = 0;
        _errorCode = RobotErrorCode.Ok;
        _state = RobotState.Idle;
    }

    private void Abort()
    {
        _cmdId = NextId(_cmdId);
        if (_state != RobotState.Transferring)
            return;
        _state = RobotState.Error;
        _errorCode = RobotErrorCode.Aborted;
    }

    private bool TryResolve(string name, out Face face)
    {
        if (name.Equals(ParkAlias, StringComparison.OrdinalIgnoreCase))
        {
            face = _faces[0];
            return true;
        }
        foreach (var candidate in _faces)
        {
            if (candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                face = candidate;
                return true;
            }
        }
        face = default;
        return false;
    }

    private static byte NextId(byte id) => (byte)(id == 255 ? 1 : id + 1);

    private static int Millis(System.Xml.Linq.XElement? timing, string attribute, int fallback)
        => int.TryParse(timing?.Attribute(attribute)?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int ms)
            ? ms : fallback;

    private static void Reply(StringBuilder reply, string text) => reply.Append(text).Append('\n');
}
