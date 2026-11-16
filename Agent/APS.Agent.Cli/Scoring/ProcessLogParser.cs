using System.Text.RegularExpressions;

namespace APS.Agent.Cli.Scoring;

public sealed record LogStep(string Time, string Command, IReadOnlyList<string> Args, string? Result);

public static partial class ProcessLogParser
{
    public const string RecipeEvent = "@recipe";

    [GeneratedRegex(@"^(\d{2}:\d{2}:\d{2}\.\d{3})  \[Execute\]    exec: (.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex ExecRegex();

    [GeneratedRegex(@"^(\d{2}:\d{2}:\d{2}\.\d{3})  \[Execute\]    result: (.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex ResultRegex();

    [GeneratedRegex(@"^(\d{2}:\d{2}:\d{2}\.\d{3})  \[Recipe\]  Recipe (started|complete|failed|aborted|INCOMPLETE): (.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex RecipeRegex();

    public static IReadOnlyList<LogStep> Parse(string text)
    {
        var steps = new List<LogStep>();
        int pendingIndex = -1;
        foreach (string rawLine in (text ?? "").Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            var mExec = ExecRegex().Match(rawLine);
            if (mExec.Success)
            {
                string[] tokens = mExec.Groups[2].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string cmd = tokens.Length > 0 ? tokens[0] : "";
                string[] args = tokens.Length > 1 ? tokens[1..] : [];
                steps.Add(new LogStep(mExec.Groups[1].Value, cmd, args, null));
                pendingIndex = steps.Count - 1;
                continue;
            }

            var mRes = ResultRegex().Match(rawLine);
            if (mRes.Success)
            {
                if (pendingIndex >= 0)
                {
                    steps[pendingIndex] = steps[pendingIndex] with { Result = mRes.Groups[2].Value };
                    pendingIndex = -1;
                }
                continue;
            }

            var mRec = RecipeRegex().Match(rawLine);
            if (mRec.Success)
            {
                steps.Add(new LogStep(mRec.Groups[1].Value, RecipeEvent, [mRec.Groups[2].Value, mRec.Groups[3].Value], null));
                pendingIndex = -1;
            }
        }
        return steps;
    }

    public static IReadOnlyList<LogStep> SelectScoredRun(IReadOnlyList<LogStep> steps)
    {
        var runs = new List<List<LogStep>>();
        bool open = false;
        foreach (var step in steps)
        {
            if (step.Command == RecipeEvent && step.Args.Count > 0 && step.Args[0] == "started")
            {
                runs.Add([step]);
                open = true;
            }
            else if (open)
            {
                runs[^1].Add(step);
                if (step.Command == RecipeEvent)
                    open = false;
            }
        }
        for (int i = runs.Count - 1; i >= 0; i--)
        {
            if (runs[i].Any(s => string.Equals(s.Command, "deposit", StringComparison.OrdinalIgnoreCase)))
                return runs[i];
        }
        return [];
    }
}
