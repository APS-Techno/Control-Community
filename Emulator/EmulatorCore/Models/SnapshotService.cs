using System.Text.Json;
using System.Text.Json.Nodes;
using EmulatorCore.Interfaces;

namespace EmulatorCore.Models;

public static class SnapshotService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static SimulationSnapshot Capture(SimulationState state, IReadOnlyList<IDeviceEmulator> devices)
    {
        var snapshot = new SimulationSnapshot
        {
            Timestamp = DateTime.Now,
            SimTime = state.SimTime,
            Thermal = new ThermalSnapshot
            {
                Temperature = state.Thermal.Temperature,
                ChamberTemperature = state.Thermal.ChamberTemperature,
                AmbientTemp = state.Thermal.AmbientTemp,
                HeatLoad = state.Thermal.HeatLoad,
                SubstrateHeatLoad = state.Thermal.SubstrateHeatLoad,
            },
            ValveStates = new Dictionary<string, bool>(state.ValveStates),
        };

        foreach (var (id, chamber) in state.Chambers)
            snapshot.Chambers[id] = CaptureChamber(chamber);

        foreach (var device in devices)
            if (device is ISnapshotable snapshotable)
                snapshot.DeviceStates[device.Id] = snapshotable.GetSnapshot();

        return snapshot;
    }

    public static void Restore(SimulationSnapshot snapshot, SimulationState state, IReadOnlyList<IDeviceEmulator> devices)
    {
        state.SimTime = snapshot.SimTime;

        foreach (var (id, snap) in snapshot.Chambers)
        {
            if (!state.Chambers.TryGetValue(id, out var chamber))
            {
                chamber = new ChamberState { Id = id };
                state.SetChamber(chamber);
            }
            RestoreChamber(snap, chamber);
        }

        state.Thermal.Temperature = snapshot.Thermal.Temperature;
        state.Thermal.ChamberTemperature = snapshot.Thermal.ChamberTemperature;
        state.Thermal.AmbientTemp = snapshot.Thermal.AmbientTemp;
        state.Thermal.HeatLoad = snapshot.Thermal.HeatLoad;
        state.Thermal.SubstrateHeatLoad = snapshot.Thermal.SubstrateHeatLoad;

        state.ValveStates.Clear();
        foreach (var kv in snapshot.ValveStates)
            state.ValveStates[kv.Key] = kv.Value;

        foreach (var device in devices)
        {
            if (device is ISnapshotable snapshotable &&
                snapshot.DeviceStates.TryGetValue(device.Id, out var deviceState))
            {
                snapshotable.RestoreSnapshot(deviceState);
            }
        }
    }

    public static void SaveToFile(SimulationSnapshot snapshot, string filePath)
    {
        string dir = Path.GetDirectoryName(filePath)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        string json = JsonSerializer.Serialize(snapshot, JsonOptions);
        File.WriteAllText(filePath, json);
    }

    public static SimulationSnapshot LoadFromFile(string filePath)
    {
        string json = File.ReadAllText(filePath);

        var root = JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidDataException($"Snapshot '{filePath}' is not a JSON object.");

        MigrateLegacyChambers(root);

        var snapshot = JsonSerializer.Deserialize<SimulationSnapshot>(root.ToJsonString(), JsonOptions)
            ?? throw new InvalidDataException("Failed to deserialize snapshot file.");

        // A snapshot that restores no chambers is almost certainly a schema mismatch, and the
        // failure is invisible at runtime: Restore() iterates nothing and every chamber keeps
        // whatever pressure it already had. Fail loudly instead.
        if (snapshot.Chambers.Count == 0)
            throw new InvalidDataException(
                $"Snapshot '{filePath}' contains no recognisable chamber data, so restoring it " +
                "would silently leave every chamber pressure unchanged. Expected a 'Chambers' " +
                "object, or the pre-2026-06 'MainChamber'/'Loadlock' form.");

        return snapshot;
    }

    /// <summary>Snapshots written before ~2026-06 stored chambers as top-level "MainChamber" and
    /// "Loadlock" objects rather than a "Chambers" dictionary. System.Text.Json ignores unmatched
    /// properties, so those files would load with ZERO chambers. The legacy object is exactly
    /// today's ChamberSnapshot minus Id/Type, so the migration is lossless.</summary>
    private static void MigrateLegacyChambers(JsonObject root)
    {
        if (root["Chambers"] is JsonObject existing && existing.Count > 0)
            return;

        var chambers = new JsonObject();
        Lift(root, "MainChamber", "Main", ChamberType.Process, chambers);
        Lift(root, "Loadlock", "Loadlock", ChamberType.Loadlock, chambers);

        if (chambers.Count > 0)
            root["Chambers"] = chambers;

        static void Lift(JsonObject root, string legacyKey, string id, ChamberType type, JsonObject into)
        {
            if (root[legacyKey] is not JsonObject legacy) return;
            var moved = (JsonObject)legacy.DeepClone();
            moved["Id"] = id;
            moved["Type"] = (int)type;                // ChamberType serializes as its integer value
            into[id] = moved;
            root.Remove(legacyKey);
        }
    }

    private static ChamberSnapshot CaptureChamber(ChamberState chamber) => new()
    {
        Id = chamber.Id,
        Type = chamber.Type,
        Pressure = chamber.Pressure,
        Volume = chamber.Volume,
        BasePressure = chamber.BasePressure,
        BaseLeakRate = chamber.BaseLeakRate,
        LeakRate = chamber.LeakRate,
        PumpSpeed = chamber.PumpSpeed,
        GasLoad = chamber.GasLoad,
        ThrottlePosition = chamber.ThrottlePosition,
    };

    private static void RestoreChamber(ChamberSnapshot snap, ChamberState chamber)
    {
        chamber.Id = snap.Id;
        chamber.Type = snap.Type;
        chamber.Pressure = snap.Pressure;
        chamber.Volume = snap.Volume;
        chamber.BasePressure = snap.BasePressure;
        chamber.BaseLeakRate = snap.BaseLeakRate;
        chamber.LeakRate = snap.LeakRate;
        chamber.PumpSpeed = snap.PumpSpeed;
        chamber.GasLoad = snap.GasLoad;
        chamber.ThrottlePosition = snap.ThrottlePosition;
    }
}
