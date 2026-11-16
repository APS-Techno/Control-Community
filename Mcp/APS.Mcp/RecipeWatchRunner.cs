using System.Globalization;
using System.Text;
using System.Text.Json;
using APS.Wire;
using ModelContextProtocol;

namespace APS.Mcp;

internal sealed record WatchOptions(
    double PollIntervalSeconds,
    double MaxWaitSeconds,
    int? WakeAfterDeposits,
    bool WakeOnEveryStep,
    double? MinDischargeCurrentMa,
    double? MaxVoltageDriftPct,
    double? MaxCurrentDriftPct,
    double? MaxPressureDriftPct);

internal sealed class RecipeWatchRunner(
    IWireCaller wire,
    Func<TimeSpan, CancellationToken, Task> delay,
    TimeProvider timeProvider,
    string telemetryDir)
{
    private const int MaxReceiptBytes = 1024;
    private static readonly JsonSerializerOptions CompactJson = new();

    private sealed class ScalarStats
    {
        public double Min { get; private set; } = double.PositiveInfinity;
        public double Max { get; private set; } = double.NegativeInfinity;
        public double Sum { get; private set; }
        public int Count { get; private set; }

        public void Add(double value)
        {
            if (value < Min) Min = value;
            if (value > Max) Max = value;
            Sum += value;
            Count++;
        }

        public object ToDto(int decimals) => new
        {
            min = Math.Round(Min, decimals),
            max = Math.Round(Max, decimals),
            mean = Math.Round(Sum / Count, decimals),
        };
    }

    private sealed class PsuAccumulator
    {
        public readonly ScalarStats VoltageV = new();
        public readonly ScalarStats CurrentMa = new();
        public readonly ScalarStats PowerW = new();
    }

    private sealed class PsuBaseline(double setCurrentMa, double setPowerW, double voltageV, double currentMa)
    {
        public double SetCurrentMa { get; } = setCurrentMa;
        public double SetPowerW { get; } = setPowerW;
        public double VoltageV { get; } = voltageV;
        public double CurrentMa { get; } = currentMa;
    }

    private sealed record PsuSample(string Id, double VoltageV, double CurrentMa, double PowerW, bool SupportsCurrent, double SetCurrentMa, double SetPowerW);
    private sealed record PressureSample(string Chamber, string Sensor, double PressureTorr);
    private sealed record AlarmSample(string Chamber, string Message);
    private sealed record LoopSample(int Iteration, int Total);

    public async Task<string> RunAsync(
        WatchOptions opts,
        IProgress<ProgressNotificationValue>? progress,
        CancellationToken ct)
    {
        if (opts.WakeAfterDeposits is <= 0)
            throw new McpException("bad_request: wake_after_deposits must be >= 1");
        if (!double.IsFinite(opts.PollIntervalSeconds) || !double.IsFinite(opts.MaxWaitSeconds))
            throw new McpException("bad_request: poll_interval_seconds and max_wait_seconds must be finite numbers");

        double pollIntervalS = Math.Clamp(opts.PollIntervalSeconds, 0.5, 30.0);
        double maxWaitS = Math.Clamp(opts.MaxWaitSeconds, 1.0, 1800.0);

        Directory.CreateDirectory(telemetryDir);
        DateTimeOffset startUtc = timeProvider.GetUtcNow();
        string tsvName = $"watch-{startUtc:yyyyMMdd_HHmmss_fff}-{Guid.NewGuid().ToString("N")[..6]}.tsv";
        string tsvPath = Path.Combine(telemetryDir, tsvName);
        await using var writer = new StreamWriter(tsvPath, append: false, new UTF8Encoding(false));
        await writer.WriteLineAsync("timestampUtc\telapsedS\trecipeState\tstepSequence\tloopIteration\texecutedDepositsCount\tchamber\tsensor\tpressureTorr\tpsuId\tvoltageV\tcurrentMa\tpowerW");

        var psuStats = new Dictionary<string, PsuAccumulator>(StringComparer.Ordinal);
        var pressureStats = new Dictionary<string, (string Sensor, ScalarStats Stats)>(StringComparer.Ordinal);
        var psuBaselines = new Dictionary<string, PsuBaseline>(StringComparer.Ordinal);
        var pressureBaselines = new Dictionary<string, double>(StringComparer.Ordinal);

        int startDeposits = 0;
        long startStepSeq = 0;
        string startOutcomeKey = "";
        HashSet<string>? initialAlarms = null;
        long currentDepositStepSeq = -1;
        int depositSettledCount = 0;
        string? lastActivePsu = null;
        string? lastActiveChamber = null;

        int pollIndex = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            JsonElement status = await CallStatusAsync(ct).ConfigureAwait(false);
            DateTimeOffset nowUtc = timeProvider.GetUtcNow();
            double elapsedS = Math.Max(0.0, (nowUtc - startUtc).TotalSeconds);

            bool isRunning = status.TryGetProperty("isRecipeRunning", out var rProp) && rProp.ValueKind == JsonValueKind.True;
            long stepSeq = status.TryGetProperty("stepSequence", out var seqProp) && seqProp.TryGetInt64(out long sVal) ? sVal : 0L;
            int executedDeposits = status.TryGetProperty("executedDepositsCount", out var depProp) && depProp.TryGetInt32(out int dVal) ? dVal : 0;
            int loopIter = status.TryGetProperty("currentLoopIteration", out var iterProp) && iterProp.TryGetInt32(out int iVal) ? iVal : 0;
            var loops = ReadLoops(status);
            var (recipeState, stepDesc) = ReadProgressState(status);
            var (outcomeKey, outcomeState, outcomeMessage) = ReadOutcome(status);
            var alarms = ReadAlarms(status);
            var (psus, pressures) = ReadTelemetry(status);

            if (pollIndex == 0)
            {
                startDeposits = executedDeposits;
                startStepSeq = stepSeq;
                startOutcomeKey = outcomeKey;
                initialAlarms = alarms.Select(a => $"{a.Chamber}\0{a.Message}").ToHashSet(StringComparer.Ordinal);
            }

            await WriteTsvRowsAsync(writer, nowUtc, elapsedS, recipeState, stepSeq, loopIter, executedDeposits, psus, pressures).ConfigureAwait(false);
            await writer.FlushAsync(ct).ConfigureAwait(false);

            foreach (var p in psus)
            {
                if (!psuStats.TryGetValue(p.Id, out var acc))
                    psuStats[p.Id] = acc = new PsuAccumulator();
                acc.VoltageV.Add(p.VoltageV);
                acc.CurrentMa.Add(p.CurrentMa);
                acc.PowerW.Add(p.PowerW);
                lastActivePsu = p.Id;
            }

            bool isDepositStep = stepDesc.StartsWith("deposit ", StringComparison.OrdinalIgnoreCase);
            bool isPwaitStep = stepDesc.StartsWith("pwait ", StringComparison.OrdinalIgnoreCase);
            var newAlarms = alarms.Where(a => initialAlarms is not null && !initialAlarms.Contains($"{a.Chamber}\0{a.Message}")).ToList();
            var alarmChambers = newAlarms.Select(a => a.Chamber).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var pr in pressures)
            {
                if (isDepositStep || isPwaitStep || alarmChambers.Contains(pr.Chamber))
                {
                    if (!pressureStats.TryGetValue(pr.Chamber, out var entry))
                        pressureStats[pr.Chamber] = entry = (pr.Sensor, new ScalarStats());
                    entry.Stats.Add(pr.PressureTorr);
                    lastActiveChamber = pr.Chamber;
                }
            }

            int loopTotal = loops.Count > 0 ? loops[^1].Total : 0;
            string iterToken = loopTotal > 0
                ? string.Create(CultureInfo.InvariantCulture, $"{loopIter}/{loopTotal}")
                : loopIter.ToString(CultureInfo.InvariantCulture);
            string telSuffix = FormatTelemetryBrief(psus, pressures);
            float? progressTotal = opts.WakeAfterDeposits is int targetDelta
                ? startDeposits + targetDelta
                : loopTotal > 0 ? loopTotal : null;
            progress?.Report(new ProgressNotificationValue
            {
                Progress = executedDeposits,
                Total = progressTotal,
                Message = string.Create(CultureInfo.InvariantCulture, $"{recipeState} step={stepSeq} iter={iterToken} deposits={executedDeposits}{telSuffix}"),
            });

            string? wakeReason = DetectTerminalWake(
                isRunning, recipeState, outcomeKey != startOutcomeKey, outcomeState, outcomeMessage, newAlarms);

            if (wakeReason is null)
            {
                if (!isDepositStep)
                {
                    currentDepositStepSeq = -1;
                    depositSettledCount = 0;
                    pressureBaselines.Clear();
                }
                else
                {
                    if (stepSeq != currentDepositStepSeq)
                    {
                        currentDepositStepSeq = stepSeq;
                        depositSettledCount = 0;
                        pressureBaselines.Clear();
                    }

                    depositSettledCount++;
                    if (depositSettledCount >= 2)
                    {
                        wakeReason = CheckDischargeAndDrift(
                            opts, psus, pressures, psuBaselines, pressureBaselines);
                    }
                }
            }

            if (wakeReason is null && opts.WakeAfterDeposits is int delta && executedDeposits - startDeposits >= delta)
                wakeReason = "deposit_milestone";
            if (wakeReason is null && opts.WakeOnEveryStep && pollIndex > 0 && stepSeq > startStepSeq)
                wakeReason = "step_changed";
            if (wakeReason is null && elapsedS >= maxWaitS)
                wakeReason = "nominal_timeout";

            if (wakeReason is not null)
            {
                return BuildBoundedReceipt(
                    wakeReason, elapsedS, recipeState, stepSeq, loops, loopIter, executedDeposits,
                    psuStats, pressureStats, lastActivePsu, lastActiveChamber, tsvPath);
            }

            double remainingS = maxWaitS - elapsedS;
            TimeSpan stepDelay = TimeSpan.FromSeconds(Math.Min(pollIntervalS, Math.Max(0.01, remainingS)));
            await delay(stepDelay, ct).ConfigureAwait(false);
            pollIndex++;
        }
    }

    private async Task<JsonElement> CallStatusAsync(CancellationToken ct)
    {
        try
        {
            return await wire.CallAsync("recipe.status", null, ct).ConfigureAwait(false);
        }
        catch (WireException ex)
        {
            throw new McpException(ex.Message);
        }
    }

    private static string? DetectTerminalWake(
        bool isRunning,
        string recipeState,
        bool outcomeChanged,
        string outcomeState,
        string outcomeMessage,
        List<AlarmSample> newAlarms)
    {
        if (newAlarms.Count > 0) return "fault_or_alarm";
        if (outcomeChanged && string.Equals(outcomeState, "Error", StringComparison.OrdinalIgnoreCase)) return "fault_or_alarm";
        if (!isRunning && string.Equals(outcomeState, "Error", StringComparison.OrdinalIgnoreCase)) return "fault_or_alarm";
        if (string.Equals(recipeState, "Error", StringComparison.OrdinalIgnoreCase)) return "fault_or_alarm";

        if (outcomeChanged && string.Equals(outcomeMessage, "Aborted", StringComparison.OrdinalIgnoreCase)) return "aborted";
        if (string.Equals(recipeState, "Aborting", StringComparison.OrdinalIgnoreCase)) return "aborted";
        if (!isRunning && string.Equals(outcomeMessage, "Aborted", StringComparison.OrdinalIgnoreCase)) return "aborted";

        if (string.Equals(recipeState, "Paused", StringComparison.OrdinalIgnoreCase)) return "paused";
        if (!isRunning || (outcomeChanged && string.Equals(outcomeState, "Complete", StringComparison.OrdinalIgnoreCase)))
            return "completed";

        return null;
    }

    private static string? CheckDischargeAndDrift(
        WatchOptions opts,
        List<PsuSample> psus,
        List<PressureSample> pressures,
        Dictionary<string, PsuBaseline> psuBaselines,
        Dictionary<string, double> pressureBaselines)
    {
        foreach (var p in psus)
        {
            if (opts.MinDischargeCurrentMa is double minMa && p.SupportsCurrent && p.CurrentMa < minMa)
                return "discharge_drop";

            if (!psuBaselines.TryGetValue(p.Id, out var b)
                || Math.Abs(b.SetCurrentMa - p.SetCurrentMa) > 1e-6
                || Math.Abs(b.SetPowerW - p.SetPowerW) > 1e-6)
            {
                psuBaselines[p.Id] = new PsuBaseline(p.SetCurrentMa, p.SetPowerW, p.VoltageV, p.CurrentMa);
                continue;
            }

            if (opts.MaxVoltageDriftPct is double maxV && ExceedsDrift(p.VoltageV, b.VoltageV, maxV))
                return "telemetry_drift";
            if (opts.MaxCurrentDriftPct is double maxC && p.SupportsCurrent && ExceedsDrift(p.CurrentMa, b.CurrentMa, maxC))
                return "telemetry_drift";
        }

        if (opts.MaxPressureDriftPct is double maxP)
        {
            foreach (var pr in pressures)
            {
                if (!pressureBaselines.TryGetValue(pr.Chamber, out double baseP))
                {
                    pressureBaselines[pr.Chamber] = pr.PressureTorr;
                    continue;
                }
                if (ExceedsDrift(pr.PressureTorr, baseP, maxP))
                    return "telemetry_drift";
            }
        }

        return null;
    }

    private static bool ExceedsDrift(double current, double baseline, double limitPct)
    {
        if (Math.Abs(baseline) <= 1e-12) return false;
        double pct = Math.Abs(current - baseline) / Math.Abs(baseline) * 100.0;
        return pct > limitPct;
    }

    private static string BuildBoundedReceipt(
        string wakeReason,
        double elapsedS,
        string recipeState,
        long stepSequence,
        List<LoopSample> loops,
        int loopIteration,
        int executedDeposits,
        Dictionary<string, PsuAccumulator> psuStats,
        Dictionary<string, (string Sensor, ScalarStats Stats)> pressureStats,
        string? lastActivePsu,
        string? lastActiveChamber,
        string tsvPath)
    {
        var innerLoops = loops.TakeLast(3).Select(f => new { iteration = f.Iteration, total = f.Total }).ToArray();
        var psuDict = psuStats.ToDictionary(
            kv => kv.Key,
            kv => (object)new
            {
                voltageV = kv.Value.VoltageV.ToDto(1),
                currentMa = kv.Value.CurrentMa.ToDto(1),
                powerW = kv.Value.PowerW.ToDto(1),
            },
            StringComparer.Ordinal);
        var pressDict = pressureStats.ToDictionary(
            kv => kv.Key,
            kv =>
            {
                var dto = new
                {
                    sensor = kv.Value.Sensor,
                    unit = "Torr",
                    min = Math.Round(kv.Value.Stats.Min, 6),
                    max = Math.Round(kv.Value.Stats.Max, 6),
                    mean = Math.Round(kv.Value.Stats.Sum / kv.Value.Stats.Count, 6),
                };
                return (object)dto;
            },
            StringComparer.Ordinal);

        string json = SerializeReceipt(
            wakeReason, elapsedS, recipeState, stepSequence, innerLoops, loopIteration,
            executedDeposits, psuDict, pressDict, tsvPath, truncated: false);
        if (Encoding.UTF8.GetByteCount(json) < MaxReceiptBytes)
            return json;

        var trimmedPsu = new Dictionary<string, object>(StringComparer.Ordinal);
        if (lastActivePsu is not null && psuDict.TryGetValue(lastActivePsu, out var ap))
            trimmedPsu[lastActivePsu] = ap;
        else if (psuDict.Count > 0)
        {
            var first = psuDict.First();
            trimmedPsu[first.Key] = first.Value;
        }

        var trimmedPress = new Dictionary<string, object>(StringComparer.Ordinal);
        if (lastActiveChamber is not null && pressDict.TryGetValue(lastActiveChamber, out var ac))
            trimmedPress[lastActiveChamber] = ac;
        else if (pressDict.Count > 0)
        {
            var first = pressDict.First();
            trimmedPress[first.Key] = first.Value;
        }

        string trimmed = SerializeReceipt(
            wakeReason, elapsedS, recipeState, stepSequence, innerLoops, loopIteration,
            executedDeposits, trimmedPsu, trimmedPress, tsvPath, truncated: true);
        if (Encoding.UTF8.GetByteCount(trimmed) < MaxReceiptBytes)
            return trimmed;

        return SerializeReceipt(
            wakeReason, elapsedS, recipeState, stepSequence, innerLoops, loopIteration,
            executedDeposits, trimmedPsu, trimmedPress, Path.GetFileName(tsvPath), truncated: true);
    }

    private static string SerializeReceipt(
        string wakeReason,
        double elapsedS,
        string recipeState,
        long stepSequence,
        object[] loopStack,
        int currentLoopIteration,
        int executedDepositsCount,
        Dictionary<string, object> psuSummary,
        Dictionary<string, object> pressureSummary,
        string telemetryTsvPath,
        bool truncated)
    {
        if (truncated)
        {
            return JsonSerializer.Serialize(new
            {
                wakeReason,
                elapsedSeconds = Math.Round(elapsedS, 1),
                recipeState,
                stepSequence,
                loopStack,
                currentLoopIteration,
                executedDepositsCount,
                psuSummary,
                pressureSummary,
                telemetryTsvPath,
                truncated = true,
            }, CompactJson);
        }

        return JsonSerializer.Serialize(new
        {
            wakeReason,
            elapsedSeconds = Math.Round(elapsedS, 1),
            recipeState,
            stepSequence,
            loopStack,
            currentLoopIteration,
            executedDepositsCount,
            psuSummary,
            pressureSummary,
            telemetryTsvPath,
        }, CompactJson);
    }

    private static async Task WriteTsvRowsAsync(
        StreamWriter writer,
        DateTimeOffset nowUtc,
        double elapsedS,
        string recipeState,
        long stepSeq,
        int loopIter,
        int deposits,
        List<PsuSample> psus,
        List<PressureSample> pressures)
    {
        string ts = nowUtc.ToString("O", CultureInfo.InvariantCulture);
        string el = elapsedS.ToString("F2", CultureInfo.InvariantCulture);
        string seq = stepSeq.ToString(CultureInfo.InvariantCulture);
        string iter = loopIter.ToString(CultureInfo.InvariantCulture);
        string dep = deposits.ToString(CultureInfo.InvariantCulture);

        if (psus.Count == 0 && pressures.Count == 0)
        {
            await writer.WriteLineAsync($"{ts}\t{el}\t{recipeState}\t{seq}\t{iter}\t{dep}\t\t\t\t\t\t\t").ConfigureAwait(false);
            return;
        }

        int rows = Math.Max(psus.Count, pressures.Count);
        for (int idx = 0; idx < rows; idx++)
        {
            PressureSample? pr = idx < pressures.Count ? pressures[idx] : (pressures.Count == 1 ? pressures[0] : null);
            PsuSample? pu = idx < psus.Count ? psus[idx] : (psus.Count == 1 ? psus[0] : null);

            string ch = pr?.Chamber ?? "";
            string sn = pr?.Sensor ?? "";
            string pt = pr is null ? "" : pr.PressureTorr.ToString("G6", CultureInfo.InvariantCulture);
            string pid = pu?.Id ?? "";
            string pv = pu is null ? "" : pu.VoltageV.ToString("F3", CultureInfo.InvariantCulture);
            string pc = pu is null ? "" : pu.CurrentMa.ToString("F3", CultureInfo.InvariantCulture);
            string pw = pu is null ? "" : pu.PowerW.ToString("F3", CultureInfo.InvariantCulture);

            await writer.WriteLineAsync($"{ts}\t{el}\t{recipeState}\t{seq}\t{iter}\t{dep}\t{ch}\t{sn}\t{pt}\t{pid}\t{pv}\t{pc}\t{pw}").ConfigureAwait(false);
        }
    }

    private static (string State, string StepDescription) ReadProgressState(JsonElement status)
    {
        if (!status.TryGetProperty("progress", out var p) || p.ValueKind != JsonValueKind.Object)
            return ("Idle", "");
        string desc = p.TryGetProperty("stepDescription", out var sd) && sd.ValueKind == JsonValueKind.String
            ? sd.GetString() ?? ""
            : "";
        string state = "Idle";
        if (p.TryGetProperty("state", out var st))
        {
            state = st.ValueKind switch
            {
                JsonValueKind.Number when st.TryGetInt32(out int code) => code switch
                {
                    0 => "Idle",
                    1 => "Running",
                    2 => "Paused",
                    3 => "Aborting",
                    4 => "Complete",
                    5 => "Error",
                    _ => code.ToString(CultureInfo.InvariantCulture),
                },
                JsonValueKind.String => st.GetString() ?? "Idle",
                _ => "Idle",
            };
        }
        return (state, desc);
    }

    private static (string RawKey, string State, string Message) ReadOutcome(JsonElement status)
    {
        if (!status.TryGetProperty("lastOutcome", out var o) || o.ValueKind != JsonValueKind.Object)
            return ("", "", "");
        string st = o.TryGetProperty("state", out var sv) && sv.ValueKind == JsonValueKind.String ? sv.GetString() ?? "" : "";
        string msg = o.TryGetProperty("message", out var mv) && mv.ValueKind == JsonValueKind.String ? mv.GetString() ?? "" : "";
        return (o.GetRawText(), st, msg);
    }

    private static List<LoopSample> ReadLoops(JsonElement status)
    {
        var list = new List<LoopSample>();
        if (!status.TryGetProperty("loopStack", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var item in arr.EnumerateArray())
        {
            int iter = item.TryGetProperty("iteration", out var ip) && ip.TryGetInt32(out int iv) ? iv : 0;
            int total = item.TryGetProperty("total", out var tp) && tp.TryGetInt32(out int tv) ? tv : 0;
            list.Add(new LoopSample(iter, total));
        }
        return list;
    }

    private static List<AlarmSample> ReadAlarms(JsonElement status)
    {
        var list = new List<AlarmSample>();
        if (!status.TryGetProperty("activeAlarms", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var item in arr.EnumerateArray())
        {
            string ch = item.TryGetProperty("chamber", out var cp) && cp.ValueKind == JsonValueKind.String ? cp.GetString() ?? "" : "";
            string msg = item.TryGetProperty("message", out var mp) && mp.ValueKind == JsonValueKind.String ? mp.GetString() ?? "" : "";
            list.Add(new AlarmSample(ch, msg));
        }
        return list;
    }

    private static (List<PsuSample> Psus, List<PressureSample> Pressures) ReadTelemetry(JsonElement status)
    {
        var psus = new List<PsuSample>();
        var pressures = new List<PressureSample>();
        if (!status.TryGetProperty("activeTelemetry", out var tel) || tel.ValueKind != JsonValueKind.Object)
            return (psus, pressures);

        if (tel.TryGetProperty("psus", out var pArr) && pArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in pArr.EnumerateArray())
            {
                string id = item.TryGetProperty("id", out var idP) && idP.ValueKind == JsonValueKind.String ? idP.GetString() ?? "" : "";
                double v = item.TryGetProperty("voltageV", out var vp) && vp.TryGetDouble(out double vv) ? vv : 0.0;
                double c = item.TryGetProperty("currentMa", out var cp) && cp.TryGetDouble(out double cv) ? cv : 0.0;
                double w = item.TryGetProperty("powerW", out var wp) && wp.TryGetDouble(out double wv) ? wv : 0.0;
                bool sc = !item.TryGetProperty("supportsCurrent", out var scp) || scp.ValueKind != JsonValueKind.False;
                double setC = item.TryGetProperty("setCurrentMa", out var scv) && scv.TryGetDouble(out double sCv) ? sCv : 0.0;
                double setP = item.TryGetProperty("setPowerW", out var spv) && spv.TryGetDouble(out double sPv) ? sPv : 0.0;
                if (!string.IsNullOrEmpty(id))
                    psus.Add(new PsuSample(id, v, c, w, sc, setC, setP));
            }
        }

        if (tel.TryGetProperty("pressures", out var prArr) && prArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in prArr.EnumerateArray())
            {
                string ch = item.TryGetProperty("chamber", out var cp) && cp.ValueKind == JsonValueKind.String ? cp.GetString() ?? "" : "";
                string sn = item.TryGetProperty("sensor", out var sp) && sp.ValueKind == JsonValueKind.String ? sp.GetString() ?? "" : "";
                double pt = item.TryGetProperty("pressureTorr", out var pp) && pp.TryGetDouble(out double pv) ? pv : 0.0;
                if (!string.IsNullOrEmpty(ch))
                    pressures.Add(new PressureSample(ch, sn, pt));
            }
        }

        return (psus, pressures);
    }

    private static string FormatTelemetryBrief(List<PsuSample> psus, List<PressureSample> pressures)
    {
        if (psus.Count == 0 && pressures.Count == 0)
            return "";
        var sb = new StringBuilder();
        if (psus.Count > 0)
        {
            var p = psus[^1];
            sb.Append(string.Create(CultureInfo.InvariantCulture, $" psu={p.Id}:{p.VoltageV:F1}V/{p.CurrentMa:F1}mA/{p.PowerW:F1}W"));
        }
        if (pressures.Count > 0)
        {
            var pr = pressures[0];
            sb.Append(string.Create(CultureInfo.InvariantCulture, $" pressure={pr.Chamber}:{pr.PressureTorr:G4}Torr"));
        }
        return sb.ToString();
    }
}
