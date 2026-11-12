namespace APS.ProcessPanel.Contracts;

public record RecipeInfo(
    string Name,
    int CurrentStep,
    int TotalSteps,
    string StepDescription,
    RecipeState State,
    TimeSpan Elapsed,
    TimeSpan? EstimatedRemaining,
    // Multi-stack progress for recipes with an outer `loop` block —
    // 0 / 0 when no loop is active. Mirrors Delphi SCH_VAL_LPROG / LTOT.
    int CurrentStack = 0,
    int TotalStacks = 0
);
