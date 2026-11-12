using EmulatorCore.Interfaces;

namespace APS.SystemEmulator.Engine;

/// <summary>
/// One magnetron source installed on a flange: links the magnetron type,
/// its discharge voltage, the chamber it is installed in, and the power
/// supply that drives it.
/// </summary>
public record SourceEntry(
    int Flange,
    string MagnetronId,
    float MagnetronVoltage,
    string ChamberId,
    IPowerSupplyEmulator PowerSupply);
