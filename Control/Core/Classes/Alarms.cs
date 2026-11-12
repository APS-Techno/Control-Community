namespace APS.SystemControl.Core.Classes;

internal record struct Alarm(
    string Sensor, float Value, int Condition, SystemState State, string Message,
    // Optional guard: when GuardValve is set and its open-state equals
    // GuardValveOpen, the alarm is suppressed (e.g. unless="LL_A1 open" — a
    // controlled vent raises pressure intentionally and must not alarm).
    string? GuardValve = null, bool GuardValveOpen = false,
    // Optional settling delay in seconds (delay="60"). The condition must hold
    // CONTINUOUSLY for this long before the alarm is raised; if it clears in
    // the meantime the timer restarts. 0 = fire on the first matching sample
    // (the behaviour of every rule that does not set it).
    //
    // Exists because a chamber leaves `processing` the instant the last gas
    // valve closes, while still at process pressure — so an idle-state
    // over-pressure rule fired on every normal recipe completion and left a
    // sticky alarm for the operator to reset.
    float DelaySeconds = 0f);

/// <summary>
/// Evaluated every poll cycle: if sensor value meets condition, transition fires.
/// </summary>
internal record struct SensorTransition(SystemState From, SystemState To, string Sensor, int Condition, float Value);

/// <summary>
/// A device-status-based alarm rule. Fires when <c>device.Status[StatusBit]</c>
/// is true and the system is in <c>State</c>. Status arrays come from devices
/// that implement <see cref="HWClasses.Base.IStatusBitNamed"/>.
/// </summary>
internal record struct DeviceStatusAlarm(
    string DeviceId, int StatusBit, SystemState State, string Message);

/// <summary>
/// Evaluated after valve operations: if valve reaches target state and all requirements are met, transition fires.
/// </summary>
internal record struct ValveTransition(SystemState From, SystemState To, string ValveId, bool ValveOpen, string[] Requirements);

/// <summary>
/// Kinds of device group for <see cref="GroupTransition"/>.
/// GasValve = valves of runtime type S7200GasValve.
/// Gas = gas valves plus MFCs (for "all off" rules that must also include flow).
/// </summary>
internal enum GroupKind { GasValve, Gas }

/// <summary>
/// Conditions evaluated against a device group.
/// AnyOpen fires if at least one member is open/nonzero.
/// AllOff fires if every member is closed/zero.
/// </summary>
internal enum GroupCondition { AnyOpen, AllOff }

/// <summary>
/// A group-based state transition rule. Evaluated after any event that could
/// change the group's aggregate state (gas-valve toggle, MFC setpoint change,
/// recipe end).
/// </summary>
internal record struct GroupTransition(SystemState From, SystemState To, GroupKind Group, GroupCondition Condition);

internal class Alarms
{
    private readonly List<Alarm> alarms = [];
    private readonly List<SensorTransition> sensorTransitions = [];
    private readonly List<ValveTransition> valveTransitions = [];
    private readonly List<GroupTransition> groupTransitions = [];
    // Concurrent: the HW watchdog respawn cancels but does NOT await the old
    // coordinator, so a replacement can briefly run alongside a still-draining
    // old one — both calling CheckSensorTransitions on the shared per-chamber
    // instance. A plain Dictionary can corrupt its buckets (or spin forever)
    // under concurrent writes on the safety-critical alarm path (audit C18).
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, float> lastSensorSample = new();

    /// <summary>
    /// When each delayed rule's condition first became true, keyed by rule.
    /// Concurrent for the same reason as <see cref="lastSensorSample"/>: an old
    /// and a respawned poll coordinator can briefly overlap on this instance.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Alarm, DateTime> pendingSince = new();

    internal Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    /// <summary>
    /// One instance per <c>&lt;chamber id="..."&gt;</c> in <c>Alarms.xml</c>;
    /// flat configs use <see cref="CoreConstants.DefaultChamberId"/> ("Main").
    /// </summary>
    public string ChamberId { get; }

    public Alarms() : this(CoreConstants.DefaultChamberId) { }

    public Alarms(string chamberId)
    {
        ChamberId = chamberId;
    }

    /// <summary>
    /// Optional runtime override registry. When set, Check() consults it
    /// before reporting an alarm; overridden rules are skipped and a
    /// [SUPPRESSED] line is written via the registry's logger.
    /// </summary>
    public OverrideRegistry? Overrides { get; set; }

    public string AlarmMessage { get; private set; } = string.Empty;

