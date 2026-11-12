using System.Globalization;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.HWClasses.Base;

namespace APS.Workers.Generic;

public static partial class Operations
{
    public static Action<string, double>? StepProgressReporter;
    public static Action<string, string>? LogReporter;

    public static void Init(string configDir)
    {
        LoadSourceMap(configDir);
    }

    internal static void ClearStaticState()
    {
        StepProgressReporter = null;
        LogReporter = null;
        DriveResolver = null;
        DepositReporter = null;
        ShutterResolver = null;
        DepositGuard = null;
        ThrottleResolver = null;
        PressureReader = null;
        _sources = new Dictionary<string, List<SourceEntry>>(StringComparer.OrdinalIgnoreCase);
        _currentChamber = null;
        PwaitSettleSamples = 6;
        PwaitCommandDelayMs = 200;
        PwaitPollMs = 500;
        DriveIdleTimeoutMs = 30_000;
        DriveSettleMs = 200;
        DrivePollMs = 50;
        TransferTimeoutMs = 60_000;
        HomeTimeoutMs = 30_000;
        AbortTimeoutMs = 10_000;
        RobotPollMs = 100;
    }

    private static object? Fail(string operation, string reason)
    {
        LogReporter?.Invoke(operation, reason);
        return null;
    }

    private static string Inv(float value, string format) => value.ToString(format, CultureInfo.InvariantCulture);

    public static void ResetRecipeState()
    {
        _currentChamber = null;
    }

    public static async Task<object?> Valve(BaseValve valve, bool newstate, CancellationToken ct)
    {
        if (!valve.Connected) return null;
        if (newstate) valve.Open(); else valve.Close();
        await Task.Delay(500, ct).ConfigureAwait(false);
        // A verified PLC write that failed after retries leaves LastWriteVerified
        // false. Returning the read-back bool would be a non-null result the recipe
        // engine treats as success — so the deposition would proceed with a gas/
        // vent/gate valve never actuated. Return null to abort the recipe instead.
        if (!valve.LastWriteVerified) return null;
        // This op REPLACES Core.OpValve (Core.LoadWorker drops the core op of the
        // same name), so the settle wait must run here too. A panel gate valve
        // (S7200PanelValve) is still travelling at this point; without the wait the
        // next step's live interlock sees the old state.
        await valve.SettleAsync(newstate, ct, msg => LogReporter?.Invoke("Valve", msg)).ConfigureAwait(false);
        return valve.IsOpen;
    }

    public static async Task<object?> MFC(BaseMFC mfc, float flow, CancellationToken ct)
    {
        if (!mfc.Connected) return null;
        mfc.SetFlow(flow);
        // Verified setpoint write failed → report failure to the recipe engine
        // (null) rather than a read-back flow that masks it as success.
        if (!mfc.LastWriteVerified) return null;
        await Task.Delay(flow > 0 ? 3000 : 500, ct).ConfigureAwait(false);
        return mfc.ReadFlow();
    }

    /// <summary>
    /// Recipe `set ID PARAM VALUE`. A parameter name the driver does not
    /// recognise fails the step (returns null) instead of being swallowed, so
    /// the engine never logs success for a setpoint that was never applied.
    /// </summary>
    public static async Task<object?> Set(BasePowerSupply psu, string parameter, float value, CancellationToken ct)
    {
        if (!psu.Connected) return null;
        try
        {
            psu.ApplySetpoint(parameter, value);
        }
        catch (ArgumentException ex)
        {
            System.Diagnostics.Trace.TraceError(
                $"set {psu.Params?.ID} {parameter} {value}: {ex.Message}");
            return null;
        }
        await Task.Delay(200, ct).ConfigureAwait(false);
        return value;
    }

    public static async Task<object?> Power(BasePowerSupply psu, bool on, CancellationToken ct)
    {
        if (!psu.Connected) return null;
        if (on) psu.OutputOn(); else psu.OutputOff();
        await Task.Delay(500, ct).ConfigureAwait(false);

        // The readback guard is for power-ON ONLY, and the asymmetry is the whole
        // point.
        //
        // Connected is latched when the port opens; it does NOT prove bytes are
        // reaching the supply. On power-ON, OutputOn() sets IsOutputOn locally
        // even if nothing arrived, so returning that flag would log a phantom
        // success and the recipe would deposit with no power. That is worth
        // aborting for.
        //
        // On power-OFF the same silence means the opposite. A supply that does
        // not answer is a supply that is not energising anything — which is
        // precisely the postcondition `power X off` asks for. The commonest
        // reason for it is the entirely normal one: the supply's mains switch is
        // off. Demanding a readback there would abort FAILURE_PROTOCOL and
        // SYSTEM_AIR_RELEASE over a source that is simply not switched on.
        //
        // Confirmation reads (not a single sample) because IsResponding flips
        // false on one late poll and a flap must not abort a run. Skipped while
        // ramping, where ReadFeedback is paused on purpose.
        if (on && !psu.RampEnabled && !psu.IsResponding
            && !await psu.ConfirmRespondingAsync(ct: ct).ConfigureAwait(false))
        {
            // Quote the driver's own reason — otherwise every one of these reads
            // identically whether the port is shut, the reply timed out, or the
            // supply answered something unparseable.
            string why = string.IsNullOrWhiteSpace(psu.LastCommFault)
                ? "no reason recorded by the driver"
                : psu.LastCommFault!;
            throw new InvalidOperationException(
                $"power {psu.Params.ID} on: supply not responding — output not confirmed " +
                $"({why}; check the supply's mains switch and its connection).");
        }

        return psu.IsOutputOn;
    }

    public static async Task<object?> Ramp(BasePowerSupply psu, float power, CancellationToken ct)
    {
        if (!psu.Connected) return null;
        psu.ApplySetpoint("Power", power);
        await Task.Delay(200, ct).ConfigureAwait(false);
        return power;
    }

    public static async Task<object?> Warmup(BasePowerSupply psu, float power, float durationSeconds, CancellationToken ct)
    {
        if (!psu.Connected) return null;
        psu.ApplySetpoint("Power", power);
        psu.OutputOn();
        StepProgressReporter?.Invoke($"Warmup {power:F0}W", durationSeconds);
        try
        {
            // Long delay — abort responsiveness matters here most. Tick every
            // second so cancellation fires within ~1 s even mid-warmup.
            int totalMs = (int)(durationSeconds * 1000);
            int elapsedMs = 0;
            while (elapsedMs < totalMs)
            {
                int step = Math.Min(1000, totalMs - elapsedMs);
                await Task.Delay(step, ct).ConfigureAwait(false);
                elapsedMs += step;
            }
            return true;
        }
        finally
        {
            // Always de-energise — on normal completion AND on abort/exception:
            // the recipe-level RunFailureProtocol is gated on AbortProtocolEnabled,
            // which is false by default. CancellationToken.None for the settle
            // delay so the power-down itself can never be cancelled.
            psu.ApplySetpoint("Power", 0);
            try { await Task.Delay(500, CancellationToken.None).ConfigureAwait(false); }
            catch { /* never let the settle delay prevent OutputOff */ }
            psu.OutputOff();
        }
    }
}
