using APS.SystemControl.Core;
using APS.SystemControl.Core.HWClasses.Base;

namespace APS.SystemControl.BaseHWClasses;
public enum ValveState { Closed, Open, Undefined }

public class BaseValve : IBaseComponent
{
    private bool _connected;

    public BaseValve(IBaseDeviceParams Params)
    {
        this.Params = Params;
        this.Params.Type = DeviceType.Valve;
    }

    public IBaseDeviceParams Params { get; private set; }
    public bool IsOpen => State == ValveState.Open;
    public virtual bool IsGasValve => false;

    /// <summary>
    /// Last open/closed state seen by the background poll. UI widgets must read
    /// this instead of <see cref="IsOpen"/>/<see cref="State"/> — pure in-memory,
    /// no PLC I/O, so it cannot block the UI thread and keeps the PLC socket
    /// single-reader (poll thread only). Refreshed by <see cref="ReadState"/> and,
    /// on the bulk poll path that never calls ReadState, by <see cref="CacheOpen"/>.
    /// </summary>
    public bool LastOpen { get; private set; }

    /// <summary>
    /// Records the open state read on the bulk poll path (Core.ReadValvesBulk),
    /// which reads raw bits in one round-trip and never calls <see cref="ReadState"/>.
    /// Keeps <see cref="LastOpen"/> fresh for the UI without an extra PLC read.
    /// </summary>
    public void CacheOpen(bool open) => LastOpen = open;

    /// <summary>
    /// Chamber this valve belongs to. Set during hardware load from the
    /// <c>chamber="..."</c> attribute; defaults to
    /// <see cref="CoreConstants.DefaultChamberId"/> ("Main") when absent.
    /// Consulted by per-chamber group-transition evaluation so a chamber
    /// reacts only to its own gas valves.
    /// </summary>
    public string Chamber { get; set; } = CoreConstants.DefaultChamberId;

    public ValveState State => ReadState();

    /// <summary>
    /// False when the most recent open/close command's verified PLC write failed
    /// after all retries. Reset to true at the start of every <see cref="ProcessCommand"/>
    /// and cleared by the driver's verified-write failure path (RaiseWriteFailed).
    /// Recipe valve ops consult this to abort instead of reporting a failed write
    /// as success. A non-PLC <see cref="BaseValve"/> never fails verification, so
    /// it stays true and behaviour is unchanged for those.
    /// </summary>
    public bool LastWriteVerified { get; protected set; } = true;

    /// <summary>
    /// How long, in ms, the valve op may wait after a command for the valve's
    /// position read-back to agree with the commanded state. 0 (the default)
    /// means actuation is synchronous with the write — the state read straight
    /// after <see cref="ProcessCommand"/> is authoritative. Drivers whose command
    /// is a fire-and-forget pulse and whose state is a slow position sensor
    /// (a machine's panel gate valves) return a positive value so a recipe
    /// does not advance to a step whose interlock reads that sensor live.
    /// </summary>
    public virtual int SettleTimeoutMs => 0;

    public const int SettlePollMs = 200;

    /// <summary>
    /// Waits until the position read-back agrees with <paramref name="targetOpen"/>,
    /// for valves that declare a positive <see cref="SettleTimeoutMs"/>. Returns at
    /// once for a synchronous valve (settle time 0) and for a valve already in
    /// position. EVERY recipe valve op must call this after the command — the core
    /// op (<c>Core.OpValve</c>) and the worker ops (<c>Operations.Valve</c> in a worker)
    /// alike: Core.LoadWorker replaces the core op with a worker op of the same name, so a
    /// wait that lived only in Core.OpValve would never run on a machine whose worker
    /// declares its own valve op.
    /// </summary>
    /// <exception cref="InvalidOperationException">The sensor never agreed within the settle timeout; names the valve.</exception>
    /// <exception cref="OperationCanceledException">Recipe aborted.</exception>
    public async Task SettleAsync(bool targetOpen, CancellationToken ct, Action<string>? log = null)
    {
        if (SettleTimeoutMs <= 0 || IsOpen == targetOpen)
            return;
        string id = Params?.ID ?? "?";
        string target = targetOpen ? "open" : "closed";
        log?.Invoke($"{id}: waiting for position sensor to report {target}");
        try
        {
            await global::APS.SystemControl.Core.Classes.ValveSettleWaiter.WaitAsync(
                () => IsOpen, targetOpen, SettlePollMs, SettleTimeoutMs, ct).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            throw new InvalidOperationException($"Valve {id} {ex.Message}", ex);
        }
    }

    public bool Connected
    {
        get => _connected;
        set => _connected = value;
    }

    public virtual void Open() => ProcessCommand(ValveState.Open);

    public virtual void Close() => ProcessCommand(ValveState.Closed);

    public void Poll() { if (_connected) ReadState(); }

    public ValveState ReadState()
    {
        if (_connected)
        {
            bool open = ReadHW();
            LastOpen = open;
            return open ? ValveState.Open : ValveState.Closed;
        }
        else
            return ValveState.Undefined;
    }

    public ValveState ProcessCommand(ValveState newState)
    {
        LastWriteVerified = true;
        WriteHW(newState == ValveState.Open);
        return ReadState();
    }

    protected virtual bool ReadHW() => false;

    protected virtual void WriteHW(bool newstate) { }
}


public class ValveFactory : IDeviceFactory
{
    public IBaseComponent CreateDevice(IBaseDeviceParams Params) => new BaseValve(Params);
}