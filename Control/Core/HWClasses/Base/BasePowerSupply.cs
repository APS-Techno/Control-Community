using System.Diagnostics;
using APS.SystemControl.Core;
using APS.SystemControl.Core.HWClasses.Base;

namespace APS.SystemControl.BaseHWClasses;

/// <summary>
/// Implemented by a power supply that is a multi-channel substrate-bias analyzer
/// rather than a single-output sputter-cathode PSU.
/// Lets Core distinguish the two — and read per-channel measurements — without
/// referencing the concrete driver, which lives in a plugin DLL loaded via
/// <c>Assembly.LoadFrom</c> and is therefore not nameable from the Core assembly.
/// Channel indices are 0-based.
/// </summary>
public interface IBiasSupply
{
    int ChannelCount { get; }
    /// <summary>Last measured voltage (V) on the 0-based channel.</summary>
    float ChannelVoltage(int channelIndex);
    /// <summary>Last measured current (A) on the 0-based channel.</summary>
    float ChannelCurrent(int channelIndex);
}

public class BasePowerSupply : IBaseComponent, ISelfTestable
{
    private bool _connected;

    public BasePowerSupply(IBaseDeviceParams deviceParams)
    {
        Params = deviceParams;
        Params.Type = DeviceType.Power;
    }

    public IBaseDeviceParams Params { get; private set; }

    public bool Connected { get => _connected; set => _connected = value; }

    public float Voltage { get; protected set; }
    public float Current { get; protected set; }
    public float Power { get; protected set; }

    /// <summary>Unit of <see cref="Current"/>. The DC and pulsed-DC magnetron supplies report mA;
    /// a driver whose readback is in amperes overrides this.</summary>
    public virtual string CurrentUnit => "mA";

    public float SetVoltage { get; set; }
    public float SetCurrent { get; set; }

    /// <summary>
    /// The supply's own power setpoint (W) — write it back in <c>ApplySetpoint("Power", …)</c>
    /// whenever <see cref="SupportsPowerSetpoint"/> is true. A driver that polls the setpoint from
    /// the supply's own status reply / register has a genuine hardware READBACK, re-read on every
    /// poll — not merely a local echo of the last command.
    /// <para><b>This property is load-bearing for a safety decision, not just telemetry.</b> The
    /// fault-pause resume (<c>CoreThread.ReEnergiseAfterCut</c>, Ruling N) reads it to tell a
    /// genuine operator hand-repair apart from a supply merely latched ON at a zeroed setpoint:
    /// <c>handRepaired = psu.IsOutputOn &amp;&amp; (!SupportsPowerSetpoint || SetPower > 0.01f)</c>.
    /// <see cref="IsOutputOn"/> alone cannot make that call — it is a pure command echo that goes
    /// true over 0 W just as readily as over a real repair. A driver that accepts "Power" (i.e.
    /// <see cref="SupportsPowerSetpoint"/> is true) but never writes this property back makes
    /// every hand repair on that supply look like a fake one: the resume "corrects" the
    /// operator's own fix back to the recipe's setpoint. A driver with no scalar power setpoint at all should override
    /// <see cref="SupportsPowerSetpoint"/> to <c>false</c> — which routes around this property
    /// entirely — rather than leaving this at a permanent, misleading zero.</para>
    /// </summary>
    public float SetPower { get; set; }

    public bool IsOutputOn { get; protected set; }
    public bool PulseMode { get; set; }
    private volatile bool _isResponding;
    public bool IsResponding { get => _isResponding; protected set => _isResponding = value; }

    /// <summary>
    /// Spark-detect / speed-detect mode. Supplies that offer it override; default no-op
    /// keeps the dispatcher in CoreThread free of driver-specific casts.
    /// </summary>
    public virtual bool SpeedDetectMode { get; set; }

    /// <summary>
    /// Software ramp-enable toggle for supplies that ramp power gradually.
    /// Independent of the configured ramp duration so the
    /// operator can disable ramping for a single run without losing the
    /// timing setup. Default no-op for supplies that don't ramp.
    /// </summary>
    public virtual bool RampEnabled { get; set; }

