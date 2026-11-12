using System.Text.Json.Nodes;
using EmulatorCore.Interfaces;

namespace EmulatorCore.Models;

public class SimulationSnapshot
{
    public DateTime Timestamp { get; set; }
    public float SimTime { get; set; }
    public Dictionary<string, ChamberSnapshot> Chambers { get; set; } = new();
    public ThermalSnapshot Thermal { get; set; } = new();
    public Dictionary<string, bool> ValveStates { get; set; } = new();
    public Dictionary<string, JsonObject> DeviceStates { get; set; } = new();
}

public class ChamberSnapshot
{
    public string Id { get; set; } = "";
    public ChamberType Type { get; set; } = ChamberType.Process;
    public float Pressure { get; set; }
    public float Volume { get; set; }
    public float BasePressure { get; set; }
    public float BaseLeakRate { get; set; }
    public float LeakRate { get; set; }
    public float PumpSpeed { get; set; }
    public float GasLoad { get; set; }
    public float ThrottlePosition { get; set; }
}

public class ThermalSnapshot
{
    public float Temperature { get; set; }
    public float ChamberTemperature { get; set; }
    public float AmbientTemp { get; set; }
    public float HeatLoad { get; set; }
    public float SubstrateHeatLoad { get; set; }
}
