namespace APS.SystemControl.Core.Classes;

/// <summary>
/// Shared parsers for the textual condition / state vocabulary used by
/// Alarms.xml and Interlocks.xml. Kept in one place so a new synonym or a
/// schema change only touches a single switch.
/// </summary>
public static class ConditionParser
{
    /// <summary>
    /// Parses a textual comparison keyword into the tri-state condition code
    /// used by <see cref="Alarms"/> / <see cref="Interlocks"/> rules.
    /// </summary>
    /// <returns>-1 (less), 0 (equal), 1 (more).</returns>
    public static int ParseCondition(string condition, string context)
    {
        ArgumentException.ThrowIfNullOrEmpty(condition);
        return condition.ToLowerInvariant() switch
        {
            "less"  => -1,
            "more"  =>  1,
            "equal" =>  0,
            _ => throw new InvalidOperationException(
                $"{context}: unknown cond='{condition}'. Expected: less, more, or equal."),
        };
    }

    /// <summary>Parses a SystemState name, returning <see cref="SystemState.Unknown"/> for unknown input.</summary>
    public static SystemState ParseSystemState(string state) => state.ToLowerInvariant() switch
    {
        "startup"     => SystemState.Startup,
        "idle"        => SystemState.Idle,
        "maintenance" => SystemState.Maintenance,
        "processing"  => SystemState.Processing,
        "stopped"     => SystemState.Stopped,
        _             => SystemState.Unknown,
    };

    /// <summary>
    /// Parses a SystemState name, throwing on unknown input like the other
    /// safety-config parsers in this file. Use this wherever a typo must fail
    /// the configuration load loudly instead of silently binding a rule or
    /// transition to <see cref="SystemState.Unknown"/> — a state the machine can
    /// never be in — which disables that safety rule with no diagnostic.
    /// </summary>
    public static SystemState ParseSystemStateStrict(string state, string context)
    {
        ArgumentException.ThrowIfNullOrEmpty(state);
        SystemState parsed = ParseSystemState(state);
        if (parsed == SystemState.Unknown)
            throw new InvalidOperationException(
                $"{context}: unknown state='{state}'. " +
                "Expected: startup, idle, maintenance, processing, or stopped.");
        return parsed;
    }

    /// <summary>Parses a group kind name ("gasvalve", "gas"). Throws on unknown input.</summary>
    internal static GroupKind ParseGroupKind(string value, string context)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        return value.ToLowerInvariant() switch
        {
            "gasvalve" => GroupKind.GasValve,
            "gas"      => GroupKind.Gas,
            _ => throw new InvalidOperationException(
                $"{context}: unknown group='{value}'. Expected: gasvalve or gas."),
        };
    }

    /// <summary>Parses a group condition ("anyOpen", "allOff"). Throws on unknown input.</summary>
    internal static GroupCondition ParseGroupCondition(string value, string context)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        return value.ToLowerInvariant() switch
        {
            "anyopen" => GroupCondition.AnyOpen,
            "alloff"  => GroupCondition.AllOff,
            _ => throw new InvalidOperationException(
                $"{context}: unknown state='{value}'. Expected: anyOpen or allOff."),
        };
    }
}