    /// <summary>
    /// Called by the poll watchdog when ReadFeedback exceeds its hard budget.
    /// Flags the device so the UI stops treating stale measurements as fresh.
    /// </summary>
    public void MarkUnresponsive() => IsResponding = false;

    /// <summary>
    /// Why the last feedback read failed, or null while the supply is answering.
    ///
    /// <see cref="IsResponding"/> alone says a supply went quiet but not how,
    /// and drivers wrote that detail to <c>Trace</c>, which no operator sees —
    /// so a failed power-ON read the same whether the port was closed, the reply
    /// timed out, or the supply answered something unparseable. Drivers set this
    /// alongside IsResponding; <c>Operations.Power</c> quotes it when it refuses
    /// to confirm an energise.
    ///
    /// A quiet supply is only a fault on the ON path. On power-OFF it is usually
    /// just a supply whose mains switch is off, and that is not an error — see
    /// the asymmetry documented in <c>Operations.Power</c>.
    /// </summary>
    public string? LastCommFault { get; protected set; }

    public void Poll() { if (Connected) ReadFeedback(); }

    /// <summary>
    /// Confirms the supply is answering before a worker declares it dead.
    /// <see cref="IsResponding"/> is a single-instant flag: one late poll reply
    /// or a PollWatchdog budget overrun under load flips it false even though
    /// the link is fine, and sampling it once can abort an otherwise-finished
    /// recipe at its teardown step. Drives direct
    /// <see cref="ReadFeedback"/> reads and re-checks after each delay so a
    /// recovery by the concurrent poll loop also counts (drivers with an
    /// in-flight guard turn the direct read into a no-op). Returns true as soon
    /// as the supply answers; false when it stays silent through all attempts.
    /// </summary>
    public async Task<bool> ConfirmRespondingAsync(
        int retries = 2, int retryDelayMs = 300, CancellationToken ct = default)
    {
        for (int i = 0; i < retries; i++)
        {
            if (IsResponding) return true;
            ReadFeedback();
            if (IsResponding) return true;
            await Task.Delay(retryDelayMs, ct).ConfigureAwait(false);
        }
        return IsResponding;
    }

    /// <summary>
    /// Read-only liveness probe: re-reads feedback (the same exchange the 200 ms
    /// poll performs) and reports Alive iff the supply parsed a valid reply.
    /// State-preserving — touches no setpoint/output. Drivers with a richer
    /// identity query override to add it.
    /// </summary>
    public virtual DeviceProbeResult Probe()
    {
        var sw = Stopwatch.StartNew();
        ReadFeedback();
        sw.Stop();
        return IsResponding
            ? DeviceProbeResult.Good(sw.Elapsed.TotalMilliseconds)
            : DeviceProbeResult.Bad(HealthLevel.Unresponsive,
                "no valid feedback reply", sw.Elapsed.TotalMilliseconds);
    }

