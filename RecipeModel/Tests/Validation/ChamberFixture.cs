using System.Collections.Generic;
using System.Linq;
using APS.RecipeModel.Validation;

namespace APS.RecipeModel.Tests.Validation;

internal static class ChamberFixture
{
    public static RecipeValidator Bound()
    {
        var ops = new List<(string Name, string? BoundDevice, IReadOnlyList<string> Vars)>
        {
            ("valve", null, ["Valve", "b"]),
            ("mfc", null, ["MFC", "f"]),
            ("pwait", "TC1", ["Controller", "f", "f"]),
        };
        var devices = new Dictionary<string, IReadOnlyCollection<string>>
        {
            ["Valve"] = new[] { "V1" },
            ["MFC"] = new[] { "Ar1", "Ar2", "LL_Ar" },
            ["Controller"] = new[] { "TC1" },
            ["Sensor"] = new[] { "pm", "pl" },
        };
        var chambers = new Dictionary<string, string>
        {
            ["pm"] = "Main", ["Ar1"] = "Main", ["Ar2"] = "Main", ["TC1"] = "Main",
            ["pl"] = "Loadlock", ["LL_Ar"] = "Loadlock",
        };
        var limits = new Dictionary<string, ChamberAlarmLimits>
        {
            ["Main"] = new(1e-4f, 60, null, 2e-1f),
        };
        return Make(ops, devices, chambers, limits);
    }

    public static RecipeValidator Unbound()
    {
        var ops = new List<(string Name, string? BoundDevice, IReadOnlyList<string> Vars)>
        {
            ("valve", null, ["Valve", "b"]),
            ("mfc", null, ["MFC", "f"]),
            ("pwait", null, ["Sensor", "f", "f"]),
        };
        var devices = new Dictionary<string, IReadOnlyCollection<string>>
        {
            ["Valve"] = new[] { "V1" },
            ["MFC"] = new[] { "C1_Ar", "TM_Ar", "LL_Ar" },
            ["Sensor"] = new[] { "C1_pm", "TM_pm", "LL_pm" },
        };
        var chambers = new Dictionary<string, string>
        {
            ["C1_pm"] = "C1", ["C1_Ar"] = "C1",
            ["TM_pm"] = "TM", ["TM_Ar"] = "TM",
            ["LL_pm"] = "LL", ["LL_Ar"] = "LL",
        };
        var limits = new Dictionary<string, ChamberAlarmLimits>
        {
            ["C1"] = new(1e-4f, 0, "C1_A1 open", 2e-1f),
            ["TM"] = new(1e-3f, 0, null, null),
        };
        return Make(ops, devices, chambers, limits);
    }

    public static RecipeValidator Make(
        List<(string Name, string? BoundDevice, IReadOnlyList<string> Vars)> ops,
        Dictionary<string, IReadOnlyCollection<string>> devices,
        Dictionary<string, string> chambers,
        Dictionary<string, ChamberAlarmLimits> limits)
    {
        var semantics = RecipeSemantics.Build(ops, devices, chamberLimits: limits, deviceChamber: chambers);
        return new RecipeValidator(
            RecipeValidator.AllKnownCommands,
            knownDevices: semantics.AllDeviceNames,
            semantics: semantics);
    }

    public static List<RecipeError> Warnings(RecipeValidator v, string recipe, string fragment) =>
        v.Validate(recipe)
            .Where(e => e.Severity == RecipeErrorSeverity.Warning && e.Message.Contains(fragment))
            .ToList();
}
