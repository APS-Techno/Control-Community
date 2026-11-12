namespace EmulatorCore.Interfaces;

public enum ChamberType { Process, Loadlock, Transfer, Buffer, Custom }

public interface IPhysicsModel
{
    float ComputePressure(ChamberState chamber, float deltaTime);
    float ComputeFlow(MFCState mfc, float deltaTime);
    float ComputeTemperature(ThermalState thermal, float deltaTime);
    float ComputeChamberTemperature(ThermalState thermal, float deltaTime);
}

public class ChamberState
{
    public string Id { get; set; } = "";

    /// <summary>From the type= attribute on &lt;Chamber&gt; elements.</summary>
    public ChamberType Type { get; set; } = ChamberType.Process;

    public float Pressure { get; set; }
    public float Volume { get; set; }
    public float BasePressure { get; set; }
    public float BaseLeakRate { get; set; }
    public float LeakRate { get; set; }
    public float PumpSpeed { get; set; }
    public float GasLoad { get; set; }
    public float ThrottlePosition { get; set; } = 100f;

    /// <summary>
    /// Most recent throttle-valve setpoint in Torr written by Control.
    /// Zero means no setpoint received yet — the throttle is either
    /// idle/open or driven by manual Open/Close.
    /// </summary>
    public float ThrottleSetpointTorr { get; set; }
}

public class MFCState
{
    public float Setpoint { get; set; }
    public float ActualFlow { get; set; }
    public bool ValveOpen { get; set; }
}

public class ThermalState
{
    /// <summary>Substrate temperature (C) — what the substrate pyrometer reads.</summary>
    public float Temperature { get; set; } = 24f;

    /// <summary>Chamber wall temperature (C) — what the PLC "tmp" sensor reads.
    /// Heats slowly from the discharge plume hitting the chamber shielding/shutter
    /// blade and cools to ambient via water/forced convection.</summary>
    public float ChamberTemperature { get; set; } = 24f;

    public float AmbientTemp { get; set; } = 24f;

    /// <summary>Total discharge power (W) deposited inside the chamber this tick.
    /// Always accumulated when sources discharge — drives the chamber wall, since
    /// the plume hits something even when the substrate shutter is closed.</summary>
    public float HeatLoad { get; set; }

    /// <summary>Subset of HeatLoad that actually reaches the substrate this tick (W).
    /// Equal to HeatLoad when the shutter is exposing the substrate, zero when closed.</summary>
    public float SubstrateHeatLoad { get; set; }
}
