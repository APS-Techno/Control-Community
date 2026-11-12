namespace EmulatorCore.Interfaces;

public enum PowerRegulationMode
{
    /// <summary>Current-regulated (DC / pulsed DC). Voltage follows from plasma impedance.</summary>
    Current,

    /// <summary>Power-regulated (RF). Matching network maintains commanded power.</summary>
    Power,
}

public interface IPowerSupplyEmulator : IDeviceEmulator
{
    PowerRegulationMode RegulationMode { get; }

    /// <summary>Commanded current in amperes. Used when <see cref="RegulationMode"/> is Current.</summary>
    float CommandedCurrent { get; }

    /// <summary>Commanded power in watts. Used when <see cref="RegulationMode"/> is Power.</summary>
    float CommandedPower { get; }

    /// <summary>True if the operator has enabled output (main contactor / relay / "G" command).</summary>
    bool OutputEnabled { get; }

    /// <summary>
    /// Voltage the supply rails to when output is enabled but the discharge has not struck
    /// (pressure too low, no plasma to draw current). Used only by Current-mode supplies —
    /// Power-mode supplies may return 0.
    /// </summary>
    float OpenCircuitVoltage { get; }

    /// <summary>
    /// Called by the simulation engine each tick to report the operating point that should
    /// be visible through the protocol. Passing (0, 0, 0) represents "no discharge".
    /// </summary>
    void SetActualOutput(float voltage, float current, float power);
}
