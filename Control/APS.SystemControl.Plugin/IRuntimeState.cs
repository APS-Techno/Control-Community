namespace APS.SystemControl.Plugin;

/// <summary>
/// Live runtime state exposed to menu contributors. Distinct from
/// <see cref="IFlagService"/>, which answers licensing questions, not
/// "is the machine busy right now".
/// </summary>
/// <remarks>
/// <see cref="IsManualControlGated"/> deliberately has NO default implementation: a held
/// recipe unlocks manual hardware control while <see cref="IsRecipeRunning"/> stays true,
/// so a default derived only from <c>IsRecipeRunning</c> would keep denying an operator
/// who was just told to go fix something by hand. Implementations wrapping <c>Core</c>
/// forward <c>Core.IsManualControlGated</c> (reference:
/// <c>Core/Menus/Services/RuntimeStateAdapter.cs</c>); implementations with no concept
/// of a hold return their <c>IsRecipeRunning</c> expression unchanged.
/// </remarks>
public interface IRuntimeState
{
    /// <summary>
    /// True while a recipe holds the single-flight run slot. Motion-issuing
    /// menu items must disable themselves when this is true.
    /// </summary>
    bool IsRecipeRunning { get; }

    /// <summary>
    /// True when manual hardware control is locked out by the run. This — not
    /// <see cref="IsRecipeRunning"/> — is what a motion or power control must
    /// gate on: a recipe that has been HELD for the operator keeps
    /// <see cref="IsRecipeRunning"/> true (the run still owns the machine and
    /// Start stays refused) while deliberately unlocking manual control, because
    /// the operator was just asked to go and fix something by hand.
    /// </summary>
    bool IsManualControlGated { get; }
}
