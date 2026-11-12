using APS.SystemControl.Core.HWClasses.Base;

namespace APS.SystemControl.BaseHWClasses;

/// <summary>
/// Implemented by a controller that measures chamber pressure itself and
/// regulates it through a throttle valve (a gauge controller).
/// Lets Core log its readings without referencing the concrete driver, which
/// lives in a plugin DLL loaded via <c>Assembly.LoadFrom</c> and is therefore not
/// nameable from the Core assembly — same reason as <see cref="IBiasSupply"/>.
///
/// During a deposition this IS the process-pressure measurement: the ion gauge
/// ("pm") is off scale once process gas is admitted, so a dataset without these
/// columns has no record of the pressure the film actually grew at.
/// Both properties read the driver's poll cache — never the wire.
/// </summary>
public interface IPressureController
{
    /// <summary>Last measured pressure in Torr, converted from the gauge's native unit.</summary>
    float PressureTorr { get; }
    /// <summary>Last measured throttle-valve position, 0–100 %.</summary>
    float ValvePositionPercent { get; }
}

public class BaseController : IBaseComponent, ISelfTestable
{
    protected bool _connected;
    protected string _errorMessage = "";

    public BaseController(IBaseDeviceParams Params)
    {
        this.Params = Params;
        this.Params.Type = DeviceType.Controller;
    }

    public bool Connected { get => _connected; set { _connected = value; } }
    public string ErrorMessage { get => _errorMessage; }
    public IBaseDeviceParams Params { get; private set; }
    public virtual object[] Measure() { return []; }

    /// <summary>
    /// Last value returned by <see cref="Measure"/>, refreshed by <see cref="Poll"/>.
    /// The coordinator/UI read THIS (no I/O); only the bus poller calls Measure().
    /// </summary>
    public object[] LastMeasurement { get; private set; } = [];

    public void Poll() { if (_connected) LastMeasurement = Measure(); }

    public virtual void Command(object[] args) { }
    public virtual bool Connect() { return false; }
    public virtual void Disconnect() { }

    /// <summary>
    /// Read-only liveness probe: re-reads the controller's measurement (the same
    /// call the 200 ms poll makes) and reports Alive iff it returned values.
    /// </summary>
    public virtual DeviceProbeResult Probe()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        object[] m = Measure();
        sw.Stop();
        return m.Length > 0
            ? DeviceProbeResult.Good(sw.Elapsed.TotalMilliseconds,
                detail: string.Join(", ", m))
            : DeviceProbeResult.Bad(HealthLevel.Unresponsive,
                "no measurement reply", sw.Elapsed.TotalMilliseconds);
    }
}

