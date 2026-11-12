using System.Globalization;
using APS.SystemControl.Core.HWClasses.Base;
using APS.SystemControl.XML;

namespace APS.SystemControl.Core.Classes;

/// <summary>
/// Loads <c>Alarms.xml</c> into per-chamber <see cref="Alarms"/> instances. Pure
/// translation: no hardware, no channels, no side effects beyond surfacing the
/// parsed rules and initial states.
/// </summary>
internal static class AlarmConfigLoader
{
    /// <param name="ChamberAlarms">One <see cref="Alarms"/> instance per chamber defined in the file. Flat (non-wrapped) configs yield a single entry keyed by <see cref="CoreConstants.DefaultChamberId"/>.</param>
    /// <param name="InitialStates">Initial <see cref="SystemState"/> per chamber, or null if the chamber's <c>&lt;states&gt;</c> element omits <c>initial</c>.</param>
    /// <param name="StateTransitionLog">Human-readable log lines describing each loaded transition — the caller forwards them to its log channel.</param>
    /// <param name="Warnings">Non-fatal issues encountered while parsing individual alarm rules — e.g. a device referenced by an alarm doesn't implement <c>IStatusBitNamed</c>. Each entry names the chamber and quotes the underlying exception. The bad rule is skipped; the rest of the file still loads.</param>
    public sealed record LoadResult(
        IReadOnlyDictionary<string, Alarms> ChamberAlarms,
        IReadOnlyDictionary<string, SystemState?> InitialStates,
        IReadOnlyList<string> StateTransitionLog,
        IReadOnlyList<string> Warnings);

    /// <param name="deviceResolver">
    /// Optional callback to resolve a device ID from the XML to an
    /// <see cref="IBaseComponent"/> instance. Required when the file
    /// contains <c>&lt;alarm device="..." status="..."&gt;</c> rules; pass
    /// <c>null</c> for sensor-only configs.
    /// </param>
    public static LoadResult Load(string fileName,
        Func<string, IBaseComponent?>? deviceResolver = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);

        var xml = Serializer<XML_Alarms>.Load(fileName)
            ?? throw new InvalidOperationException($"Alarms.xml at '{fileName}' could not be parsed.");

        var chamberAlarms = new Dictionary<string, Alarms>();
        var initialStates = new Dictionary<string, SystemState?>();
        var transitionLog = new List<string>();
        var warnings = new List<string>();

        if (xml.Chambers.Count > 0)
        {
            foreach (var ch in xml.Chambers)
            {
                string id = ch.Id
                    ?? throw new InvalidOperationException("Alarms.xml <chamber> missing 'id' attribute.");
                var alarms = new Alarms(id);
                SystemState? init = LoadStatesInto(ch.States, alarms, transitionLog);
                foreach (var a in ch.AlarmList)
                    TryLoadAlarmRule(alarms, a, deviceResolver, id, warnings);
                chamberAlarms[id] = alarms;
                initialStates[id] = init;
            }
        }
        else
        {
            // Flat config — implicit Main chamber.
            string id = CoreConstants.DefaultChamberId;
            var alarms = new Alarms(id);
            SystemState? init = LoadStatesInto(xml.States, alarms, transitionLog);
            foreach (var a in xml.AlarmList)
                TryLoadAlarmRule(alarms, a, deviceResolver, id, warnings);
            chamberAlarms[id] = alarms;
            initialStates[id] = init;
        }

