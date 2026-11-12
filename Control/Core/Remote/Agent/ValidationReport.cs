using APS.RecipeModel.Validation;

namespace APS.SystemControl.Core.Remote.Agent;

/// <summary>Findings and script summary of one validation. <paramref name="XmlLineOf"/> maps a
/// script line to the line of the same text in the submitted .rcpx; it is null for bare DSL
/// content, where the script line is the only line there is.</summary>
internal sealed record ValidationReport(
    IReadOnlyList<RecipeError> Findings,
    ScriptSummary Summary,
    Func<int, int?>? XmlLineOf)
{
    internal const string PwaitTimeoutsNote =
        "pwait/swait timeout arguments (e.g. 600 s, 10 s) are upper-bound ceilings, not fixed durations; " +
        "total wall-clock run time cannot be determined without running the recipe.";

    internal const string CommentsNote =
        "All text after '#' or '//' on a line (including ';' characters) is stripped before statement " +
        "splitting and is completely inert.";

    public object ToWire() => new
    {
        valid = Findings.All(f => f.Severity != RecipeErrorSeverity.Error),
        findings = Findings.Select(FindingToWire).ToList(),
        scriptSummary = SummaryToWire(),
    };

    private Dictionary<string, object> FindingToWire(RecipeError f)
    {
        var d = new Dictionary<string, object>
        {
            ["line"] = f.Line,
            ["column"] = f.Column,
            ["severity"] = f.Severity.ToString().ToLowerInvariant(),
            ["message"] = f.Message,
        };
        if (XmlLineOf?.Invoke(f.Line) is { } xml) d["xmlLine"] = xml;
        return d;
    }

    private object SummaryToWire()
    {
        var perSource = new Dictionary<string, object>();
        foreach (var (source, s) in Summary.DepositSecondsBySource)
            perSource[source] = new
            {
                outsideLoopSeconds = Seconds(s.OutsideLoop),
                inLoopSeconds = Seconds(s.InLoop),
                totalSeconds = Seconds(s.Total),
            };

        var inert = Summary.InertCommentLinesWithSemicolon.Select(line =>
        {
            var d = new Dictionary<string, object> { ["scriptLine"] = line };
            if (XmlLineOf?.Invoke(line) is { } xml) d["xmlLine"] = xml;
            return d;
        }).ToList();

        return new
        {
            depositSecondsBySource = perSource,
            totalDepositSeconds = Seconds(Summary.TotalDepositSeconds),
            hasUnresolvedVariables = Summary.HasUnresolvedVariables,
            pwaitTimeoutsNote = PwaitTimeoutsNote,
            inertCommentLinesWithSemicolon = inert,
            commentsNote = CommentsNote,
        };
    }

    private static double Seconds(double value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
