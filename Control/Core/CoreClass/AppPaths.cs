namespace APS.SystemControl.Core.CoreClass;

/// <summary>
/// Process-wide resolved filesystem roots, populated once at config load
/// (<see cref="Initialize"/>) from the active <see cref="Config"/>. Read by
/// the scattered logging consumers that cannot reach a Config instance
/// (DataLogger, DepositionRecordService — DataDir; LogThread, process panels — LogsDir). Defaults match the
/// historical hardcoded literals so reads before init are still safe.
/// </summary>
public static class AppPaths
{
    public static string RecipesDir { get; private set; } = "Recipes";
    public static string LogsDir { get; private set; } = "Logs";
    public static string DataDir { get; private set; } = "Data";

    internal static void Initialize(Config config)
    {
        ArgumentNullException.ThrowIfNull(config);
        RecipesDir = config.RecipesDir;
        LogsDir = config.LogsDir;
        DataDir = config.DataDir;
    }
}
