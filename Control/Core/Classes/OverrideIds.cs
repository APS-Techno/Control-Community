using System.Globalization;
using APS.SystemControl.Core.HWClasses.Base;

namespace APS.SystemControl.Core.Classes;

/// <summary>
/// Static helpers for synthesizing stable IDs and display labels
/// for alarm rules. Interlocks already have dictionary keys and do
/// not need synthesis.
/// </summary>
internal static class OverrideIds
{
    /// <summary>
    /// Format: alarm:{chamberId}|{sensor}|{state}|{op}|{value}
    /// where op is &lt; / = / &gt; mapped from condition -1/0/1.
    /// The chamber id is the first segment after the type prefix so a
    /// back-compat reader can match legacy entries (no chamber segment)
    /// by stripping just the chamber prefix when chamberId == "Main".
    /// </summary>
    public static string Alarm(string chamberId, string sensor, SystemState state, int condition, float value)
    {
        return $"alarm:{chamberId}|{sensor}|{state}|{OpSymbol(condition)}|{FormatValue(value)}";
    }

    /// <summary>
    /// Human-facing label: "{message} — {sensor} {op} {value} in {state}"
    /// For non-default chambers the chamber id is prepended as "{chamberId}: ".
    /// The default chamber (<see cref="CoreConstants.DefaultChamberId"/>) is
    /// elided so single-chamber installs keep the original label.
    /// </summary>
    public static string AlarmLabel(string chamberId, string message, string sensor, SystemState state, int condition, float value)
    {
        string body = $"{message} — {sensor} {OpSymbol(condition)} {FormatValue(value)} in {state}";
        return chamberId == CoreConstants.DefaultChamberId ? body : $"{chamberId}: {body}";
    }

    private static string OpSymbol(int condition) => condition switch
    {
        -1 => "<",
         1 => ">",
         _ => "=",
    };

    /// <summary>
    /// Format: devstatus:{chamberId}|{deviceId}|{statusBit}|{state}
    /// </summary>
    public static string DeviceStatus(string chamberId, string deviceId, int statusBit, SystemState state)
    {
        return $"devstatus:{chamberId}|{deviceId}|{statusBit}|{state}";
    }

    /// <summary>
    /// Human-facing label: "{message} — {deviceId} status[{bit}] in {state}"
    /// For non-default chambers the chamber id is prepended as "{chamberId}: ".
    /// </summary>
    public static string DeviceStatusLabel(string chamberId, string message, string deviceId, int statusBit, SystemState state)
    {
        string body = $"{message} — {deviceId} status[{statusBit}] in {state}";
        return chamberId == CoreConstants.DefaultChamberId ? body : $"{chamberId}: {body}";
    }

    /// <summary>
    /// Human-facing label for an interlock key like "valve_m1_true".
    /// Format: "{op} {device} {arg} ⇒ requires {c1}, {c2}, …"
    /// Example: "valve M1 open ⇒ requires GVM closed, PP on"
    /// </summary>
    public static string InterlockLabel(string key, Interlock interlock)
    {
        var parts = key.Split('_');
        string op     = parts.Length > 0 ? parts[0] : "";
        string arg    = parts.Length > 1 ? parts[^1] : "";
        string device = parts.Length > 2
            ? string.Join('_', parts, 1, parts.Length - 2)
            : "";

        string argLabel = (op.ToLowerInvariant(), arg.ToLowerInvariant()) switch
        {
            ("valve", "true")  => "open",
            ("valve", "false") => "closed",
            ("pump",  "true")  => "on",
            ("pump",  "false") => "off",
            _ => arg,
        };

        string head = string.IsNullOrEmpty(device) ? $"{op} {argLabel}".Trim() : $"{op} {device.ToUpperInvariant()} {argLabel}".Trim();
        if (interlock.Conditions.Count == 0) return head;

        var condLabels = interlock.Conditions.Select(FormatInterlockCond);
        return $"{head} ⇒ requires {string.Join(", ", condLabels)}";
    }

    internal static string FormatInterlockCond(InterlockCond c)
    {
        if (c.Target == null) return "?";
        string id = c.Target.Params.ID;
        string Op() => c.Condition switch { -1 => "<", 1 => ">", _ => "=" };
        string Num() => Convert.ToSingle(c.Value, CultureInfo.InvariantCulture)
            .ToString("R", CultureInfo.InvariantCulture);
        switch (c.Target.Params.Type)
        {
            case DeviceType.Valve:
                return $"{id} {(c.Value is true ? "open" : "closed")}";
            case DeviceType.Pump when c.StringCondition == "status":
            {
                int bit = c.Value is int i ? i : -1;
                string name = c.Target is IStatusBitNamed named && bit >= 0 && bit < named.StatusBitNames.Count
                    ? named.StatusBitNames[bit]
                    : $"bit {bit}";
                return $"{id} status {name}";
            }
            case DeviceType.Pump:
                return $"{id} {(c.Value is true ? "on" : "off")}";
            case DeviceType.Sensor:
                return $"{id} {Op()} {Convert.ToString(c.Value, CultureInfo.InvariantCulture)}";
            case DeviceType.Power:
                return c.Condition == 1
                    ? $"{id} output on, measured {Op()} {Num()} W"
                    : $"{id} measured {Op()} {Num()} W";
            case DeviceType.MFC when c.StringCondition == "stable":
                return $"{id} flow stable within {Num()}% of setpoint";
            case DeviceType.MFC:
                return $"{id} flow {Op()} {Num()} sccm";
            default:
                return $"{id}={Convert.ToString(c.Value, CultureInfo.InvariantCulture)}";
        }
    }

    // Invariant culture so "0.05" stays "0.05" on machines with comma decimals.
    private static string FormatValue(float value) => value.ToString("R", CultureInfo.InvariantCulture);
}
