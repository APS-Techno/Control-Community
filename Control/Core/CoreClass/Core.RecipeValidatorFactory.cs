using APS.RecipeModel.Validation;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.Classes;
using APS.SystemControl.Core.CoreClass;
using APS.SystemControl.Core.HWClasses.Base;
using APS.SystemControl.Core.Remote.Agent;

namespace APS.SystemControl.Core;

public partial class Core
{
    // throttleValveSensor = "pm" matches the throttle-controller binding in Worker.xml
    // (the only sensor whose pressure is regulated via a throttle valve);
    // pwait on any other sensor is a recipe-author mistake — use swait.
    internal RecipeSemantics BuildRecipeSemantics() => RecipeSemantics.Build(
        GetOperationDeclarations(), GetDeviceNamesByKind(), GetSetpointParameterNames(),
        sourceToPsu: CoreAgentSystemAccess.SourceToPsuMap(ConfigDir is null
            ? []
            : SourcesMapLoader.LoadFlangeSourceMap(ConfigDir).Values),
        mfcGateValves: CoreAgentSystemAccess.MfcGateValvesMap(
            hwElements.Item.TryGetValue(DeviceType.MFC, out var mfcs)
                ? mfcs.Select(kv => (kv.Key, kv.Value)).ToList()
                : []),
        rotationOperational: IsRotationOperational(),
        chamberLimits: ChamberLimitsOf(chamberAlarms),
        deviceChamber: DeviceChamberMap(hwElements));

    public RecipeValidator BuildRecipeValidator() => BuildRecipeValidator(BuildRecipeSemantics());

    internal RecipeValidator BuildRecipeValidator(RecipeSemantics semantics) => new(
        RecipeValidator.AllKnownCommands,
        GetKnownDeviceNames(),
        GetInstalledSourceNames(),
        embRecipes?.GetKnownNames(),
        throttleValveSensor: "pm",
        semantics: semantics);

    private bool IsRotationOperational() =>
        IsStageOperational(FindElement(DeviceType.Stage, "rotation"))
        || IsStageOperational(FindElement(DeviceType.Stage, "Rotation"));

    private static bool IsStageOperational(IBaseComponent? stage) =>
        stage is { Connected: true } && stage is not BaseStage { IsOperational: false };

    /// <summary>
    /// Per chamber: the lowest <c>cond="more"</c> ceiling of its idle rules (and that rule's
    /// settling delay and guard) and of its processing rules. A chamber with neither is omitted.
    /// </summary>
    internal static IReadOnlyDictionary<string, ChamberAlarmLimits> ChamberLimitsOf(
        IEnumerable<KeyValuePair<string, Alarms>> chambers)
    {
        var result = new Dictionary<string, ChamberAlarmLimits>(StringComparer.OrdinalIgnoreCase);
        foreach (var (chamber, alarms) in chambers)
        {
            float? idleCeiling = null, processingCeiling = null;
            double idleDelay = 0d;
            string? idleUnless = null;
            foreach (var rule in alarms.Rules)
            {
                if (rule.Condition != 1) continue;
                if (rule.State == SystemState.Idle && (idleCeiling is null || rule.Value < idleCeiling))
                {
                    idleCeiling = rule.Value;
                    idleDelay = rule.DelaySeconds;
                    idleUnless = rule.GuardValve is null
                        ? null
                        : $"{rule.GuardValve} {(rule.GuardValveOpen ? "open" : "closed")}";
                }
                else if (rule.State == SystemState.Processing
                    && (processingCeiling is null || rule.Value < processingCeiling))
                    processingCeiling = rule.Value;
            }
            if (idleCeiling is null && processingCeiling is null) continue;

            result[chamber] = new ChamberAlarmLimits(idleCeiling, idleDelay, idleUnless, processingCeiling);
        }
        return result;
    }

    internal static IReadOnlyDictionary<string, string> DeviceChamberMap(HwElementCollection elements)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (type, group) in elements.Item)
        {
            if (type is not (DeviceType.Sensor or DeviceType.MFC or DeviceType.Controller)) continue;
            foreach (var (id, device) in group)
            {
                string? chamber = device switch
                {
                    BaseSensor s => s.Chamber,
                    BaseMFC m => m.Chamber,
                    BaseController c => c.Chamber,
                    _ => null,
                };
                if (chamber is not null) result[id] = chamber;
            }
        }
        return result;
    }
}
