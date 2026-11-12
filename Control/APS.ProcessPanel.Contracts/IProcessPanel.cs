namespace APS.ProcessPanel.Contracts;

public interface IProcessPanel
{
    void UpdateRecipe(RecipeInfo info);
    void UpdateStep(ProcessStepInfo info);
    void UpdateDrives(DriveInfo[] drives);
    void UpdateReadings(ProcessReading[] readings);
    void UpdateValves(ValveInfo[] valves);
    void UpdateSources(IReadOnlyDictionary<int, FlangeSourceInfo> flangeSources);
    void Log(string source, string message);
    void SetPowerSupplyVisibility(IReadOnlyDictionary<string, bool> visibleById);

    /// <summary>
    /// Every <c>ProcessReading.Id</c> this panel can display — the host registers
    /// exactly these with its telemetry collector, so the panel's reading list
    /// is the single source of truth for what gets cached.
    /// </summary>
    IReadOnlyCollection<string> ReadingIds { get; }
}
