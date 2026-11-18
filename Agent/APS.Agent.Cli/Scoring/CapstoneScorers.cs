using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace APS.Agent.Cli.Scoring;

public sealed record CapstoneScoreResult(
    bool Pass,
    bool Completed,
    int NPeriods,
    bool EndStateClean,
    double? PeriodMeanA,
    double? RelError,
    double? CuThicknessA,
    double? WThicknessA,
    double? CuRelError,
    double? WRelError,
    IReadOnlyDictionary<string, bool> Checks,
    IReadOnlyDictionary<string, double?> Reported,
    IReadOnlyList<(string From, string To)>? Transfers = null);

public static partial class CapstoneScorers
{
    private const double MaxRelError = 0.03;
    private const double ReleaseMaxTorr = 1e-5;

    private static readonly Dictionary<string, double> TorrPerUnit = new(StringComparer.OrdinalIgnoreCase)
    {
        [""] = 1.0,
        ["torr"] = 1.0,
        ["mt"] = 1e-3,
        ["mtorr"] = 1e-3,
        ["ut"] = 1e-6,
    };

    [GeneratedRegex(@"^\s*([+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?)")]
    private static partial Regex LeadingNumberRegex();

    [GeneratedRegex(@"^([+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?)\s*([a-zA-Z]*)$")]
    private static partial Regex PressureRegex();

    [GeneratedRegex(@"^[+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?\s*(?:mT|uT|mTorr|Torr)$", RegexOptions.IgnoreCase)]
    private static partial Regex PressureWithUnitRegex();

    [GeneratedRegex(@"(?:^|\s)#.*$")]
    private static partial Regex CommentRegex();

    private sealed record DepositConditions(
        string Source,
        Dictionary<string, Dictionary<string, double?>> Supplies,
        double? PressureTorr);

    public static CapstoneScoreResult ScoreAc1(
        IReadOnlyList<LogStep> steps,
        string rcpxXml,
        JsonElement finalSystemStatus,
        IReadOnlyDictionary<string, string>? parameters = null)
    {
        const double ruRate = 0.4711;
        const double cRate = 0.4030;
        const double targetPeriod = 68.5;
        const double deadtimeRuRate = 0.4990;
        const double deadtimeS = 2.02;

        var run = ProcessLogParser.SelectScoredRun(steps);
        bool completed = run.Count > 0 && run[^1].Command == ProcessLogParser.RecipeEvent && run[^1].Args[0] == "complete";
        var numParams = NumericParams(rcpxXml, parameters);
        var deposits = ExecutedDeposits(run, numParams);
        var (adhesion, periods, cap, unpaired) = SplitAc1Layers(deposits);

        double? periodMean = null, relError = null, gamma = null, deadtimeMean = null;
        if (periods.Count > 0)
        {
            periodMean = periods.Average(p => cRate * p.TC + ruRate * p.TRu);
            if (periodMean.Value == 0.0)
                throw new InvalidOperationException("Mean period is zero; run cannot be scored.");
            relError = Math.Abs(periodMean.Value - targetPeriod) / targetPeriod;
            gamma = periods.Average(p => ruRate * p.TRu) / periodMean.Value;
            deadtimeMean = periods.Average(p => cRate * p.TC + deadtimeRuRate * (p.TRu - deadtimeS));
        }

        var conditions = ConditionsAtDeposits(run);
        var pwaits = run.Where(s => s.Command.Equals("pwait", StringComparison.OrdinalIgnoreCase) && s.Result is not null && s.Args.Count > 0).ToList();
        double? lastTarget = pwaits.Count > 0 ? ParseTorr(pwaits[^1].Args.Count >= 2 ? pwaits[^1].Args[^2] : pwaits[^1].Args[0]) : null;

        var checks = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["ru_supply_current"] = SupplyHolds(conditions, "Ru", "PSU_C", ("Current", 50.0)),
            ["c_supply_current"] = SupplyHolds(conditions, "C", "PSU_B", ("Current", 300.0)),
            ["pulse"] = SupplyHolds(conditions, "Ru", "PSU_C", ("Frequency", 50.0), ("DutyCycle", 60.0))
                     && SupplyHolds(conditions, "C", "PSU_B", ("Frequency", 50.0), ("DutyCycle", 60.0)),
            ["working_pressure"] = conditions.Count > 0 && conditions.All(d =>
                d.PressureTorr is double p && Math.Round(p, 12) >= 0.0019 && Math.Round(p, 12) <= 0.0021),
            ["pressure_with_unit"] = PressureWithUnit(rcpxXml, parameters),
            ["no_rf"] = !steps.Any(s => s.Command.Equals("set", StringComparison.OrdinalIgnoreCase)
                && s.Args.Count > 0
                && (s.Args[0].StartsWith("RF", StringComparison.OrdinalIgnoreCase)
                    || s.Args[0].StartsWith("PSU_RF", StringComparison.OrdinalIgnoreCase))),
            ["throttle_released"] = lastTarget is double lt && Math.Round(lt, 12) <= ReleaseMaxTorr,
        };

