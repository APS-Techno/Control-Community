using APS.SystemControl.BaseHWClasses;

namespace APS.Workers.Generic;

public static partial class Operations
{
    /// <summary>Resolver: pressure-sensor id → the throttle controller regulating that sensor's chamber. Wired by Core.LoadWorker.</summary>
    public static Func<string, BaseController?>? ThrottleResolver;

    /// <summary>Resolver: pressure-sensor id → its current reading in Torr. Wired by Core.LoadWorker.</summary>
    public static Func<string, float?>? PressureReader;

    /// <summary>
    /// Consecutive polls the reading must stay within ±10 % of the target before the
    /// wait completes, so a gas-inrush transient crossing the band does not end the
    /// wait before the pressure has settled.
    /// </summary>
    internal static int PwaitSettleSamples = 6;

    internal static int PwaitCommandDelayMs = 200;
    internal static int PwaitPollMs = 500;

    /// <summary>
    /// Pressure wait on a controller bound in Worker.xml. <c>targetPressure</c> &lt;= 0
    /// parks the throttle open and returns at once; a positive target is regulated
    /// to — including one at the gauge floor, which is NOT the same as parking.
    /// Returns the settled pressure in Torr, or null when the controller rejects the
    /// command, the sensor never settles, or the timeout expires.
    /// </summary>
    public static Task<object?> Pwait(BaseController controller, string sensorId,
        float targetPressure, float timeoutSeconds, CancellationToken ct)
        => PwaitCore(controller, sensorId, targetPressure, timeoutSeconds, ct);

    /// <summary>
    /// Chamber-aware pressure wait: the throttle regulating <paramref name="sensorId"/>'s
    /// chamber comes from <see cref="ThrottleResolver"/>.
    /// </summary>
    public static Task<object?> Pwait(string sensorId, float targetPressure,
        float timeoutSeconds, CancellationToken ct)
    {
        var throttle = ThrottleResolver?.Invoke(sensorId);
        if (throttle == null)
            return Task.FromResult(Fail("Pwait", $"no throttle controller resolved for sensor '{sensorId}'"));
        return PwaitCore(throttle, sensorId, targetPressure, timeoutSeconds, ct);
    }

    private static async Task<object?> PwaitCore(BaseController throttle, string sensorId,
        float targetPressure, float timeoutSeconds, CancellationToken ct)
    {
        string throttleId = throttle.Params.ID;
        if (!throttle.Connected)
            return Fail("Pwait", $"controller {throttleId} is not connected");

        bool park = targetPressure <= 0f;
        object[] command = park ? ["Open"] : ["SetPressure", targetPressure];
        try
        {
            throttle.Command(command);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Fail("Pwait", $"controller {throttleId} rejected '{command[0]}': {ex.Message}");
        }
        await Task.Delay(PwaitCommandDelayMs, ct).ConfigureAwait(false);
        if (park) return 0f;

        StepProgressReporter?.Invoke(
            $"Pwait {sensorId} -> {Inv(targetPressure, "g3")}", timeoutSeconds);

        int timeoutMs = (int)(timeoutSeconds * 1000);
        int elapsedMs = 0;
        int inBand = 0;
        while (elapsedMs < timeoutMs)
        {
            if (ReadPressure(throttle, sensorId) is float pressure)
            {
                bool ok = Math.Abs(pressure - targetPressure) / targetPressure < 0.1f;
                inBand = ok ? inBand + 1 : 0;
                if (inBand >= PwaitSettleSamples)
                    return pressure;
            }
            await Task.Delay(PwaitPollMs, ct).ConfigureAwait(false);
            elapsedMs += PwaitPollMs;
        }

        string last = ReadPressure(throttle, sensorId) is float final ? Inv(final, "g3") : "n/a";
        return Fail("Pwait",
            $"{sensorId} did not converge to {Inv(targetPressure, "g3")} Torr within " +
            $"{Inv(timeoutSeconds, "g")}s (last = {last} Torr)");
    }

    private static float? ReadPressure(BaseController throttle, string sensorId)
    {
        float? reading = throttle is IPressureController gauge
            ? gauge.PressureTorr
            : PressureReader?.Invoke(sensorId);
        return reading is float value && float.IsFinite(value) ? value : null;
    }
}
