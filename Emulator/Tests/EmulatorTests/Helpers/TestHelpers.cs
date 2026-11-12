using APS.Emulator.Devices.S7;
using EmulatorCore.Interfaces;
using EmulatorCore.Models;

namespace EmulatorTests.Helpers;

public static class TestHelpers
{
    public static SimulationState CreateTestState()
    {
        var state = new SimulationState
        {
            Physics = new VacuumPhysicsModel(),
            DataSource = new NullDataSource(),
        };

        state.SetChamber(new ChamberState
        {
            Id = "Main",
            Type = ChamberType.Process,
            Pressure = 760f,
            Volume = 50f,
            BasePressure = 2.2e-4f,
            BaseLeakRate = 1e-5f,
            LeakRate = 1e-5f,
        });

        state.SetChamber(new ChamberState
        {
            Id = "Loadlock",
            Type = ChamberType.Loadlock,
            Pressure = 760f,
            Volume = 5f,
            BasePressure = 5e-2f,
            BaseLeakRate = 1e-5f,
            LeakRate = 1e-5f,
        });

        return state;
    }
}
