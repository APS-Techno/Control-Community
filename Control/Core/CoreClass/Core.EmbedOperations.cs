using APS.SystemControl.BaseHWClasses;

namespace APS.SystemControl.Core;

public partial class Core
{
    /// <summary>
    /// Embedded implementation of the <c>isolate</c> recipe keyword: closes every
    /// connected valve and zeroes every MFC with 100 ms inter-step delays.
    /// </summary>
    private async Task<object?> OpIsolate(object[] args, CancellationToken ct)
    {
        var valves = hwElements.Item.TryGetValue(DeviceType.Valve, out var valveElements)
            ? valveElements._elements.Values.OfType<BaseValve>()
            : Enumerable.Empty<BaseValve>();

        var mfcs = hwElements.Item.TryGetValue(DeviceType.MFC, out var mfcElements)
            ? mfcElements._elements.Values.OfType<BaseMFC>()
            : Enumerable.Empty<BaseMFC>();

        await IsolateOperation.RunAsync(valves, mfcs, ct);
        return true;
    }

    public async Task OpDelay(float waitTime, CancellationToken cancellationToken = default)
    {
        SendLogMesssage("OpDelay", $"Waiting {waitTime}s");
        int totalMs = (int)(waitTime * 1000);
        int stepMs = 500;
        int totalSteps = Math.Max(totalMs / stepMs, 1);
        for (int i = 0; i <= totalSteps; i++)
        {
            int delayMs = Math.Min(stepMs, totalMs - i * stepMs);
            if (delayMs > 0)
                await Task.Delay(delayMs, cancellationToken);
            UpdateProgress("Waiting", (float)i / totalSteps);
        }
        SendLogMesssage("OpDelay", "Continue");
    }

    /// <summary>
    /// Embedded implementation of the <c>warmup TIME SRC1 SRC2 ...</c> recipe
    /// keyword. The listed sources are already energised by the preceding
    /// <c>set</c>/<c>power</c> lines; warmup is a stabilisation dwell that holds
    /// for <paramref name="minutes"/> (the WarmUpTime parameter is declared in
    /// minutes) while the plasma settles, then continues with the sources left
    /// ON for the following deposit. The source list is informational (logged +
    /// shown on the progress label). Honours recipe cancellation.
    /// Intercepted in CoreThread before dispatch, so it never reaches the
    /// (differently-shaped) worker <c>Warmup(psu, power, seconds)</c> op.
    /// </summary>
    public async Task OpWarmup(float seconds, IReadOnlyList<string> sources, CancellationToken cancellationToken = default)
    {
        string srcList = sources.Count > 0 ? string.Join(", ", sources) : "(none)";
        float waitSeconds = seconds;
        SendLogMesssage("Warmup", $"Warming up {srcList} for {waitSeconds:F0}s");
        int totalMs = (int)(waitSeconds * 1000);
        int stepMs = 500;
        int totalSteps = Math.Max(totalMs / stepMs, 1);
        for (int i = 0; i <= totalSteps; i++)
        {
            int delayMs = Math.Min(stepMs, totalMs - i * stepMs);
            if (delayMs > 0)
                await Task.Delay(delayMs, cancellationToken);
            UpdateProgress($"Warmup {srcList}", (float)i / totalSteps);
        }
        SendLogMesssage("Warmup", "Warmup complete");
    }

    internal async Task<object?> OpValve(object[] args, CancellationToken ct)
    {
        BaseValve valve = (BaseValve)args[0];
        bool open = Convert.ToBoolean(args[1]);
        if (valve == null)
            return null;

        if (open) valve.Open(); else valve.Close();
        // Failed verified write → null so the recipe aborts rather than
        // treating a non-null read-back as success (see BaseValve.LastWriteVerified).
        if (!valve.LastWriteVerified)
            return null;

        // A pulse-commanded valve with a position sensor (the panel gate valves)
        // is still travelling when the write returns. Wait for the sensor so the
        // next step's interlock — which reads that same sensor live — sees the
        // valve where the recipe put it (incident 2026-09-07). A timeout fails the
        // step loudly, naming the valve. A worker that declares its own `Valve` op
        // replaces this op entirely — the shared wait
        // lives in BaseValve.SettleAsync so both paths behave the same.
        await valve.SettleAsync(open, ct, msg => SendLogMesssage("Valve", msg)).ConfigureAwait(false);
        return valve.IsOpen;
    }

    private async Task<object?> OpMFC(object[] args, CancellationToken ct)
    {
        BaseMFC mfc = (BaseMFC)args[0];
        // args[1] is the raw recipe token; parse invariant so a fractional value
        // does not throw FormatException on a comma-decimal locale (audit M-12).
        float flow = Convert.ToSingle(args[1], System.Globalization.CultureInfo.InvariantCulture);

        if (mfc != null)
        {
            mfc.SetFlow(flow);
            // Failed verified write → null (recipe abort) instead of a read-back
            // that masks it as success.
            if (!mfc.LastWriteVerified) return null;
            // Awaitable settle time — respects recipe cancellation.
            await Task.Delay(flow > 0 ? 3000 : 500, ct).ConfigureAwait(false);
            return mfc.ReadFlow();
        }
        return null;
    }

