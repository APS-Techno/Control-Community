using EmulatorCore.Interfaces;

namespace APS.Emulator.Devices.S7;

public class VacuumPhysicsModel : IPhysicsModel
{
    private const float TorrLitrePerSecondPerSccm = 760f * 1e-3f / 60f;

    private readonly float _mfcResponseTau;
    private readonly float _substrateHeatingCoeff;
    private readonly float _substrateCoolingCoeff;
    private readonly float _substrateRadiativeCoeff;
    private readonly float _chamberHeatingCoeff;
    private readonly float _chamberCoolingCoeff;

    public VacuumPhysicsModel(
        float mfcResponseTau = 0.5f,
        float substrateHeatingCoeff = 0.002f,
        float substrateCoolingCoeff = 0.005f,
        float substrateRadiativeCoeff = 5e-12f,
        float chamberHeatingCoeff = 3e-4f,
        float chamberCoolingCoeff = 0.01f)
    {
        _mfcResponseTau = mfcResponseTau;
        _substrateHeatingCoeff = substrateHeatingCoeff;
        _substrateCoolingCoeff = substrateCoolingCoeff;
        _substrateRadiativeCoeff = substrateRadiativeCoeff;
        _chamberHeatingCoeff = chamberHeatingCoeff;
        _chamberCoolingCoeff = chamberCoolingCoeff;
    }

    public float ComputePressure(ChamberState chamber, float deltaTime)
    {
        if (chamber.Volume <= 0)
            return chamber.Pressure;

        // Butterfly/gate throttle valve: conductance scales ~quadratically with opening
        float throttle = chamber.ThrottlePosition / 100f;
        float S_eff = chamber.PumpSpeed * throttle * throttle;

        if (S_eff <= 0)
        {
            float gasLoad = chamber.GasLoad * TorrLitrePerSecondPerSccm;
            float dP = (gasLoad + chamber.LeakRate) / chamber.Volume * deltaTime;
            return chamber.Pressure + dP;
        }
        float tau = chamber.Volume / S_eff;

        float gasEquilibrium = (chamber.GasLoad * TorrLitrePerSecondPerSccm + chamber.LeakRate) / S_eff;
        float targetPressure = chamber.BasePressure + gasEquilibrium;

        float alpha = 1f - MathF.Exp(-deltaTime / tau);
        float newPressure = chamber.Pressure + (targetPressure - chamber.Pressure) * alpha;

        return MathF.Max(newPressure, chamber.BasePressure);
    }

    public float ComputeFlow(MFCState mfc, float deltaTime)
    {
        float alpha = 1f - MathF.Exp(-deltaTime / _mfcResponseTau);
        float target = mfc.ValveOpen ? mfc.Setpoint : 0f;
        return mfc.ActualFlow + (target - mfc.ActualFlow) * alpha;
    }

    public float ComputeTemperature(ThermalState thermal, float deltaTime)
    {
        // Substrate is driven by the shutter-gated SubstrateHeatLoad: only power that actually
        // reaches the substrate through the open shutter contributes.
        float heatRate = thermal.SubstrateHeatLoad * _substrateHeatingCoeff;
        float coolRate = (thermal.Temperature - thermal.AmbientTemp) * _substrateCoolingCoeff;

        // Radiative (Stefan-Boltzmann-style) loss in Kelvin^4. Dominates at high T and prevents
        // the substrate temperature from running away when many sources discharge simultaneously.
        float tK = thermal.Temperature + 273.15f;
        float tAmbK = thermal.AmbientTemp + 273.15f;
        float radRate = _substrateRadiativeCoeff * (tK * tK * tK * tK - tAmbK * tAmbK * tAmbK * tAmbK);

        return thermal.Temperature + (heatRate - coolRate - radRate) * deltaTime;
    }

    public float ComputeChamberTemperature(ThermalState thermal, float deltaTime)
    {
        // Chamber walls see the full discharge HeatLoad regardless of shutter position —
        // the plume hits the shutter blade or chamber shielding, which is still inside the
        // chamber. Walls have weaker plasma coupling and water/forced-convection cooling, so
        // their equilibrium is far below the substrate's.
        float heatRate = thermal.HeatLoad * _chamberHeatingCoeff;
        float coolRate = (thermal.ChamberTemperature - thermal.AmbientTemp) * _chamberCoolingCoeff;
        return thermal.ChamberTemperature + (heatRate - coolRate) * deltaTime;
    }
}
