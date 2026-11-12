namespace APS.ProcessPanel.Contracts;

public record ProcessStepInfo(
    string StepType,
    string? Material,
    double ElapsedSeconds,
    double TotalSeconds,
    IReadOnlyDictionary<string, ProcessParameter> Parameters
);