    /// <summary>
    /// Rejects NaN/Infinity and clamps the value to [0, <paramref name="max"/>].
    /// Shared by all drivers that convert a float setpoint into a register or
    /// bitstream write — catches order-of-magnitude typos and negative values
    /// that would wrap catastrophically through an unsigned cast.
    /// </summary>
    protected static float SanitizeSetpoint(float value, float max, string name)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            throw new ArgumentOutOfRangeException(name, value,
                $"{name} setpoint must be a finite number");
        return Math.Clamp(value, 0f, max);
    }

    public virtual bool Connect() => false;

    /// <summary>
    /// Forces a hard reconnect — closes and reopens the underlying transport.
    /// Used by menu contributors and the recipe system to recover from
    /// persistent connection failures that the auto-retry path has not resolved.
    /// Base implementation simply calls Connect(); concrete drivers override
    /// to call Disconnect() first. Returns <c>true</c> if the new connection
    /// opened successfully.
    /// </summary>
    public virtual bool Reconnect() => Connect();

    /// <summary>
    /// Releases the underlying transport (serial port / TCP socket) and
    /// flips Connected to false. Override in drivers that own a port.
    /// Idempotent — safe to call when already disconnected.
    /// </summary>
    public virtual void Disconnect() { Connected = false; }

    public virtual void ClearFault() { }
    public virtual void OutputOn() { WriteOutputState(true); IsOutputOn = true; }
    public virtual void OutputOff() { WriteOutputState(false); IsOutputOn = false; }
    /// <summary>
    /// Enables output on a specific channel (1-based).
    /// Base implementation delegates to the single-channel OutputOn().
    /// Multi-channel supplies override this.
    /// </summary>
    public virtual void OutputOn(int channel) => OutputOn();
    /// <summary>
    /// Disables output on a specific channel (1-based).
    /// Base implementation delegates to the single-channel OutputOff().
    /// Multi-channel supplies override this.
    /// </summary>
    public virtual void OutputOff(int channel) => OutputOff();
    public virtual void ReadFeedback() { }  // Called by polling loop, updates Voltage/Current/Power
    public virtual void ApplySetpoint(string parameter, float value) { }

    /// <summary>
    /// The parameter names THIS driver's <see cref="ApplySetpoint"/> accepts —
    /// exactly the strings its switch matches, aliases included, case-sensitive.
    /// Core feeds the list to the recipe validator so `set &lt;psu&gt; &lt;name&gt; &lt;v&gt;`
    /// with a name the driver would throw on (`Freq` on a driver without that setpoint, `Pulse` on an RF
    /// head) is rejected at validation instead of pausing a chamber run.
    /// Empty means "not declared": the
    /// validator then leaves the device's parameter names unchecked. Keep it
    /// next to the switch and covered by PsuSetpointNamesTests.
    /// </summary>
    public virtual IReadOnlyCollection<string> SetpointParameterNames => [];

    /// <summary>
    /// The setpoint the device itself reported for <paramref name="parameter"/> (a name from
    /// <see cref="SetpointParameterNames"/>); false when the driver has not read it.
    /// </summary>
    public virtual bool TryGetSetpoint(string parameter, out float value)
    {
        value = 0;
        return false;
    }

    /// <summary>
    /// Whether <c>ApplySetpoint("Power", w)</c> is a name THIS driver accepts — i.e. whether the
    /// supply has a scalar power setpoint at all. True for every sputter supply; false for
    /// supplies that regulate by other quantities and reject the name on purpose: a bias
    /// analyser (Voltage / Current / Frequency / DutyCycle / Delay / Mode / RunArb) and an
    /// ion source (Current / Frequency / DutyCycle — a routine deposition participant, not a
    /// bench instrument).
    /// <para><b>Why this exists as a property rather than a try/catch.</b> The fault-pause
    /// restore path (<c>CoreThread.ReEnergiseAfterCut</c>) and the soft hold's zeroing
    /// (<c>CoreThread.RunFailureHold</c>) both need to know, BEFORE they act, whether "Power"
    /// means anything here. Probing by catching <see cref="ArgumentException"/> would work and is
    /// wrong twice over: <c>ApplySetpoint</c> throwing on an unknown name is deliberate and
    /// load-bearing (a recipe typo must abort the run rather than deposit with the setpoint
    /// unapplied), so overloading it as a capability probe makes a
    /// safety-path reader unable to tell a typo from a supply shape; and exception-driven control
    /// flow on the de-energise path is exactly what a later reader misreads. Override this next
    /// to the <c>switch</c> that rejects the name, so the two can never drift apart.</para>
    /// <para>A supply that answers false is NOT excluded from restoration. Its OUTPUT is still
    /// restored — that is the load-bearing half — and the caller says plainly that there was no
    /// setpoint to put back. Refusing a resume merely because a supply has no Power setpoint
    /// stranded the ion source in a pause only Skip or Abort could leave.</para>
    /// </summary>
    public virtual bool SupportsPowerSetpoint => true;
    protected virtual void WriteOutputState(bool on) { }

    public virtual object[] GetReadback() => [Voltage, Current, Power, IsOutputOn];
}

public class PowerSupplyFactory : IDeviceFactory
{
    public IBaseComponent CreateDevice(IBaseDeviceParams deviceParams) => new BasePowerSupply(deviceParams);
}