    /// <summary>
    /// Relative equality tolerance for the condition==0 ("equal to") check.
    /// float.Epsilon (~1.4e-45) is too small to ever match sensor values.
    /// Use 1e-3 relative or 1e-6 absolute, whichever is larger.
    /// </summary>
    private const float EqualityAbsoluteFloor = 1e-6f;
    private const float EqualityRelativeTolerance = 1e-3f;
    private static float EqualityTolerance(float reference)
        => Math.Max(EqualityAbsoluteFloor, Math.Abs(reference) * EqualityRelativeTolerance);

    public IReadOnlyList<Alarm> Rules => alarms;
    public IReadOnlyList<SensorTransition> SensorTransitions => sensorTransitions;
    public IReadOnlyList<ValveTransition> ValveTransitions => valveTransitions;
    public IReadOnlyList<GroupTransition> GroupTransitions => groupTransitions;

    private readonly List<DeviceStatusAlarm> deviceStatusAlarms = [];

    public IReadOnlyList<DeviceStatusAlarm> DeviceStatusRules => deviceStatusAlarms;

    public void AddDeviceStatusAlarm(string deviceId, int statusBit,
        SystemState state, string message)
    {
        deviceStatusAlarms.Add(new DeviceStatusAlarm(deviceId, statusBit, state, message));
    }

    /// <summary>
    /// Returns true if any rule fires; sets <see cref="AlarmMessage"/> to the
    /// matching rule's message. Out-of-range <c>StatusBit</c> values are ignored.
    /// </summary>
    public bool CheckDeviceStatus(SystemState state, string deviceId, bool[] status)
    {
        bool result = false;
        foreach (var rule in deviceStatusAlarms)
        {
            if (rule.DeviceId != deviceId || rule.State != state) continue;
            if (rule.StatusBit < 0 || rule.StatusBit >= status.Length) continue;
            if (!status[rule.StatusBit]) continue;

            if (Overrides != null)
            {
                string id = OverrideIds.DeviceStatus(ChamberId, rule.DeviceId, rule.StatusBit, rule.State);
                if (Overrides.IsAlarmOverridden(id))
                {
                    Overrides.LogSuppressedAlarm(id, rule.Message);
                    continue;
                }
            }

            result = true;
            AlarmMessage = rule.Message;
        }
        return result;
    }

    public void Add(string sensor, float value, int condition, SystemState state, string message,
                    string? guardValve = null, bool guardValveOpen = false,
                    float delaySeconds = 0f)
    {
        alarms.Add(new Alarm(sensor, value, condition, state, message, guardValve, guardValveOpen,
                             delaySeconds));
    }

    public void AddSensorTransition(SystemState from, SystemState to, string sensor, int condition, float value)
    {
        sensorTransitions.Add(new SensorTransition(from, to, sensor, condition, value));
    }

    public void AddValveTransition(SystemState from, SystemState to, string valveId, bool valveOpen, string[] requirements)
    {
        valveTransitions.Add(new ValveTransition(from, to, valveId, valveOpen, requirements));
    }

    /// <summary>
    /// Registers a group-based state transition. Evaluated by
    /// <see cref="CheckGroupTransition"/> whenever the group's aggregate state may
    /// have changed (gas-valve toggle, MFC setpoint change, recipe end).
    /// </summary>
    public void AddGroupTransition(SystemState from, SystemState to, GroupKind group, GroupCondition condition)
    {
        groupTransitions.Add(new GroupTransition(from, to, group, condition));
    }

    /// <summary>
    /// Checks group-based transitions. Returns the new state if a transition fires, null otherwise.
    /// The evaluator callback answers whether a (group, condition) pair is currently satisfied,
    /// given the live hardware state — kept external so Alarms has no hardware dependencies.
    /// </summary>
    public SystemState? CheckGroupTransition(SystemState current, Func<GroupKind, GroupCondition, bool> evaluator)
    {
        foreach (var t in groupTransitions)
        {
            if (t.From != current) continue;
            if (evaluator(t.Group, t.Condition))
                return t.To;
        }
        return null;
    }

