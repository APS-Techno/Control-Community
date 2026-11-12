using APS.SystemControl.Core;
using APS.SystemControl.Core.HWClasses.Base;

namespace APS.SystemControl.BaseHWClasses;

public class BaseMFC : IBaseComponent
{
    protected bool _connected;

    public BaseMFC(IBaseDeviceParams Params)
    {
        this.Params = Params;
        this.Params.Type = DeviceType.MFC;
    }

    public IBaseDeviceParams Params { get; private set; }

    /// <summary>
    /// Chamber this MFC belongs to. Set during hardware load from the
    /// <c>chamber="..."</c> attribute; defaults to
    /// <see cref="CoreConstants.DefaultChamberId"/> ("Main") when absent.
    /// Consulted by per-chamber group-transition evaluation so a chamber
    /// reacts only to its own gas flow.
    /// </summary>
    public string Chamber { get; set; } = CoreConstants.DefaultChamberId;

    /// <summary>Inlet valves from the optional <c>valve="V2,V3"</c> attribute; gas passes while ANY is open. Empty = ungated.</summary>
    public IReadOnlyList<string> GateValves { get; set; } = [];

    public bool Connected { get => _connected; set { _connected = value; } }

    public void Poll() { if (_connected) { ReadFlow(); ReadSetpoint(); } }

    public float ReadFlow()
    {
        float v = ReadHW();
        // A non-working MFC's PLC register decodes to denormalized garbage (~1e-38),
        // which otherwise logs as e.g. "1.4E-38" and feeds the UI + group-transition
        // logic. No real sccm reading approaches that floor; treat junk as no flow.
        LastFlowValid = float.IsFinite(v) && (v == 0f || MathF.Abs(v) >= 1e-20f);
        if (!LastFlowValid)
            v = 0f;
        LastFlow = v;
        return LastFlow;
    }

    /// <summary>
    /// False when the most recent <see cref="ReadFlow"/> got a NaN/Infinity or
    /// denormalized-garbage register value that was sanitized to 0. Safety checks
    /// must treat that 0 as "unknown", not as a genuine zero flow.
    /// </summary>
    public bool LastFlowValid { get; private set; }

    /// <summary>
    /// Last flow read by the background poll. UI widgets must read this instead
    /// of <see cref="ReadFlow"/> — pure in-memory, no PLC I/O, so it cannot
    /// block the UI thread and keeps the PLC socket single-reader (poll only).
    /// </summary>
    public float LastFlow { get; private set; }

    /// <summary>
    /// Last successfully written setpoint, cached on the C# side. Stays at 0f
    /// until SetFlow succeeds. Consulted by group-transition evaluation.
    ///
    /// This is what CONTROL commanded, not what the device holds — after a
    /// Control restart it reads 0 even while the MFC is flowing. For the
    /// device's own setpoint use <see cref="LastSetpoint"/>.
    /// </summary>
    public float Setpoint { get; private set; }

    /// <summary>
    /// Setpoint read back from the hardware by the background poll — the value
    /// the device is actually holding, independent of whether this Control
    /// session wrote it. Pure in-memory access; no PLC I/O.
    /// </summary>
    public float LastSetpoint { get; private set; }

    /// <summary>
    /// Reads the device's own setpoint register. Applies the same
    /// denormalized-garbage floor as <see cref="ReadFlow"/>: a dead register
    /// decodes to ~1e-38 and must not surface as a real setpoint.
    /// </summary>
    public float ReadSetpoint()
    {
        float v = ReadSetpointHW();
        if (float.IsNaN(v) || float.IsInfinity(v) || MathF.Abs(v) < 1e-20f)
            v = 0f;
        LastSetpoint = v;
        return LastSetpoint;
    }

    /// <summary>
    /// Hardware read of the setpoint register. Default returns 0 for devices
    /// with no readback; PLC-backed MFCs override it.
    /// </summary>
    protected virtual float ReadSetpointHW() => 0;

    /// <summary>
    /// False when the most recent <see cref="SetFlow"/>'s verified PLC write failed
    /// after all retries. Reset to true at the start of each SetFlow and cleared by
    /// the driver's verified-write failure path (RaiseWriteFailed). Recipe MFC ops
    /// consult this to abort instead of reporting a failed setpoint as success. A
    /// non-PLC <see cref="BaseMFC"/> never fails verification, so it stays true.
    /// </summary>
    public bool LastWriteVerified { get; protected set; } = true;

    /// <summary>
    /// Raised when <see cref="SetFlow"/> successfully changes <see cref="Setpoint"/>.
    /// Core subscribes to re-evaluate Processing&#x2194;Idle group transitions.
    /// </summary>
    public event Action<BaseMFC>? SetpointChanged;

    public virtual void SetFlow(float value)
    {
        if (!_connected) return;
        LastWriteVerified = true;
        WriteHW(value);
        // A failed verified PLC write (RaiseWriteFailed cleared the flag
        // synchronously inside WriteHW) must not cache the setpoint or fire
        // SetpointChanged — a fabricated value would otherwise feed the
        // Idle->Processing group-transition logic.
        if (!LastWriteVerified) return;
        if (Setpoint == value) return;
        Setpoint = value;
        SetpointChanged?.Invoke(this);
    }

    protected virtual float ReadHW() => 0;

    protected virtual void WriteHW(float newvalue) { }
}

public class MFCFactory : IDeviceFactory
{
    public IBaseComponent CreateDevice(IBaseDeviceParams Params) => new BaseMFC(Params);
}
