namespace APS.ProcessPanel.Contracts;

public record ProcessParameter(
    string Name,
    double Value,
    string Unit,
    string? Category = null
);
