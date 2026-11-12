using System.Collections.Generic;
using System.Linq;

namespace APS.SystemControl.Core;

/// <summary>Pure predicate: is a deposition happening? True while a recipe is
/// running or whenever any power supply is drawing watts. NOTE: keyed on the
/// recipe-running flag, NOT the chamber SystemState — the chamber flips to
/// Processing on gas/pressure regulation alone, which is not a deposition.
/// Watts come from cached device state — callers must never live-read the PLC.</summary>
public static class DepositionActivity
{
    public static bool IsActive(bool recipeRunning, IEnumerable<double> psuWatts)
        => recipeRunning || psuWatts.Any(w => w > 0);
}
