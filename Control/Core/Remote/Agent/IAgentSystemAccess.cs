using APS.RecipeModel.Validation;

namespace APS.SystemControl.Core.Remote.Agent;

internal interface IAgentSystemAccess
{
    // discovery + telemetry (Read tier)
    object DescribeMachine();
    object DescribeHardware();
    /// <summary>Describe available recipes (embedded and library). A key scoped to a recipe
    /// folder (<paramref name="folder"/> non-null) sees only the user recipes in that folder.</summary>
    object DescribeRecipes(string? folder);
    object DescribeDsl();
    object DescribeInterlocks();
    object DescribeAlarms();
    /// <summary>Get overall system status (Startup/Idle/Processing/Maintenance).</summary>
    object SystemStatus();
    object DeviceSnapshot();
    object TailLog(int count);
    object RecipeStatus();
    /// <summary>Most recent runs (newest first) with the artifacts on disk for each: TSV data log,
    /// process-log window, as-run .rcpx, signed .depo.json snapshot.</summary>
    object ListRuns(int count);
    /// <summary>One byte range of a run artifact, base64-encoded in the payload, with eof/totalBytes
    /// so a caller can page through a multi-megabyte TSV under the wire's line limit.</summary>
    object ReadRunFile(string runId, string kind, long offset, int maxBytes);

    // recipe (Recipe tier)
    IReadOnlyList<RecipeError> ValidateRecipe(string content, bool isRcpx);
    /// <summary>Save a recipe to the library, optionally overwriting. A key scoped to a recipe
    /// folder (<paramref name="folder"/> non-null) saves into that folder only.</summary>
    object SaveRecipe(string name, string content, bool overwrite, string? folder);
    void StartRecipe(string name, IReadOnlyDictionary<string, string>? parameters);
    void AbortRecipe();
    /// <summary>Answer a waiting fault pause with Retry / Skip / Abort — the operator's decision,
    /// taken over the wire instead of through the pause dialog. Throws when no pause is waiting:
    /// <c>Core.ResolveFault</c> is deliberately a no-op in that case, and a caller that resolved
    /// nothing must be told so rather than handed a success.</summary>
    void ResolveFaultPause(FaultDecision decision);

    // manual (Manual tier)
    void SendDeviceCommand(string deviceType, string deviceId, object[] args);
}
