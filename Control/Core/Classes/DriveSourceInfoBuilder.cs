using APS.RecipeModel.Validation;

namespace APS.SystemControl.Core.Classes;

/// <summary>
/// Builds the per-flange source payload the stepper-drive panel draws beside each
/// flange. Kept out of the form so the label rule is unit-testable: the label is the
/// effective source handle — the <c>name</c> when set, else the material — the same
/// handle recipes address (see <see cref="SourceNames.Effective"/>).
/// <para>
/// The panel lives in a plugin DLL that cannot reference <see cref="Source"/>, so the
/// payload is plain arrays: one <c>object[] { label(string), totalSeconds(double) }</c>
/// per flange, in flange order 1..N.
/// </para>
/// </summary>
public static class DriveSourceInfoBuilder
{
    /// <param name="flangeCount">
    /// Number of flange entries to emit. The stepper panel drops the payload unless its
    /// length matches its own flange count, so this stays an explicit count rather than
    /// whatever the running config happens to hold.
    /// </param>
    public static object[] Build(SourceManager sources, int flangeCount = 6)
    {
        var info = new object[flangeCount];
        for (int i = 1; i <= flangeCount; i++)
        {
            var src = sources.GetSource(i);
            info[i - 1] = new object[]
            {
                SourceNames.Effective(src?.Name, src?.Material),
                src?.TotalDepositionTime.TotalSeconds ?? 0.0
            };
        }
        return info;
    }
}
