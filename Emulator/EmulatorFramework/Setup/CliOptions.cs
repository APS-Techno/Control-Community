namespace APS.SystemEmulator.Setup;

/// <summary>Command-line options for headless/scripted startup. Everything is optional;
/// with no args the app runs TryOpenLastOrPrompt.</summary>
public sealed class CliOptions
{
    /// <summary>Project name under ConfigsRoot to open on startup, e.g. "Sputter".</summary>
    public string? Project { get; init; }

    /// <summary>.emstate file to restore after the project opens. Relative paths resolve
    /// against the active project directory.</summary>
    public string? StateFile { get; init; }

    public static CliOptions Parse(string[] args)
    {
        string? project = null, state = null;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--project", StringComparison.OrdinalIgnoreCase))
                project = args[i + 1];
            else if (string.Equals(args[i], "--state", StringComparison.OrdinalIgnoreCase))
                state = args[i + 1];
        }
        return new CliOptions { Project = project, StateFile = state };
    }
}
