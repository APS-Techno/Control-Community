using System.Globalization;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.HWClasses.Base;

namespace APS.Hardware.Demo;

public sealed class LanTransferRobot : BaseRobot, IDisposable
{
    private const string IdentityPrefix = "APS,LANROBOT-100";
    private const string ParkName = "PARK";

    private readonly BaseSocketPort _socket;
    private readonly object _cmdGate = new();
    private readonly object _pollGate = new();
    private byte _nextCmdId = 1;

    public LanTransferRobot(string id, string host, int port, string description)
        : base(ParamsConverter.Params(id, 0, description))
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(host);
        _socket = new BaseSocketPort(host, port);
    }

    public int TimeoutMs
    {
        get => _socket.TimeoutMs;
        set => _socket.TimeoutMs = value;
    }

    public string? LastCommFault { get; private set; }

    public override bool Connect()
    {
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

            ReadStatus();
        }
        catch (Exception ex)
        {
            _socket.Disconnect();
            Fail($"{ex.GetType().Name}: {ex.Message}");
            return false;
        }

        Connected = true;
        LastCommFault = null;
        return true;
    }

    public void Disconnect()
    {
        _socket.Disconnect();
        Connected = false;
    }

    public bool Reconnect()
    {
        Disconnect();
        return Connect();
    }

    public void Dispose() => _socket.Dispose();

    public override void Poll()
    {
        if (!Connected)
            return;
        try
        {
            ReadStatus();
            LastCommFault = null;
        }
        catch (Exception ex)
        {
            Fail($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    public override void Transfer(byte srcFace, byte dstFace)
        => Issue($"XFER {NameOf(srcFace)},{NameOf(dstFace)}");

    public override void Home() => Issue("HOME");

    public override void Abort() => Issue("ABOR");

    private void ReadStatus()
    {
        string stat = _socket.SendCommand("STAT?").Trim();
        string loc = _socket.SendCommand("LOC?").Trim();

        RobotState state;
        switch (stat.ToUpperInvariant())
        {
            case "IDLE": state = RobotState.Idle; break;
            case "TRANSFERRING": state = RobotState.Transferring; break;
            case "ERROR": state = RobotState.Error; break;
            default: throw new FormatException($"unparseable STAT? reply '{stat}'");
        }

        byte face = Faces.TryGetValue(loc, out byte idx) ? idx : (byte)0;
        lock (_pollGate)
        {
            State = state;
            CurrentFace = face;
            Homed = state != RobotState.Error;
            ErrorCode = state == RobotState.Error ? RobotErrorCode.Aborted : RobotErrorCode.Ok;
            LastCmdId = LastIssuedCmdId;
        }
    }

    private string NameOf(byte face)
    {
        foreach (var (name, idx) in Faces)
            if (idx == face)
                return name;
        if (face == RobotWireContract.ParkFace)
            return ParkName;
        throw new ArgumentException($"LanTransferRobot[{Params.ID}]: no face with index {face}.");
    }

    private void Issue(string command)
    {
        lock (_cmdGate)
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
                throw;
            }

            int comma = reply.IndexOf(',');
            string codeText = comma < 0 ? reply : reply[..comma];
            if (!int.TryParse(codeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int code))
                throw new InvalidOperationException(
                    $"LanTransferRobot[{Params.ID}]: '{command}' answered unusable error reply '{reply}'");
            if (code != 0)
                throw new InvalidOperationException(
                    $"LanTransferRobot[{Params.ID}]: '{command}' rejected by device: {reply}");

            byte id = _nextCmdId;
            _nextCmdId = (byte)(_nextCmdId == 255 ? 1 : _nextCmdId + 1);
            LastIssuedCmdId = id;
        }
    }

    private void Fail(string fault)
    {
        LastCommFault = fault;
        System.Diagnostics.Trace.TraceWarning($"LanTransferRobot[{Params?.ID}] {fault}");
    }
}
