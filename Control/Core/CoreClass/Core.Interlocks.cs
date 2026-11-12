using System.Globalization;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.Classes;
using APS.SystemControl.Core.HWClasses.Base;
using APS.SystemControl.XML;


namespace APS.SystemControl.Core;

public partial class Core
{
    private readonly Interlocks interlocks = new();

    /// <summary>Position tolerance (steps) for Stage interlock home checks.</summary>
    private const float StageHomeTolerance = 1.0f;

    /// <summary>
    /// Renders an interlock rejection for the operator: every device currently
    /// blocking, when <see cref="InterlockCond.Blockers"/> was collected by the
    /// failing check, otherwise just this condition's own device.
    /// </summary>
    public static string InterLockToString(InterlockCond cond)
    {
        return string.IsNullOrEmpty(cond.Blockers) ? cond.Target!.Params.ID : cond.Blockers;
    }

    /// <summary>
    /// Worker callback: re-evaluates the deposit guard immediately before a shutter is
    /// opened. Returns null when the deposit may proceed, otherwise the blocking reason.
    /// </summary>
    internal string? DepositGuardReason(BaseStepperDrive holder, string position, float seconds, string retract)
    {
        var blocked = CheckInterlocks("deposit", [holder, position, seconds, retract]);
        if (blocked == null) return null;
        string msg = $"deposit {position} blocked by interlock on {InterLockToString(blocked.Value)}";
        SendLogMesssage("Interlock", msg);
        return msg;
    }

    /// <summary>
    /// Resolve the registry key for an operation's interlock lookup.
    /// Tries the full key (<c>op_id_arg1_arg2...</c>) first; for stage
    /// operations whose continuous-valued position args cannot be
    /// enumerated as XML entries, falls back to a position-agnostic key
    /// (<c>op_id</c>) so one rule guards every target position. Returns
    /// the full key when nothing matches, so callers can use it for
    /// diagnostics.
    /// </summary>
    internal static string ResolveInterlockKey(Interlocks registry, string opName, object[] fArgs)
    {
        // The first arg is the bound device for most ops, but some ops take no
        // device (e.g. cluster 'deposit Cu 60', whose first arg is the material
        // string), so only an IBaseComponent first arg contributes its device ID.
        // Format every non-device arg with InvariantCulture: the registry keys are
        // built from culture-neutral XML text ("Stage_VS_-10.5"), so a current-culture
        // ToString() on ru-RU would produce "stage_vs_-10,5" and never match —
        // silently failing the interlock open (audit U-18).
        static string Inv(object? o) => o is IFormattable f
            ? f.ToString(null, CultureInfo.InvariantCulture)
            : o?.ToString() ?? "";

        string firstKey = fArgs.Length > 0
            ? (fArgs[0] is IBaseComponent comp ? comp.Params.ID : Inv(fArgs[0]))
            : "";
        string fullKey = opName + '_' + firstKey;
        for (int i = 1; i < fArgs.Length; i++)
        {
            if (fArgs[i] != null) fullKey += "_" + Inv(fArgs[i]);
        }
        fullKey = fullKey.ToLower();

        if (registry.Get(fullKey) != null) return fullKey;

        if (opName.Equals("deposit", StringComparison.OrdinalIgnoreCase) && fArgs.Length >= 2)
        {
            string? depositKey = fArgs[0] is IBaseComponent dev && fArgs[1] is string pos
                ? $"deposit_{dev.Params.ID}_{pos}".ToLower()
                : fArgs[0] is string source ? $"deposit_{source}".ToLower() : null;
            if (depositKey != null && registry.Get(depositKey) != null) return depositKey;
        }

        if (fArgs.Length >= 2 && fArgs[0] is BaseStage stage)
        {
            string stageKey = (opName + '_' + stage.Params.ID).ToLower();
            if (registry.Get(stageKey) != null) return stageKey;
        }
        return fullKey;
    }

