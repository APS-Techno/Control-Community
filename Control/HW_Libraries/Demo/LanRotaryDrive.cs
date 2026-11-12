using System.Globalization;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.HWClasses.Base;

namespace APS.Hardware.Demo;

public sealed class LanRotaryDrive : BaseStepperDrive, IDisposable
{
    private const string IdentityPrefix = "APS,LANDRIVE-100";
    private const int MaxFlange = 8;
    private const int ArrivalTimeoutMs = 30_000;

    private readonly BaseSocketPort _socket;
    private readonly object _ioGate = new();
    private CancellationTokenSource? _sequence;
    private volatile bool _sequenceActive;

    public LanRotaryDrive(string id, string host, int port, string description)
        : base(ParamsConverter.Params(id, 0, description))
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(host);
        _socket = new BaseSocketPort(host, port);
    }

    public bool IsResponding { get; private set; }

    public string? LastCommFault { get; private set; }

    public int TimeoutMs
    {
        get => _socket.TimeoutMs;
        set => _socket.TimeoutMs = value;
    }

    public override bool FlangePositionKnown => true;

    public override bool Connect()
    {
        try
        {
            if (!_socket.Connect())
            {
                Fail($"TCP connect to {_socket.Host}:{_socket.Port} failed");
                return false;
            }

            string identity;
            lock (_ioGate)
                identity = _socket.SendCommand("*IDN?");
            if (!identity.StartsWith(IdentityPrefix, StringComparison.Ordinal))
            {
                _socket.Disconnect();
                Fail($"unexpected identity '{identity}'");
                return false;
            }

            Poll();
        }
        catch (Exception ex)
        {
            _socket.Disconnect();
            Fail($"{ex.GetType().Name}: {ex.Message}");
            return false;
        }

        Connected = true;
        IsHomed = true;
        IsResponding = true;
        LastCommFault = null;
        return true;
    }

    public void Disconnect()
    {
        CancelSequence();
        _socket.Disconnect();
        Connected = false;
    }

    public void Dispose()
    {
        CancelSequence();
        _socket.Dispose();
    }

    public override void MoveToFlange(int flangeNum)
    {
        SendPosition(flangeNum.ToString(CultureInfo.InvariantCulture), flangeNum);
    }

    public override void MoveTo(float position)
    {
        if (!float.IsFinite(position))
            throw new ArgumentException($"LanRotaryDrive[{Params?.ID}]: position {position} is not finite.");
        int flange = (int)MathF.Round(position, MidpointRounding.AwayFromZero);
        MoveToFlange(flange);
    }

    public override void Home()
    {
        SendPosition("Home", 0);
    }

    public override void ProcessCommand(string command, object[] args)
    {
        switch (command)
        {
            case "MoveToFlange" when args.Length >= 1:
                MoveToFlange(Convert.ToInt32(args[0], CultureInfo.InvariantCulture));
                break;
            case "MoveTo" when args.Length >= 1:
                MoveTo(Convert.ToSingle(args[0], CultureInfo.InvariantCulture));
                break;
            case "Home":
                Home();
                break;
            default:
                base.ProcessCommand(command, args);
                break;
        }
    }

    public override void OpenShutterForDeposition(int pos1, int pos2, float durationSeconds)
    {
        CancelSequence();
        var cts = new CancellationTokenSource();
        _sequence = cts;
        _sequenceActive = true;
        IsMoving = true;
        try
        {
            MoveToFlange(pos1);
        }
        catch
        {
            _sequenceActive = false;
            throw;
        }
        _ = Task.Run(() => RunDwell(pos2, durationSeconds, cts.Token));
    }

    public override void AbortMotion()
    {
        CancelSequence();
        base.AbortMotion();
    }

    public override void ReadPosition()
    {
        try
        {
            Poll();
            IsResponding = true;
            LastCommFault = null;
        }
        catch (Exception ex)
        {
            Fail($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private void Poll()
    {
        string position;
        string busy;
        lock (_ioGate)
        {
            position = _socket.SendCommand("POS?").Trim();
            busy = _socket.SendCommand("BUSY?").Trim();
        }
        if (busy != "0" && busy != "1")
            throw new InvalidOperationException($"unparseable BUSY? reply '{busy}'");

        float current;
        if (string.Equals(position, "Home", StringComparison.OrdinalIgnoreCase))
            current = 0f;
        else if (int.TryParse(position, NumberStyles.None, CultureInfo.InvariantCulture, out int flange))
            current = flange;
        else
            throw new InvalidOperationException($"unparseable POS? reply '{position}'");

        CurrentPosition = current;
        IsMoving = busy == "1" || _sequenceActive;
    }

    private void SendPosition(string token, int flange)
    {
        lock (_ioGate)
        {
            string reply;
            try
            {
                _socket.Send($"POS {token}");
                reply = _socket.SendCommand("SYST:ERR?").Trim();
            }
            catch (Exception ex)
            {
                Fail($"'POS {token}' not delivered: {ex.GetType().Name}: {ex.Message}");
                throw;
            }
            int comma = reply.IndexOf(',');
            string codeText = comma < 0 ? reply : reply[..comma];
            if (!int.TryParse(codeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int code))
            {
                Fail($"'POS {token}' answered unusable error reply '{reply}'");
                throw new InvalidOperationException(
                    $"LanRotaryDrive[{Params?.ID}]: unusable error reply '{reply}'");
            }
            if (code != 0)
                throw new ArgumentException(
                    $"LanRotaryDrive[{Params?.ID}]: 'POS {token}' rejected by device: {reply}");
        }

        TargetPosition = flange;
        IsMoving = true;
    }

    private async Task RunDwell(int pos2, float durationSeconds, CancellationToken ct)
    {
        try
        {
            await WaitUntilIdle(ct).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(0f, durationSeconds)), ct).ConfigureAwait(false);
            MoveToFlange(pos2);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            Fail($"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            if (!ct.IsCancellationRequested)
                _sequenceActive = false;
        }
    }

    private async Task WaitUntilIdle(CancellationToken ct)
    {
        for (int waited = 0; waited < ArrivalTimeoutMs; waited += 50)
        {
            string busy;
            lock (_ioGate)
                busy = _socket.SendCommand("BUSY?").Trim();
            if (busy == "0")
                return;
            await Task.Delay(50, ct).ConfigureAwait(false);
        }
        throw new TimeoutException("drive did not arrive at the first deposit position");
    }

    private void CancelSequence()
    {
        var cts = Interlocked.Exchange(ref _sequence, null);
        if (cts == null)
            return;
        _sequenceActive = false;
        cts.Cancel();
        cts.Dispose();
    }

    private void Fail(string fault)
    {
        IsResponding = false;
        LastCommFault = fault;
        System.Diagnostics.Trace.TraceWarning($"LanRotaryDrive[{Params?.ID}] {fault}");
    }
}