    public bool Check(SystemState state, string sensor, float value,
                      Func<string, bool?>? valveStateResolver = null)
    {
        bool result = false;
        List<Alarm> sensor_alarms = alarms.FindAll(alarm => alarm.Sensor == sensor);

        foreach (Alarm alarm in sensor_alarms)
        {
            bool fires = alarm.Condition switch
            {
                -1 => value < alarm.Value,
                 1 => value > alarm.Value,
                 _ => Math.Abs(value - alarm.Value) <= EqualityTolerance(alarm.Value),
            };

            // A rule that is out of state, or whose condition is not met, has
            // its settling timer cleared: the next qualifying sample starts a
            // fresh window rather than inheriting a stale one.
            if (state != alarm.State || !fires)
            {
                if (alarm.DelaySeconds > 0f) pendingSince.TryRemove(alarm, out _);
                continue;
            }

            // Vent guard: suppress while the named valve is in the guard state
            // (e.g. unless="LL_A1 open" — venting is an intentional pressure rise,
            // not an over-pressure fault). If the valve can't be resolved, err
            // toward firing.
            if (alarm.GuardValve != null && valveStateResolver != null)
            {
                bool? guardState = valveStateResolver(alarm.GuardValve);
                if (guardState == alarm.GuardValveOpen)
                {
                    if (alarm.DelaySeconds > 0f) pendingSince.TryRemove(alarm, out _);
                    continue;
                }
            }

            // Settling delay: the condition must hold continuously for
            // DelaySeconds before the alarm is raised. Suppresses the transient
            // that every normal recipe produces when the chamber enters `idle`
            // still at process pressure, while a persistent fault still fires.
            if (alarm.DelaySeconds > 0f)
            {
                DateTime now = Clock();
                DateTime since = pendingSince.GetOrAdd(alarm, now);
                if ((now - since).TotalSeconds < alarm.DelaySeconds)
                    continue;
            }

            // Consult the override registry. If this rule is overridden,
            // log it as SUPPRESSED (rate-limited) and skip without setting
            // AlarmMessage — the caller will treat it as "no alarm this tick".
            if (Overrides != null)
            {
                string id = OverrideIds.Alarm(ChamberId, alarm.Sensor, alarm.State, alarm.Condition, alarm.Value);
                if (Overrides.IsAlarmOverridden(id))
                {
                    Overrides.LogSuppressedAlarm(id, alarm.Message);
                    continue;
                }
            }

            result = true;
            AlarmMessage = alarm.Message;
        }
        return result;
    }

    /// <summary>
    /// Checks sensor-based transitions. Returns the new state if a transition fires, null otherwise.
    /// Edge-triggered: a rule fires only when the condition was NOT met at the previous sample for
    /// this sensor — prevents re-entering a From state whose condition is already level-true from
    /// immediately bouncing back (e.g. recipe sets state=startup while pm is still under vacuum).
    /// </summary>
    public SystemState? CheckSensorTransitions(SystemState current, string sensor, float value)
    {
        bool hadPrev = lastSensorSample.TryGetValue(sensor, out float prev);
        SystemState? result = null;

        foreach (var t in sensorTransitions)
        {
            if (t.From != current || t.Sensor != sensor)
                continue;

            if (!SensorConditionMet(t.Condition, value, t.Value))
                continue;

            if (hadPrev && SensorConditionMet(t.Condition, prev, t.Value))
                continue;

            if (result == null)
                result = t.To;
        }

        lastSensorSample[sensor] = value;
        return result;
    }

    private static bool SensorConditionMet(int condition, float value, float threshold) => condition switch
    {
        -1 => value < threshold,
         1 => value > threshold,
         _ => Math.Abs(value - threshold) <= EqualityTolerance(threshold),
    };

    /// <summary>
    /// Checks valve-action transitions. Returns the new state if a transition fires, null otherwise.
    /// The valveStateResolver callback returns true if the named valve is open.
    /// </summary>
    public SystemState? CheckValveTransition(SystemState current, string valveId, bool isOpen, Func<string, bool?> valveStateResolver)
    {
        foreach (var t in valveTransitions)
        {
            if (t.From != current || t.ValveId != valveId || t.ValveOpen != isOpen)
                continue;

            bool allMet = true;
            foreach (string req in t.Requirements)
            {
                // req format: "GVM closed" or "PP open"
                string[] parts = req.Split(' ', 2);
                if (parts.Length != 2)
                    continue;

                string reqValveId = parts[0];
                bool reqOpen = parts[1].Equals("open", StringComparison.OrdinalIgnoreCase);
                bool? actualState = valveStateResolver(reqValveId);

                if (actualState == null || actualState.Value != reqOpen)
                {
                    allMet = false;
                    break;
                }
            }

            if (allMet)
                return t.To;
        }
        return null;
    }
}
