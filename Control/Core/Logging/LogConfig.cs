using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.CoreClass;
using APS.SystemControl.Core.HWClasses.Base;

namespace APS.SystemControl.Core.Logging;

public class LogConfig
{
    /// <summary>Id of the sensor that carries the profile's external clock; it is a timestamp, not a measurement, so it never gets a data column.</summary>
    public string? ClockSensorId { get; set; }

    private bool IsClock(string sensorId) =>
        ClockSensorId is not null && string.Equals(ClockSensorId, sensorId, StringComparison.OrdinalIgnoreCase);

    /// <summary>A pressure of zero or below is a gauge that has not reported, not a vacuum; NaN carries the status. +/-Infinity pass through as over/under range.</summary>
    internal static double PressureOrInvalid(double torr) => double.IsInfinity(torr) ? torr : double.IsNaN(torr) || torr <= 0 ? double.NaN : torr;

    internal static double PercentOrInvalid(double percent) => double.IsNaN(percent) || percent < 0 || percent > 100 ? double.NaN : percent;

    /// <param name="sourcePsuIds">Ids of the power supplies assigned to a mounted
    /// source in Sources.xml (the same set recorded in the .depo.json snapshot).
    /// A sputter PSU is logged only if it appears here; a bias analyzer is always
    /// logged (it drives no source). Case-insensitive membership expected.</param>
    public Dictionary<string, string> BuildColumns(
        HwElementCollection hwElements, IReadOnlyCollection<string> sourcePsuIds, bool includeStates = false)
    {
        var columns = new Dictionary<string, string>();

        // Only CONNECTED devices get a column — a configured-but-absent supply
        // (each serial PSU connects independently) otherwise produced a header
        // with permanently empty cells. Connection state is frozen at recipe
        // Start; a device connecting later simply has no column (CollectValues'
        // value for a missing header is dropped by DataLogger.LogRow).

        if (hwElements.Item.TryGetValue(DeviceType.Sensor, out Elements? sensors))
        {
            foreach (var kvp in sensors)
            {
                if (!kvp.Value.Connected || IsClock(kvp.Key)) continue;
                string header = $"S_{kvp.Key}";
                columns[header] = GetSensorUnit(kvp.Key, kvp.Value.Params.Description);
            }
        }

        // Pressure controllers are registered as Controllers, not Sensors.
        // The throttle-valve position rides along from the same poll cache — the
        // diagnostic for whether the PID still had headroom at the logged pressure.
        if (hwElements.Item.TryGetValue(DeviceType.Controller, out Elements? controllers))
        {
            foreach (var kvp in controllers)
            {
                if (!kvp.Value.Connected || kvp.Value is not IPressureController) continue;
                columns[$"S_{kvp.Key}"] = "Torr";
                columns[$"TV_{kvp.Key}"] = "%";
            }
        }

        if (hwElements.Item.TryGetValue(DeviceType.MFC, out Elements? mfcs))
        {
            foreach (var kvp in mfcs)
            {
                if (!kvp.Value.Connected) continue;
                string header = $"MFC_{kvp.Key}";
                columns[header] = "sccm";
            }
        }

        // Power supplies — voltage and current only (power is redundant V×I).
        // A multi-channel bias analyzer (via IBiasSupply) is logged as
        // BIAS per channel, not as a single-output sputter-cathode PSU.
        if (hwElements.Item.TryGetValue(DeviceType.Power, out Elements? powerSupplies))
        {
            foreach (var kvp in powerSupplies)
            {
                if (kvp.Value is IBiasSupply bias)
                {
                    // Bias analyzer (substrate bias) — always logged when configured;
                    // it drives no source, so it is never in sourcePsuIds.
                    for (int ch = 1; ch <= bias.ChannelCount; ch++)
                    {
                        columns[$"BIAS_{kvp.Key}_ch{ch}_V"] = "V";
                        columns[$"BIAS_{kvp.Key}_ch{ch}_I"] = "A";
                    }
                }
                else if (sourcePsuIds.Contains(kvp.Key))
                {
                    // Sputter PSU — logged only when assigned to a mounted source.
                    // A supply declared but bolted to no source is skipped, rather
                    // than relying on the port-open Connected flag or the flaky
                    // single-instant IsResponding.
                    columns[$"PSU_{kvp.Key}_V"] = "V";
                    columns[$"PSU_{kvp.Key}_I"] = kvp.Value is BasePowerSupply psu ? psu.CurrentUnit : "mA";
                }
            }
        }

        if (hwElements.Item.TryGetValue(DeviceType.Stage, out Elements? stages))
        {
            foreach (var kvp in stages)
            {
                if (!kvp.Value.Connected) continue;
                string header = $"STG_{kvp.Key}";
                columns[header] = "mm";
            }
        }
        if (includeStates)
        {
            if (hwElements.Item.TryGetValue(DeviceType.Valve, out Elements? valves))
                foreach (var kvp in valves)
                    columns[$"VLV_{kvp.Key}"] = "1=open";

            if (hwElements.Item.TryGetValue(DeviceType.Pump, out Elements? pumps))
                foreach (var kvp in pumps)
                {
                    if (kvp.Value is IStatusBitNamed named)
                        foreach (string bit in named.StatusBitNames)
                            columns[$"PMP_{kvp.Key}_{bit}"] = "1=on";
                    else
                        columns[$"PMP_{kvp.Key}"] = "1=on";
                }
        }

        return columns;
    }