        bool endClean = EndStateClean(finalSystemStatus, ["Main"]);
        bool pass = completed && periods.Count == 5 && endClean
                 && relError is <= MaxRelError && checks.Values.All(v => v);

        var reported = new Dictionary<string, double?>(StringComparer.Ordinal)
        {
            ["gamma"] = gamma,
            ["adhesion_A"] = ruRate * adhesion.Sum(),
            ["cap_A"] = cap is double cv ? cRate * cv : null,
            ["unpaired_deposits"] = unpaired,
            ["deadtime_period_mean_A"] = deadtimeMean,
        };
        return new CapstoneScoreResult(pass, completed, periods.Count, endClean, periodMean, relError, null, null, null, null, checks, reported);
    }

    public static CapstoneScoreResult ScoreSc1(
        IReadOnlyList<LogStep> steps,
        string rcpxXml,
        JsonElement finalSystemStatus,
        IReadOnlyDictionary<string, string>? parameters = null)
    {
        const double moRate = 0.800;
        const double sio2Rate = 0.400;
        const double targetPeriod = 60.0;

        var run = ProcessLogParser.SelectScoredRun(steps);
        bool completed = run.Count > 0 && run[^1].Command == ProcessLogParser.RecipeEvent && run[^1].Args[0] == "complete";
        var numParams = NumericParams(rcpxXml, parameters);
        var deposits = ExecutedDeposits(run, numParams);
        var (periods, unpaired) = SplitPairs(deposits, "Mo", "SiO2");

        double? periodMean = null, relError = null, gamma = null;
        if (periods.Count > 0)
        {
            periodMean = periods.Average(p => moRate * p.First + sio2Rate * p.Second);
            if (periodMean.Value == 0.0)
                throw new InvalidOperationException("Mean period is zero; run cannot be scored.");
            relError = Math.Abs(periodMean.Value - targetPeriod) / targetPeriod;
            gamma = periods.Average(p => moRate * p.First) / periodMean.Value;
        }

        var conditions = ConditionsAtDeposits(run);
        var checks = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["mo_supply_current"] = SupplyHolds(conditions, "Mo", "PSU1", ("Current", 200.0)),
            ["mo_pulse"] = SupplyHolds(conditions, "Mo", "PSU1", ("Frequency", 50.0), ("DutyCycle", 60.0)),
            ["sio2_supply_power"] = SupplyHolds(conditions, "SiO2", "PSU2", ("Power", 150.0)),
            ["sputter_2arg_deposit"] = AllDepositsHaveTwoArgs(rcpxXml),
            ["no_pwait"] = !run.Any(s => s.Command.Equals("pwait", StringComparison.OrdinalIgnoreCase)),
        };

        bool endClean = EndStateClean(finalSystemStatus, ["Main"]);
        bool pass = completed && periods.Count == 5 && endClean
                 && relError is <= MaxRelError && checks.Values.All(v => v);

        var reported = new Dictionary<string, double?>(StringComparer.Ordinal)
        {
            ["gamma"] = gamma,
            ["unpaired_deposits"] = unpaired,
        };
        return new CapstoneScoreResult(pass, completed, periods.Count, endClean, periodMean, relError, null, null, null, null, checks, reported);
    }

    public static CapstoneScoreResult ScoreCc1(
        IReadOnlyList<LogStep> steps,
        string rcpxXml,
        JsonElement finalSystemStatus,
        IReadOnlyDictionary<string, string>? parameters = null)
    {
        const double cuRate = 1.250;
        const double wRate = 0.750;
        const double targetCu = 100.0;
        const double targetW = 150.0;

        var run = ProcessLogParser.SelectScoredRun(steps);
        bool completed = run.Count > 0 && run[^1].Command == ProcessLogParser.RecipeEvent && run[^1].Args[0] == "complete";
        var numParams = NumericParams(rcpxXml, parameters);
        var deposits = ExecutedDeposits(run, numParams);

        var cuDwells = deposits.Where(d => d.Source == "C1_Cu").Select(d => d.Seconds).ToList();
        var wDwells = deposits.Where(d => d.Source == "W").Select(d => d.Seconds).ToList();
        if ((cuDwells.Count > 0 && cuDwells.Sum() == 0.0) || (wDwells.Count > 0 && wDwells.Sum() == 0.0))
            throw new InvalidOperationException("Deposit dwell time is zero; run cannot be scored.");

        double? cuThick = cuDwells.Count > 0 ? cuRate * cuDwells.Sum() : null;
        double? wThick = wDwells.Count > 0 ? wRate * wDwells.Sum() : null;
        double? cuErr = cuThick is double cu ? Math.Abs(cu - targetCu) / targetCu : null;
        double? wErr = wThick is double w ? Math.Abs(w - targetW) / targetW : null;

        var transfers = run
            .Where(s => s.Command.Equals("transfer", StringComparison.OrdinalIgnoreCase) && s.Result is not null && s.Args.Count >= 2)
            .Select(s => (s.Args[0], s.Args[1]))
            .ToList();
        (string, string)[] expectedTransfers = [("LL", "C1"), ("C1", "C2"), ("C2", "LL")];

        var conditions = ConditionsAtDeposits(run);
        var ctrlPressures = ControllerPressuresAtDeposits(run);
        var lastTargets = LastControllerTargets(run);

        var checks = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["transfer_sequence"] = transfers.SequenceEqual(expectedTransfers),
            ["cu_supply"] = SupplyHolds(conditions, "C1_Cu", "C1_PSU1", ("Current", 250.0), ("Frequency", 50.0), ("DutyCycle", 60.0)),
            ["w_supply"] = SupplyHolds(conditions, "W", "C2_PSU", ("Current", 200.0), ("Frequency", 50.0), ("DutyCycle", 60.0)),
            ["c1_working_pressure"] = ctrlPressures["C1_Cu"].Count > 0 && ctrlPressures["C1_Cu"].All(p =>
                p is double v && Math.Round(v, 12) >= 0.00285 && Math.Round(v, 12) <= 0.00315),
            ["c2_working_pressure"] = ctrlPressures["W"].Count > 0 && ctrlPressures["W"].All(p =>
                p is double v && Math.Round(v, 12) >= 0.00190 && Math.Round(v, 12) <= 0.00210),
            ["throttles_released"] = new[] { "C1_TC", "C2_TC" }.All(ctrl =>
                lastTargets.TryGetValue(ctrl, out double? val) && val is double v && Math.Round(v, 12) <= ReleaseMaxTorr),
            ["pressure_with_unit"] = PressureWithUnit(rcpxXml, parameters),
        };

        bool endClean = EndStateClean(finalSystemStatus, ["TM", "LL", "C1", "C2", "C3", "C4"]);
        bool pass = completed && cuDwells.Count == 1 && wDwells.Count == 1 && endClean
                 && cuErr is <= MaxRelError && wErr is <= MaxRelError
                 && checks.Values.All(v => v);

        var reported = new Dictionary<string, double?>(StringComparer.Ordinal)
        {
            ["n_cu_deposits"] = cuDwells.Count,
            ["n_w_deposits"] = wDwells.Count,
        };
        return new CapstoneScoreResult(pass, completed, 0, endClean, null, null, cuThick, wThick, cuErr, wErr, checks, reported, transfers);
    }

    private static Dictionary<string, string> RawParams(string rcpxXml, IReadOnlyDictionary<string, string>? overrides)
    {
        var doc = XDocument.Parse(rcpxXml);
        var raw = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in doc.Descendants("Param"))
        {
            string? name = p.Attribute("name")?.Value;
            if (!string.IsNullOrEmpty(name))
                raw[name] = (p.Value ?? "").Trim();
        }
        if (overrides is not null)
        {
            foreach (var kv in overrides)
                raw[kv.Key] = kv.Value.Trim();
        }
        return raw;
    }

    private static Dictionary<string, double> NumericParams(string rcpxXml, IReadOnlyDictionary<string, string>? overrides)
    {
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var kv in RawParams(rcpxXml, overrides))
        {
            if (double.TryParse(kv.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                result[kv.Key] = v;
        }
        return result;
    }

    private static List<(string Source, double Seconds)> ExecutedDeposits(
        IReadOnlyList<LogStep> run,
        IReadOnlyDictionary<string, double> numParams)
    {
        var list = new List<(string, double)>();
        foreach (var s in run)
        {
            if (!s.Command.Equals("deposit", StringComparison.OrdinalIgnoreCase) || s.Result is null || s.Args.Count < 2)
                continue;
            string tok = s.Args[1];
            double sec;
            if (!double.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out sec))
            {
                string key = tok.StartsWith('$') ? tok[1..] : tok;
                if (!numParams.TryGetValue(key, out sec))
                    throw new InvalidOperationException($"Deposit dwell '{tok}' is neither a number nor a recipe parameter.");
            }
            list.Add((s.Args[0], sec));
        }
        return list;
    }

    private static (List<double> Adhesion, List<(double TC, double TRu)> Periods, double? Cap, int Unpaired) SplitAc1Layers(
        List<(string Source, double Seconds)> deposits)
    {
        if (deposits.Count >= 3 && deposits[0].Source == "Ru" && deposits.Count % 2 == 1
            && Enumerable.Range(0, (deposits.Count - 1) / 2).All(k =>
                deposits[1 + 2 * k].Source == "Ru" && deposits[2 + 2 * k].Source == "C"))
        {
            var adh = new List<double> { deposits[0].Seconds };
            var per = Enumerable.Range(0, (deposits.Count - 1) / 2)
                .Select(k => (deposits[2 + 2 * k].Seconds, deposits[1 + 2 * k].Seconds))
                .ToList();
            return (adh, per, null, 0);
        }

        int firstC = deposits.FindIndex(d => d.Source == "C");
        if (firstC < 0) firstC = deposits.Count;
        var adhesion = deposits.Take(firstC).Where(d => d.Source == "Ru").Select(d => d.Seconds).ToList();
        int unpaired = firstC - adhesion.Count;
        var periods = new List<(double, double)>();
        double? cap = null;
        int i = firstC;
        while (i < deposits.Count)
        {
            var (src, t) = deposits[i];
            if (src == "C" && i + 1 < deposits.Count && deposits[i + 1].Source == "Ru")
            {
                periods.Add((t, deposits[i + 1].Seconds));
                i += 2;
                continue;
            }
            if (src == "C" && i == deposits.Count - 1)
                cap = t;
            else
                unpaired++;
            i++;
        }
        return (adhesion, periods, cap, unpaired);
    }

    private static (List<(double First, double Second)> Pairs, int Unpaired) SplitPairs(
        List<(string Source, double Seconds)> deposits,
        string first,
        string second)
    {
        var pairs = new List<(double, double)>();
        int unpaired = 0;
        int i = 0;
        while (i < deposits.Count)
        {
            if (i + 1 < deposits.Count)
            {
                var (s1, t1) = deposits[i];
                var (s2, t2) = deposits[i + 1];
                if (s1 == first && s2 == second) { pairs.Add((t1, t2)); i += 2; continue; }
                if (s1 == second && s2 == first) { pairs.Add((t2, t1)); i += 2; continue; }
            }
            unpaired++;
            i++;
        }
        return (pairs, unpaired);
    }

    private static List<DepositConditions> ConditionsAtDeposits(IReadOnlyList<LogStep> run)
    {
        var supplies = new Dictionary<string, Dictionary<string, double?>>(StringComparer.Ordinal);
        double? pressure = null;
        var found = new List<DepositConditions>();
        foreach (var s in run)
        {
            if (s.Result is null) continue;
            string cmd = s.Command.ToLowerInvariant();
            if (cmd == "set" && s.Args.Count >= 3)
            {
                if (!supplies.TryGetValue(s.Args[0], out var map))
                    supplies[s.Args[0]] = map = new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase);
                map[s.Args[1].ToLowerInvariant()] = ParseLeadingNumber(s.Args[2]);
            }
            else if (cmd == "pwait" && s.Args.Count > 0)
            {
                pressure = ParseTorr(s.Args.Count >= 2 ? s.Args[^2] : s.Args[0]);
            }
            else if (cmd == "deposit" && s.Args.Count > 0)
            {
                var copy = supplies.ToDictionary(kv => kv.Key, kv => new Dictionary<string, double?>(kv.Value, StringComparer.OrdinalIgnoreCase), StringComparer.Ordinal);
                found.Add(new DepositConditions(s.Args[0], copy, pressure));
            }
        }
        return found;
    }

    private static Dictionary<string, List<double?>> ControllerPressuresAtDeposits(IReadOnlyList<LogStep> run)
    {
        var active = new Dictionary<string, double?>(StringComparer.Ordinal);
        var atSource = new Dictionary<string, List<double?>>(StringComparer.Ordinal) { ["C1_Cu"] = [], ["W"] = [] };
        foreach (var s in run)
        {
            if (s.Result is null || s.Args.Count == 0) continue;
            string cmd = s.Command.ToLowerInvariant();
            if (cmd == "pwait" && s.Args.Count >= 3)
                active[NormalizeClusterController(s.Args[0])] = ParseTorr(s.Args[1]);
            else if (cmd == "deposit" && atSource.TryGetValue(s.Args[0], out var list))
            {
                string ctrl = s.Args[0] == "C1_Cu" ? "C1_TC" : "C2_TC";
                list.Add(active.GetValueOrDefault(ctrl));
            }
        }
        return atSource;
    }

    private static Dictionary<string, double?> LastControllerTargets(IReadOnlyList<LogStep> run)
    {
        var last = new Dictionary<string, double?>(StringComparer.Ordinal);
        foreach (var s in run)
        {
            if (s.Result is not null && s.Command.Equals("pwait", StringComparison.OrdinalIgnoreCase) && s.Args.Count >= 3)
                last[NormalizeClusterController(s.Args[0])] = ParseTorr(s.Args[1]);
        }
        return last;
    }

    private static string NormalizeClusterController(string token) =>
        token switch
        {
            "C1_pm" or "C1_TV" => "C1_TC",
            "C2_pm" or "C2_TV" => "C2_TC",
            "C3_pm" or "C3_TV" => "C3_TC",
            "C4_pm" or "C4_TV" => "C4_TC",
            _ => token,
        };

    private static bool SupplyHolds(
        List<DepositConditions> deposits,
        string source,
        string supply,
        params (string Param, double Value)[] settings)
    {
        var mine = deposits.Where(d => d.Source == source).ToList();
        return mine.Count > 0 && mine.All(d =>
            d.Supplies.TryGetValue(supply, out var map)
            && settings.All(kv => map.TryGetValue(kv.Param.ToLowerInvariant(), out double? v) && v == kv.Value));
    }

    private static bool AllDepositsHaveTwoArgs(string rcpxXml)
    {
        var doc = XDocument.Parse(rcpxXml);
        string script = string.Concat(doc.Descendants("Script").Select(s => s.Value));
        bool seen = false;
        foreach (string rawLine in script.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            string line = CommentRegex().Replace(rawLine, "").Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith(';')) continue;
            string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length > 0 && tokens[0].Equals("deposit", StringComparison.OrdinalIgnoreCase))
            {
                seen = true;
                if (tokens.Length != 3) return false;
            }
        }
        return seen;
    }

    private static bool PressureWithUnit(string rcpxXml, IReadOnlyDictionary<string, string>? overrides)
    {
        var raw = RawParams(rcpxXml, overrides);
        var doc = XDocument.Parse(rcpxXml);
        string script = string.Concat(doc.Descendants("Script").Select(s => s.Value));
        bool seen = false;
        foreach (string line in script.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            string stripped = CommentRegex().Replace(line, "");
            foreach (string stmt in stripped.Split(';'))
            {
                string[] tokens = stmt.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 0) continue;
                string cmd = tokens[0].ToLowerInvariant();
                if (cmd != "pwait" && cmd != "swait") continue;
                seen = true;
                if (tokens.Length < 3) return false;
                string token = tokens[^2];
                string name = token.StartsWith('$') ? token[1..] : token;
                string val = raw.TryGetValue(name, out var paramVal) ? paramVal.Trim() : token.Trim();
                if (cmd == "pwait" && ParseLeadingNumber(val) == 0.0) continue;
                if (!PressureWithUnitRegex().IsMatch(val)) return false;
            }
        }
        return seen;
    }

    private static bool EndStateClean(JsonElement status, string[] requiredChambers)
    {
        if (status.ValueKind != JsonValueKind.Object) return false;
        if (!status.TryGetProperty("isRecipeRunning", out var rr) || rr.ValueKind != JsonValueKind.False) return false;
        if (!status.TryGetProperty("activeAlarms", out var aa) || aa.ValueKind != JsonValueKind.Array || aa.GetArrayLength() != 0) return false;
        if (!status.TryGetProperty("chambers", out var ch) || ch.ValueKind != JsonValueKind.Array) return false;

        var states = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in ch.EnumerateArray())
        {
            string? id = item.TryGetProperty("id", out var idP) ? idP.GetString() : null;
            string? st = item.TryGetProperty("state", out var stP) ? stP.GetString() : null;
            if (id is not null && st is not null)
                states[id] = st;
        }
        return requiredChambers.All(c => states.TryGetValue(c, out var st) && st == "Idle");
    }

    private static double? ParseLeadingNumber(string token)
    {
        var m = LeadingNumberRegex().Match(token);
        return m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
            ? v
            : null;
    }

    private static double? ParseTorr(string token)
    {
        var m = PressureRegex().Match(token.Trim());
        if (!m.Success || !TorrPerUnit.TryGetValue(m.Groups[2].Value, out double mult))
            return null;
        return double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
            ? v * mult
            : null;
    }
}
