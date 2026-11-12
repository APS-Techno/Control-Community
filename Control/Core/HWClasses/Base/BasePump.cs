using APS.SystemControl.Core;
using APS.SystemControl.Core.HWClasses.Base;

namespace APS.SystemControl.BaseHWClasses;

public class BasePump : IBaseComponent
{
    protected bool _connected;

    public BasePump(IBaseDeviceParams Params)
    {
        this.Params = Params;
        this.Params.Type = DeviceType.Pump;
    }

    public IBaseDeviceParams Params { get; private set; }

    /// <summary>
    /// Chamber this pump belongs to. Set during hardware load from the
    /// <c>chamber="..."</c> attribute; defaults to
    /// <see cref="CoreConstants.DefaultChamberId"/> ("Main"). Used to route the
    /// pump's device-status alarm to its own chamber's alarm rules.
    /// </summary>
    public string Chamber { get; set; } = CoreConstants.DefaultChamberId;

    public bool Connected { get => _connected; set { _connected = value; } }

    public void Poll() { if (_connected) _ = Status; }

    public bool[] Status { get { LastStatus = ReadStatus(); return LastStatus; } }

    /// <summary>
    /// Last status array read by the background poll. UI widgets must read this
    /// (and <see cref="LastRunning"/>) instead of <see cref="Status"/>: it is a
    /// pure in-memory access with no PLC I/O, so it cannot block the UI thread
    /// and keeps the PLC socket single-reader (poll thread only).
    /// </summary>
    public bool[] LastStatus { get; private set; } = [];

    /// <summary>
    /// Cached running flag, decoded from the last polled status — no PLC I/O.
    /// Shares <see cref="IsRunningFrom"/> with <see cref="IsRunning"/> so the
    /// cached and live answers can never disagree.
    /// </summary>
    public bool LastRunning => IsRunningFrom(LastStatus);

    /// <summary>
    /// True when the pump is considered running. Reads live status; prefer
    /// <see cref="LastRunning"/> on the UI thread.
    /// </summary>
    public virtual bool IsRunning => IsRunningFrom(Status);

    /// <summary>
    /// Decodes "is running" from a status array. Default is the first status bit
    /// (for S7200Pump, the echoed MB command bit). Pumps whose feedback needs a
    /// different predicate — e.g. a turbo, where bit 0 is the transient ACCEL
    /// composite that clears once at speed — override THIS, not
    /// <see cref="IsRunning"/>, so the cached path stays consistent.
    /// </summary>
    protected virtual bool IsRunningFrom(bool[] status) => status.Length > 0 && status[0];

    protected virtual bool[] ReadStatus() => [];

    public virtual void Power(bool state)
    {
        // Idempotent on: re-commanding a pump that is already running is a
        // no-op (avoids re-triggering spin-up / disturbing a running system).
        // 'off' is never suppressed.
        if (state && IsRunning)
            return;
        if (_connected)
            WriteHW(state);
    }

    protected virtual bool ReadHW() => false;

    protected virtual void WriteHW(bool newstate) { }
}

public class PumpFactory : IDeviceFactory
{
    public IBaseComponent CreateDevice(IBaseDeviceParams Params) => new BasePump(Params);
}
