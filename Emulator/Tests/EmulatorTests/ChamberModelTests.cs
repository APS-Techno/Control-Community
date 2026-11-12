using APS.Emulator.Devices.S7;
using EmulatorCore.Interfaces;

namespace EmulatorTests;

public class ChamberModelTests
{
    [Fact]
    public void Pumpdown_From760Torr_ReachesBelow001_After60s()
    {
        var model = new VacuumPhysicsModel();
        var chamber = new ChamberState
        {
            Pressure = 760f,
            Volume = 50f,
            BasePressure = 2.2e-4f,
            LeakRate = 1e-5f,
            PumpSpeed = 50f,
            ThrottlePosition = 100f,
            GasLoad = 0f,
        };

        float dt = 0.1f;
        for (float t = 0; t < 60f; t += dt)
        {
            chamber.Pressure = model.ComputePressure(chamber, dt);
        }

        Assert.True(chamber.Pressure < 0.01f,
            $"Expected pressure < 0.01 Torr after 60s pumpdown, got {chamber.Pressure}");
    }

    [Fact]
    public void NoPump_PressureIncreases()
    {
        var model = new VacuumPhysicsModel();
        var chamber = new ChamberState
        {
            Pressure = 1.0f,
            Volume = 50f,
            BasePressure = 2.2e-4f,
            LeakRate = 1e-3f,
            PumpSpeed = 0f,
            ThrottlePosition = 100f,
            GasLoad = 0f,
        };

        float initialPressure = chamber.Pressure;
        chamber.Pressure = model.ComputePressure(chamber, 1.0f);

        Assert.True(chamber.Pressure > initialPressure);
    }

    [Fact]
    public void GasLoad_PumpedEquilibrium_Is100SccmOver1000LpsAt1p27mTorr()
    {
        var model = new VacuumPhysicsModel();
        var chamber = new ChamberState
        {
            Pressure = 0f,
            Volume = 100f,
            BasePressure = 0f,
            LeakRate = 0f,
            PumpSpeed = 1000f,
            ThrottlePosition = 100f,
            GasLoad = 100f,
        };

        for (float t = 0; t < 10f; t += 0.1f)
            chamber.Pressure = model.ComputePressure(chamber, 0.1f);

        Assert.InRange(chamber.Pressure, 1.254e-3f, 1.280e-3f);
    }

    [Fact]
    public void GasLoad_Unpumped_60SccmInto200LRises38mTorrIn10s()
    {
        var model = new VacuumPhysicsModel();
        var chamber = new ChamberState
        {
            Pressure = 0f,
            Volume = 200f,
            BasePressure = 0f,
            LeakRate = 0f,
            PumpSpeed = 0f,
            ThrottlePosition = 100f,
            GasLoad = 60f,
        };

        chamber.Pressure = model.ComputePressure(chamber, 10f);

        Assert.InRange(chamber.Pressure, 0.0376f, 0.0384f);
    }

    [Fact]
    public void ComputeTemperature_WithHeatLoad_Increases()
    {
        var model = new VacuumPhysicsModel();
        var thermal = new ThermalState
        {
            Temperature = 24f,
            AmbientTemp = 24f,
            SubstrateHeatLoad = 100f,
        };

        float newTemp = model.ComputeTemperature(thermal, 1.0f);
        Assert.True(newTemp > 24f);
    }

    [Fact]
    public void ComputeTemperature_WithHeatingCoeff_ScalesHeatRate()
    {
        // heatingCoeff=0.01, coolingCoeff=0.0 means dT/dt = HeatLoad * 0.01
        var model = new VacuumPhysicsModel(
            mfcResponseTau: 0.5f,
            substrateHeatingCoeff: 0.01f,
            substrateCoolingCoeff: 0.0f,
            substrateRadiativeCoeff: 0f);
        var thermal = new ThermalState
        {
            Temperature = 24f,
            AmbientTemp = 24f,
            SubstrateHeatLoad = 500f,
        };

        float newTemp = model.ComputeTemperature(thermal, 1.0f);

        // Expected: 24 + (500 * 0.01 - 0) * 1 = 29
        Assert.Equal(29f, newTemp, precision: 3);
    }

    [Fact]
    public void ComputeTemperature_HighPower_BoundedByRadiativeTerm()
    {
        // With the radiative loss term, even a 5 kW substrate-bound load reaches a finite
        // equilibrium well below 1000 C.
        var model = new VacuumPhysicsModel();
        var thermal = new ThermalState
        {
            Temperature = 24f,
            AmbientTemp = 24f,
            SubstrateHeatLoad = 5000f,
        };

        // Run to steady state with a 1 s step (the equilibrium does not depend on dt).
        for (int i = 0; i < 5000; i++)
            thermal.Temperature = model.ComputeTemperature(thermal, 1.0f);

        Assert.InRange(thermal.Temperature, 500f, 900f);
    }

    [Fact]
    public void ComputeTemperature_WithCoolingCoeff_DecaysTowardAmbient()
    {
        var model = new VacuumPhysicsModel(
            mfcResponseTau: 0.5f,
            substrateHeatingCoeff: 0.0f,
            substrateCoolingCoeff: 0.1f,
            substrateRadiativeCoeff: 0f);
        var thermal = new ThermalState
        {
            Temperature = 100f,
            AmbientTemp = 24f,
            HeatLoad = 0f,
        };

        float newTemp = model.ComputeTemperature(thermal, 1.0f);

        // Expected: 100 - (100 - 24) * 0.1 * 1 = 92.4
        Assert.Equal(92.4f, newTemp, precision: 3);
    }
}