    public InterlockCond? CheckInterlocks(string opName, object[] fArgs)
    {
        InterlockCond? result = null;
        // Every failing condition, in rule order, so one rejection can name them
        // all instead of making the operator clear blockers one at a time.
        List<string>? blockers = null;

        string key = ResolveInterlockKey(interlocks, opName, fArgs);
        var interlock = interlocks.Get(key);
        if (interlock != null && overrides != null && overrides.IsInterlockOverridden(key))
        {
            string label = $"interlock {key} bypassed";
            overrides.LogSuppressedInterlock(key, label);
            guiwriter.TryWrite(new ValMsg<string>(MsgType.OverrideFlash, DeviceType.System, key, label));
            return null;
        }

        void Fail(InterlockCond cond)
        {
            result = cond;
            (blockers ??= []).Add(cond.Target!.Params.ID);
        }

        if (interlock != null)
        {
            foreach (InterlockCond interlockCond in interlock.Conditions)
            {
                switch (interlockCond.Target!.Params.Type)
                {
                    case DeviceType.Valve:
                        // Pass the tri-state so an Undefined (disconnected PLC) state
                        // fails safe instead of being read as "closed" (fail-open).
                        if (InterlockEvaluator.ValveFails(
                                actualState: ((BaseValve)interlockCond.Target).State,
                                requiredIsOpen: (bool)interlockCond.Value!))
                            Fail(interlockCond);
                        break;
                    case DeviceType.Pump:
                    {
                        var pump = (BasePump)interlockCond.Target;
                        bool pumpFails = interlockCond.StringCondition == "status"
                            // Named bit must be true on a live read; a disconnected pump or a
                            // short/empty status array is unknown and fails closed.
                            ? InterlockEvaluator.PumpStatusBitFails(pump.Connected, pump.Status, (int)interlockCond.Value!)
                            // Generic running semantics: a turbo's Status[0] is the transient
                            // ACCL bit, so the decoder the device class owns is the only
                            // correct "is it running" answer.
                            : InterlockEvaluator.PumpFails(pump.IsRunning, (bool)interlockCond.Value!);
                        if (pumpFails) Fail(interlockCond);
                        break;
                    }
                    case DeviceType.Power:
                    {
                        var psu = (BasePowerSupply)interlockCond.Target;
                        if (InterlockEvaluator.PowerFails(
                                connected: psu.Connected,
                                responding: psu.IsResponding,
                                outputOn: psu.IsOutputOn,
                                measuredWatts: psu.Power,
                                condition: interlockCond.Condition,
                                thresholdWatts: Convert.ToSingle(interlockCond.Value!, CultureInfo.InvariantCulture)))
                            Fail(interlockCond);
                        break;
                    }
                    case DeviceType.MFC:
                    {
                        var mfc = (BaseMFC)interlockCond.Target;
                        float flow = mfc.Connected ? mfc.ReadFlow() : float.NaN;
                        float number = Convert.ToSingle(interlockCond.Value!, CultureInfo.InvariantCulture);
                        bool failed;
                        if (interlockCond.StringCondition == "stable")
                        {
                            float setpoint = mfc.Connected ? mfc.ReadSetpoint() : float.NaN;
                            failed = InterlockEvaluator.MfcStableFails(mfc.Connected, mfc.LastFlowValid, flow, setpoint, number);
                        }
                        else
                            failed = InterlockEvaluator.MfcFlowFails(mfc.Connected, mfc.LastFlowValid, flow, interlockCond.Condition, number);
                        if (failed) Fail(interlockCond);
                        break;
                    }
                    case DeviceType.Sensor:
                        // Convert (not (float) unbox): Obj() boxes an integer literal
                        // threshold ("750") as int, and a direct (float) unbox throws
                        // InvalidCastException, aborting the guarded op (audit M-13).
                        if (InterlockEvaluator.SensorFails(
                                actual: ((BaseSensor)interlockCond.Target).Value(),
                                condition: interlockCond.Condition,
                                expected: Convert.ToSingle(interlockCond.Value!, CultureInfo.InvariantCulture)))
                            Fail(interlockCond);
                        break;
                    case DeviceType.Stage:
                        // 3-part XML form: "Stage <id> <home-position>".
                        if (InterlockEvaluator.StageFails(
                                actualPosition: ((BaseStage)interlockCond.Target).Position,
                                expectedPosition: Convert.ToSingle(interlockCond.Value!, CultureInfo.InvariantCulture),
                                tolerance: StageHomeTolerance))
                            Fail(interlockCond);
                        break;
                    case DeviceType.Robot:
                        // 5-part XML form: "Robot <id> state <op> <stateName>"
                        // e.g. "Robot ROBOT state notIn transferring"
                        if (InterlockEvaluator.RobotFails(
                                actual: ((BaseRobot)interlockCond.Target).State,
                                expectedStateName: (string)interlockCond.Value!,
                                op: interlockCond.StringCondition!))
                            Fail(interlockCond);
                        break;
                }
            }
        }

        if (result.HasValue && blockers is { Count: > 0 })
        {
            InterlockCond withBlockers = result.Value;
            withBlockers.Blockers = string.Join(", ", blockers);
            result = withBlockers;
        }

        return result;
    }