    public Dictionary<string, double> CollectValues(
        HwElementCollection hwElements, IReadOnlyCollection<string> sourcePsuIds, bool includeStates = false)
    {
        var values = new Dictionary<string, double>();

        if (hwElements.Item.TryGetValue(DeviceType.Sensor, out Elements? sensors))
        {
            foreach (var kvp in sensors)
            {
                if (kvp.Value.Connected && kvp.Value is BaseSensor sensor && !IsClock(kvp.Key))
                {
                    string unit = GetSensorUnit(kvp.Key, sensor.Params.Description);
                    double raw = sensor.LastValue;
                    values[$"S_{kvp.Key}"] = unit == "Torr" ? PressureOrInvalid(raw) : raw;
                }
            }
        }

        // Pressure controllers — same gate as BuildColumns.
        if (hwElements.Item.TryGetValue(DeviceType.Controller, out Elements? controllers))
        {
            foreach (var kvp in controllers)
            {
                if (kvp.Value.Connected && kvp.Value is IPressureController pc)
                {
                    try
                    {
                        values[$"S_{kvp.Key}"] = PressureOrInvalid(pc.PressureTorr);
                        values[$"TV_{kvp.Key}"] = PercentOrInvalid(pc.ValvePositionPercent);
                    }
                    catch { }
                }
            }
        }

        if (hwElements.Item.TryGetValue(DeviceType.MFC, out Elements? mfcs))
        {
            foreach (var kvp in mfcs)
            {
                if (kvp.Value.Connected && kvp.Value is BaseMFC mfc)
                {
                    try { values[$"MFC_{kvp.Key}"] = mfc.LastFlow; }
                    catch { }
                }
            }
        }

        if (hwElements.Item.TryGetValue(DeviceType.Power, out Elements? powerSupplies))
        {
            foreach (var kvp in powerSupplies)
            {
                // Same source-driven gate as BuildColumns so the row's columns
                // exactly match the header set.
                if (kvp.Value is IBiasSupply bias)
                {
                    for (int ch = 0; ch < bias.ChannelCount; ch++)
                    {
                        values[$"BIAS_{kvp.Key}_ch{ch + 1}_V"] = bias.ChannelVoltage(ch);
                        values[$"BIAS_{kvp.Key}_ch{ch + 1}_I"] = bias.ChannelCurrent(ch);
                    }
                }
                else if (kvp.Value is BasePowerSupply psu && sourcePsuIds.Contains(kvp.Key))
                {
                    values[$"PSU_{kvp.Key}_V"] = psu.Voltage;
                    values[$"PSU_{kvp.Key}_I"] = psu.Current;
                }
            }
        }

        if (hwElements.Item.TryGetValue(DeviceType.Stage, out Elements? stages))
        {
            foreach (var kvp in stages)
            {
                if (kvp.Value.Connected && kvp.Value is BaseStage stage)
                {
                    try { values[$"STG_{kvp.Key}"] = stage.LastPosition; }
                    catch { }
                }
            }
        }

        if (includeStates)
        {
            if (hwElements.Item.TryGetValue(DeviceType.Valve, out Elements? valves))
                foreach (var kvp in valves)
                    values[$"VLV_{kvp.Key}"] = kvp.Value is BaseValve valve && valve.Connected
                        ? (valve.LastOpen ? 1 : 0) : double.NaN;

            if (hwElements.Item.TryGetValue(DeviceType.Pump, out Elements? pumps))
                foreach (var kvp in pumps)
                {
                    bool[]? status = kvp.Value is BasePump pump && pump.Connected ? pump.LastStatus : null;
                    if (kvp.Value is IStatusBitNamed named)
                    {
                        for (int i = 0; i < named.StatusBitNames.Count; i++)
                            values[$"PMP_{kvp.Key}_{named.StatusBitNames[i]}"] =
                                status is not null && i < status.Length ? (status[i] ? 1 : 0) : double.NaN;
                    }
                    else
                        values[$"PMP_{kvp.Key}"] = status is { Length: > 0 } ? (status[0] ? 1 : 0) : double.NaN;
                }
        }

        return values;
    }

    /// <summary>
    /// Infers a sensor's unit from its id and description. Temperature sensors
    /// (id "tmp"/"C1_tmp", or a sensor whose temp marker is only in its "Substrate
    /// Temp" description) report °C; a modeled film-thickness channel (description
    /// containing "thickness") reports nm; every other sensor on the vacuum system is a
    /// pressure gauge (pm, pl) reporting Torr.
    ///
    /// The temperature test must come FIRST and match a whole "temp"/"tmp" token,
    /// not a bare 'p', because "tmp" contains a p. Pressure reads Torr — the PLC
    /// DB float is Torr, matching the recipe DSL and engine-side pwait. (audit U-8)
    /// </summary>
    private static string GetSensorUnit(string sensorId, string description)
    {
        string s = $"{sensorId} {description}".ToLowerInvariant();
        if (s.Contains("temp") || s.Contains("tmp"))
            return "C";
        if (s.Contains("thickness"))
            return "nm";
        return "Torr";
    }
}
