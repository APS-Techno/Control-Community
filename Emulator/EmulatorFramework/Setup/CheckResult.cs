namespace APS.SystemEmulator.Setup;

public enum CheckStatus { Ok, Missing, Unknown }

public record CheckRow(
    string Name,
    CheckStatus Status,
    string Detail,
    IReadOnlyList<string> FixCommands,
    bool Fixable);