    internal bool LoadInterlocks(string fileName)
    {
        bool loadedAny = false;
        try
        {
            XML_Interlocks xml_interlocks = Serializer<XML_Interlocks>.Load(fileName);
            if (xml_interlocks != null)
            {
                if (xml_interlocks.Includes != null)
                {
                    string path = Path.GetDirectoryName(fileName);
                    foreach (string file in xml_interlocks.Includes)
                    {
                        // Propagate include failures: one missing/malformed include
                        // must not silently drop its whole interlock file (e.g. all
                        // GVL/Stage collision guards) while the parent reports OK.
                        if (!LoadInterlocks(Path.Combine(path, @"Interlocks\" + file)))
                            return false;
                        loadedAny = true;
                    }
                }
                if (xml_interlocks.InterlockList != null)
                {
                    foreach (XML_Interlock xml_interlock in xml_interlocks.InterlockList)
                    {
                        Interlock interlock = new();
                        foreach (string condition in xml_interlock.Conditions!)
                        {
                            interlock.Conditions.Add(ParseInterlockCondition(condition));
                        }
                        // Key must match CheckInterlocks() format: opName_deviceId_arg
                        // XML IDs use keywords (open/closed/on/off) but CheckInterlocks
                        // builds keys from boolean args (true/false). Substitute underscored
                        // ID so "valve_a2_open" becomes "valve_a2_true".
                        string interlockKey = xml_interlock.Id!.ToLower().Replace('_', ' ');
                        interlockKey = Substitute(interlockKey).Replace(' ', '_');
                        interlocks.Add(interlockKey, interlock);
                    }
                    SendLogMesssage("Core", $"Interlocks loaded from {fileName}");
                    loadedAny = true;
                }
            }

            // Fail closed: a file that yielded neither includes nor interlocks is a
            // misconfiguration (renamed/empty/malformed) — surface it rather than
            // booting with the guards silently absent. A pure-<includes> top-level
            // file correctly returns true here once its children have loaded.
            if (!loadedAny)
                LastErrorMessage = $"No interlocks or includes found in {fileName}";
            return loadedAny;
        }
        catch (Exception ex)
        {
            LastErrorMessage = "Failed to load Interlocks from file: " + ex.Message;
            System.Diagnostics.Trace.TraceError($"Core.LoadInterlocks({fileName}): {ex}");
        }
        return false;
    }

    private InterlockCond ParseInterlockCondition(string condition)
    {
        string[] parts = PrepareParts(condition);
        if (parts.Length < 3)
            throw new InvalidOperationException($"Interlocks condition '{condition}': expected '<kind> <id> <value>'.");

        DeviceType targetType = FindTarget(parts[0]);
        if (targetType is not (DeviceType.Valve or DeviceType.Pump or DeviceType.Sensor
            or DeviceType.Stage or DeviceType.Robot or DeviceType.Power or DeviceType.MFC))
            throw new InvalidOperationException(
                $"Interlocks condition '{condition}': unsupported condition kind '{parts[0]}'. " +
                "Supported: Valve, Pump, Sensor, Stage, Robot, Power, MFC.");

        InterlockCond interlockCond = new() { Target = FindElement(targetType, parts[1]) };
        if (targetType is DeviceType.Power or DeviceType.MFC && interlockCond.Target is null)
            throw new InvalidOperationException(
                $"Interlocks condition '{condition}': {targetType} '{parts[1]}' is not configured.");

        if (targetType == DeviceType.Pump && parts.Length == 4
            && parts[2].Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            if (interlockCond.Target is not IStatusBitNamed named)
                throw new InvalidOperationException(
                    $"Interlocks condition '{condition}': pump '{parts[1]}' is not configured or does not declare named status bits.");
            int bit = -1;
            for (int i = 0; i < named.StatusBitNames.Count; i++)
                if (named.StatusBitNames[i].Equals(parts[3], StringComparison.OrdinalIgnoreCase)) { bit = i; break; }
            if (bit < 0)
                throw new InvalidOperationException(
                    $"Interlocks condition '{condition}': pump '{parts[1]}' has no status bit named '{parts[3]}'. " +
                    $"Known: {string.Join(", ", named.StatusBitNames)}");
            interlockCond.StringCondition = "status";
            interlockCond.Value = bit;
        }
        else if (parts.Length == 5 && parts[2].Equals("state", StringComparison.OrdinalIgnoreCase))
        {
            interlockCond.StringCondition = parts[3];
            interlockCond.Value = parts[4];
        }
        else if (targetType == DeviceType.MFC && parts.Length == 4
                 && parts[2].Equals("stable", StringComparison.OrdinalIgnoreCase))
        {
            interlockCond.StringCondition = "stable";
            interlockCond.Value = ParseFiniteThreshold(parts[3], condition, minExclusive: 0f, maxInclusive: 100f);
        }
        else if (parts.Length == 4)
        {
            interlockCond.Condition = ConditionParser.ParseCondition(parts[2],
                $"Interlocks condition '{condition}'");
            interlockCond.Value = targetType is DeviceType.Power or DeviceType.MFC
                ? ParseFiniteThreshold(parts[3], condition, minExclusive: float.NegativeInfinity, maxInclusive: float.PositiveInfinity)
                : Obj(parts[3]);
        }
        else if (targetType is DeviceType.Power or DeviceType.MFC)
            throw new InvalidOperationException(
                $"Interlocks condition '{condition}': {targetType} conditions take '<id> less|equal|more <number>'" +
                (targetType == DeviceType.MFC ? " or '<id> stable <percent>'." : "."));
        else
            interlockCond.Value = Obj(parts[2]);

        return interlockCond;
    }

    private static float ParseFiniteThreshold(string text, string condition, float minExclusive, float maxInclusive)
    {
        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)
            || !float.IsFinite(v) || v <= minExclusive || v > maxInclusive)
            throw new InvalidOperationException(
                $"Interlocks condition '{condition}': '{text}' is not a valid finite threshold.");
        return v;
    }

    internal static object? Obj(string input)
    {
        // Config literals are culture-invariant (e.g. the interlock threshold
        // "5.0e-2"); a current-culture parse fails on a comma-decimal locale (ru-RU)
        // and the value falls through as a string.
        if (int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out int iresult))
            return iresult;
        else if (float.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out float fresult))
            return fresult;
        else if (bool.TryParse(input, out bool bresult))
            return bresult;
        return input;
    }
}

