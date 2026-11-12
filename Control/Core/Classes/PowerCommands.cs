namespace APS.SystemControl.Core.Classes;

/// <summary>
/// The manual power-supply command names understood by the HW dispatcher's
/// <c>ProcessPowerCommand</c>. These are the PANEL command names, not the recipe
/// DSL verbs: a panel/agent sends <c>["SetCurrent", 250]</c> then
/// <c>["OutputOn"]</c>, whereas the DSL writes <c>set PSU-A Current 250</c> /
/// <c>power PSU-A on</c>.
///
/// Kept here so the dispatcher can reject an unrecognised name loudly: a
/// silently ignored wrong name (notably a DSL verb) leaves a supply the caller
/// believes energised switched off.
/// </summary>
internal static class PowerCommands
{
    /// <summary>
    /// Every command name <c>ProcessPowerCommand</c> dispatches. Must stay in
    /// step with that switch — <c>PowerCommandNamesTests</c> guards both ways.
    /// </summary>
    public static readonly IReadOnlyList<string> Known =
    [
        "OutputOn",
        "OutputOff",
        "SetCurrent",
        "SetVoltage",
        "SetPower",
        "SetMode",
        "SetFrequency",
        "SetDutyCycle",
        "SetRampUp",
        "SetRampDown",
        "PulseMode",
        "RampEnabled",
        "SpeedDetect",
        "ClearFault",
        "Reconnect",
    ];

    /// <summary>
    /// True when <paramref name="command"/> is a dispatchable power command.
    /// Case-insensitive, matching the dispatcher's own comparisons. Pass the
    /// name with any <c>":channel"</c> suffix already stripped.
    /// </summary>
    public static bool IsKnown(string? command)
        => !string.IsNullOrWhiteSpace(command)
           && Known.Contains(command, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Operator-facing rejection text. Lists the valid names so a caller that
    /// guessed (or followed stale documentation) can see what to send instead.
    /// </summary>
    public static string UnknownMessage(string psuId, string? command)
        => $"Unknown power command '{command}' for {psuId} — ignored. "
         + $"Valid commands: {string.Join(", ", Known)}.";
}