        return new LoadResult(chamberAlarms, initialStates, transitionLog, warnings);
    }

    /// <summary>
    /// Wraps <see cref="LoadAlarmRule"/> so a single bad rule (e.g. a device
    /// alarm whose target doesn't implement <see cref="IStatusBitNamed"/>) is
    /// recorded as a warning rather than aborting the whole file. The chamber
    /// keeps any rules that did parse, and the caller continues with the rest
    /// of the file so chamber state initialisation and the status-bar UI still
    /// see a populated <c>chamberStates</c>.
    /// </summary>
    private static void TryLoadAlarmRule(Alarms alarms, XML_Alarm xmlAlarm,
        Func<string, IBaseComponent?>? deviceResolver, string chamberId,
        List<string> warnings)
    {
        try
        {
            LoadAlarmRule(alarms, xmlAlarm, deviceResolver);
        }
        catch (Exception ex)
        {
            string ruleLabel = !string.IsNullOrEmpty(xmlAlarm.Device)
                ? $"device '{xmlAlarm.Device}'"
                : !string.IsNullOrEmpty(xmlAlarm.Sensor)
                    ? $"sensor '{xmlAlarm.Sensor}'"
                    : "rule";
            warnings.Add($"Chamber '{chamberId}': skipped alarm {ruleLabel} — {ex.Message}");
        }
    }

    private static SystemState? LoadStatesInto(XML_States? states, Alarms alarms, List<string> log)
    {
        if (states is null) return null;
        SystemState? initial = null;
        if (states.Initial is { } initialStr)
            initial = ConditionParser.ParseSystemStateStrict(initialStr, "Alarms.xml <states initial>");
        foreach (var t in states.Transitions)
            LoadTransition(alarms, t, log);
        return initial;
    }

    /// <summary>
    /// Parses an <c>unless="&lt;valve&gt; open|closed"</c> alarm guard into
    /// (valveId, suppressWhenOpen). Returns (null, false) when absent/malformed.
    /// </summary>
    private static (string? valve, bool open) ParseGuard(string? unless)
    {
        if (string.IsNullOrWhiteSpace(unless)) return (null, false);
        var parts = unless.Split(' ', 2,
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return (null, false);
        return (parts[0], parts[1].Equals("open", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Parses the optional <c>delay="60"</c> settling period (seconds). Absent
    /// or blank means 0 (fire on the first matching sample). Throws on a value
    /// that is not a non-negative number, rather than silently disabling the
    /// delay and letting a nuisance alarm return unnoticed.
    /// </summary>
    private static float ParseDelay(string? text, string ruleName)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0f;
        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float seconds)
            || seconds < 0f || float.IsNaN(seconds) || float.IsInfinity(seconds))
        {
            throw new InvalidOperationException(
                $"Alarms.xml rule '{ruleName}' has an invalid delay '{text}'. " +
                "Expected a non-negative number of seconds, e.g. delay=\"60\".");
        }
        return seconds;
    }

    private static void LoadAlarmRule(Alarms alarms, XML_Alarm xmlAlarm,
        Func<string, IBaseComponent?>? deviceResolver)
    {
        bool hasSensor = !string.IsNullOrEmpty(xmlAlarm.Sensor);
        bool hasDevice = !string.IsNullOrEmpty(xmlAlarm.Device);

        if (hasSensor)
        {
            int condition = ConditionParser.ParseCondition(xmlAlarm.Condition!,
                $"Alarms.xml sensor '{xmlAlarm.Sensor}'");
            SystemState state = ConditionParser.ParseSystemStateStrict(xmlAlarm.State!,
                $"Alarms.xml sensor '{xmlAlarm.Sensor}'");
            var (guardValve, guardOpen) = ParseGuard(xmlAlarm.Unless);
            float delaySeconds = ParseDelay(xmlAlarm.Delay, xmlAlarm.Sensor!);
            alarms.Add(xmlAlarm.Sensor!, Convert.ToSingle(xmlAlarm.Value, CultureInfo.InvariantCulture),
                condition, state, xmlAlarm.Message ?? string.Empty, guardValve, guardOpen,
                delaySeconds);
            return;
        }

        if (hasDevice)
        {
            if (string.IsNullOrEmpty(xmlAlarm.Status))
                throw new InvalidOperationException(
                    $"Alarms.xml device alarm for '{xmlAlarm.Device}' is missing the 'status' attribute.");

            IBaseComponent device =
                (deviceResolver?.Invoke(xmlAlarm.Device!))
                ?? throw new InvalidOperationException(
                    $"Alarms.xml references device '{xmlAlarm.Device}' which is not loaded.");

            if (device is not IStatusBitNamed bitNamed)
                throw new InvalidOperationException(
                    $"Alarms.xml device '{xmlAlarm.Device}' does not declare named status bits " +
                    $"(implement IStatusBitNamed).");

            int idx = -1;
            for (int i = 0; i < bitNamed.StatusBitNames.Count; i++)
            {
                if (bitNamed.StatusBitNames[i].Equals(xmlAlarm.Status,
                        StringComparison.OrdinalIgnoreCase))
                {
                    idx = i;
                    break;
                }
            }
            if (idx < 0)
                throw new InvalidOperationException(
                    $"Alarms.xml device '{xmlAlarm.Device}' has no status bit named '{xmlAlarm.Status}'. " +
                    $"Known: {string.Join(", ", bitNamed.StatusBitNames)}");

            SystemState state = ConditionParser.ParseSystemStateStrict(xmlAlarm.State!,
                $"Alarms.xml device '{xmlAlarm.Device}'");
            alarms.AddDeviceStatusAlarm(xmlAlarm.Device!, idx, state, xmlAlarm.Message ?? string.Empty);
            return;
        }

        throw new InvalidOperationException(
            "Alarm rule must have either 'sensor' or both 'device' and 'status' attributes.");
    }

    private static void LoadTransition(Alarms alarms, XML_Transition t, List<string> log)
    {
        if (t.From is null || t.To is null)
            throw new InvalidOperationException("Transition missing 'from' or 'to' attribute");

        SystemState from = ConditionParser.ParseSystemStateStrict(t.From, "Alarms.xml transition 'from'");
        SystemState to   = ConditionParser.ParseSystemStateStrict(t.To,   "Alarms.xml transition 'to'");

        if (t.Sensor is not null)
        {
            if (t.Condition is null || t.Value is null)
                throw new InvalidOperationException(
                    $"Sensor transition {t.From}->{t.To} missing 'cond' or 'val' attribute");

            int condition = ConditionParser.ParseCondition(t.Condition,
                $"Alarms.xml transition {t.From}->{t.To}");
            float value = Convert.ToSingle(t.Value, CultureInfo.InvariantCulture);
            alarms.AddSensorTransition(from, to, t.Sensor, condition, value);
            log.Add($"State transition: {from} -> {to} when {t.Sensor} {t.Condition} {t.Value}");
        }
        else if (t.Valve is not null)
        {
            if (t.State is null)
                throw new InvalidOperationException(
                    $"Valve transition {t.From}->{t.To} missing 'state' attribute");

            bool valveOpen = t.State.Equals("open", StringComparison.OrdinalIgnoreCase);
            string[] requirements = string.IsNullOrEmpty(t.Require)
                ? []
                : t.Require.Split(',', StringSplitOptions.TrimEntries);
            alarms.AddValveTransition(from, to, t.Valve, valveOpen, requirements);
            log.Add($"State transition: {from} -> {to} when valve {t.Valve} {t.State}");
        }
        else if (t.Group is not null)
        {
            if (t.State is null)
                throw new InvalidOperationException(
                    $"Group transition {t.From}->{t.To} missing 'state' attribute");

            GroupKind group = ConditionParser.ParseGroupKind(t.Group,
                $"Alarms.xml transition {t.From}->{t.To}");
            GroupCondition cond = ConditionParser.ParseGroupCondition(t.State,
                $"Alarms.xml transition {t.From}->{t.To}");
            alarms.AddGroupTransition(from, to, group, cond);
            log.Add($"State transition: {from} -> {to} when group {t.Group} {t.State}");
        }
    }
}
