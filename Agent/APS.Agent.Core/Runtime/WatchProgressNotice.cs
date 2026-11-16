using System.Globalization;

namespace APS.Agent.Core.Runtime;

public sealed record WatchProgressNotice(
    string RecipeState,
    long StepSequence,
    int CurrentLoopIteration,
    int TotalLoopIterations,
    int ExecutedDepositsCount,
    double ProgressValue,
    double? TotalValue,
    string TelemetrySummary,
    string RawMessage)
{
    public string PeriodLabel => TotalLoopIterations > 0
        ? string.Create(CultureInfo.InvariantCulture, $"Period {CurrentLoopIteration}/{TotalLoopIterations}")
        : CurrentLoopIteration > 0
            ? string.Create(CultureInfo.InvariantCulture, $"Period {CurrentLoopIteration}")
            : "Period -/-";

    public static WatchProgressNotice Parse(double progress, double? total, string? message)
    {
        string raw = message ?? "";
        string state = "Running";
        long stepSeq = 0;
        int curIter = 0;
        int totIter = 0;
        int deposits = double.IsFinite(progress) ? (int)Math.Round(progress, MidpointRounding.AwayFromZero) : 0;
        var telTokens = new List<string>();

        if (!string.IsNullOrWhiteSpace(raw))
        {
            string[] parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            int idx = 0;
            if (parts.Length > 0 && !parts[0].Contains('=', StringComparison.Ordinal))
            {
                state = parts[0];
                idx = 1;
            }

            for (; idx < parts.Length; idx++)
            {
                string tok = parts[idx];
                if (tok.StartsWith("step=", StringComparison.OrdinalIgnoreCase))
                {
                    long.TryParse(tok["step=".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out stepSeq);
                }
                else if (tok.StartsWith("iter=", StringComparison.OrdinalIgnoreCase))
                {
                    string val = tok["iter=".Length..];
                    int slash = val.IndexOf('/');
                    if (slash >= 0)
                    {
                        int.TryParse(val[..slash], NumberStyles.Integer, CultureInfo.InvariantCulture, out curIter);
                        int.TryParse(val[(slash + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out totIter);
                    }
                    else
                    {
                        int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out curIter);
                    }
                }
                else if (tok.StartsWith("deposits=", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(tok["deposits=".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int dep))
                        deposits = dep;
                }
                else if (tok.StartsWith("psu=", StringComparison.OrdinalIgnoreCase)
                    || tok.StartsWith("pressure=", StringComparison.OrdinalIgnoreCase))
                {
                    telTokens.Add(tok);
                }
            }
        }

        return new WatchProgressNotice(
            state,
            stepSeq,
            curIter,
            totIter,
            deposits,
            progress,
            total,
            string.Join(" ", telTokens),
            raw);
    }
}