    /// <summary>
    /// Tolerance band (mm) used to consider a stage "arrived" — matches the
    /// ±1 mm window the GVL/Stage interlocks use when validating
    /// <c>Stage HS at 15</c> in Interlocks.LL.xml.
    /// </summary>
    private const float StageArrivalToleranceMm = 1.0f;

    /// <summary>
    /// Hard ceiling for any single stage move. The longest stroke we expect
    /// (HS arm full traverse, ~280 mm) completes in well under a minute, so
    /// 120 s gives ample slack before we declare the drive stuck.
    /// </summary>
    private const int StageArrivalTimeoutMs = 120_000;

    private const int StageArrivalPollMs = 200;

    private async Task<object?> OpStage(object[] args, CancellationToken ct)
    {
        BaseStage stage = (BaseStage)args[0];
        // Parse invariant — operator .rcpx recipes use editable fractional stage
        // setpoints; current-culture parse aborts the recipe on ru-RU (audit M-12).
        float position = Convert.ToSingle(args[1], System.Globalization.CultureInfo.InvariantCulture);

        if (stage != null)
        {
            string id = stage.Params?.ID ?? "?";
            SendLogMesssage("Stage", $"{id}: moving to {position:F1} mm");
            // Link the arrival-wait to a per-stage token so a manual Stop
            // (OpStageStop → CancelMove) aborts it promptly instead of letting
            // it run to the 120 s timeout. A recipe abort still propagates: the
            // linked token trips ct too, and the catch below rethrows in that case.
            var moveCt = stage.BeginMove(ct);
            stage.Position = position;
            // Poll until the stage actually reaches the commanded position
            // (Delphi parity: TStepperDrive.WaitFor in unit_StepperDrive.pas).
            try
            {
                float arrived = await Classes.StageArrivalWaiter.WaitAsync(
                    () => stage.Position,
                    position,
                    StageArrivalToleranceMm,
                    StageArrivalPollMs,
                    StageArrivalTimeoutMs,
                    moveCt).ConfigureAwait(false);
                return arrived;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Manual Stop interrupted the move — report where it halted
                // rather than surfacing a recipe-abort cancellation.
                SendLogMesssage("Stage", $"{id}: stopped at {stage.LastPosition:F1} mm");
                return stage.LastPosition;
            }
        }
        return null;
    }

    /// <summary>
    /// Immediately halts a stage: cancels the in-flight arrival-wait and pulses
    /// the driver's stop bit (<see cref="BaseStage.Stop"/>). Invoked from the
    /// widget/popup Stop button via the "StageStop" worker command.
    /// </summary>
    private Task<object?> OpStageStop(object[] args, CancellationToken ct)
    {
        BaseStage stage = (BaseStage)args[0];
        if (stage != null)
        {
            SendLogMesssage("Stage", $"{stage.Params?.ID ?? "?"}: STOP");
            stage.CancelMove();
            stage.Stop();
            return Task.FromResult<object?>(stage.LastPosition);
        }
        return Task.FromResult<object?>(null);
    }

    /// <summary>
    /// <c>pump &lt;name&gt; &lt;on|off&gt; [seconds]</c>.
    ///
    /// The optional third argument is a spin-up wait: the recipes that use it
    /// (<c>pump SP1 on 30</c> before opening PP, <c>pump C4_SP on 5</c> before
    /// the roughing line) mean "start it and let it come up to speed before the
    /// next step". It is honoured only when starting — stopping returns at once,
    /// since a spin-DOWN wait is spelled out as its own <c>delay</c> step.
    /// </summary>
    internal async Task<object?> OpPump(object[] args, CancellationToken ct)
    {
        BasePump pump = (BasePump)args[0];
        bool on = Convert.ToBoolean(args[1]);
        // Optional slot: absent (null) whenever the recipe omits it.
        float spinUpSeconds = args.Length > 2 && args[2] != null
            ? Convert.ToSingle(args[2], System.Globalization.CultureInfo.InvariantCulture)
            : 0f;

        if (pump != null)
        {
            if (on && pump.IsRunning)
                SendLogMesssage("Pump", $"Pump {pump.Params.ID} already running — skipping");
            pump.Power(on);   // no-op when on && IsRunning (idempotent guard in Power)

            if (on && spinUpSeconds > 0)
            {
                SendLogMesssage("Pump", $"Pump {pump.Params.ID}: spin-up wait {spinUpSeconds:F0}s");
                await Task.Delay((int)(spinUpSeconds * 1000), ct).ConfigureAwait(false);
            }
            // Return the COMMANDED state, not the status array: S7200TurboPump
            // decodes running from status[1] || status[2] (its status[0] is the
            // ACCEL composite), and a turbo needs ~30 s to spin up / coast down,
            // so sampling here would log the wrong state. Actual running state is
            // reported by the widget, S7200TurboPump's DB17 status-register trace
            // (LogStatusChange), and the TP alarm rules.
            return on;
        }
        return null;
    }
}
