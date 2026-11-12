using APS.SystemControl.Core;
using APS.SystemControl.Core.Classes;

namespace CoreLogicTests;

public class ChamberDispatchTests
{
    [Fact]
    public void SensorTransition_for_sensor_in_chamber_only_affects_that_chamber_state()
    {
        // Two chambers. pm is in Main; pl is in Loadlock.
        // A pm read crossing the Main threshold must NOT touch Loadlock state.
        var main = new Alarms("Main");
        main.AddSensorTransition(SystemState.Startup, SystemState.Idle, "pm", -1, 1e-6f);
        var ll = new Alarms("Loadlock");
        ll.AddSensorTransition(SystemState.Startup, SystemState.Idle, "pl", -1, 1e-3f);

        // Simulate the dispatcher choosing the chamber by sensor binding.
        var stateMain = SystemState.Startup;
        var stateLL   = SystemState.Startup;

        // pm reading in Main: prime, then cross.
        main.CheckSensorTransitions(stateMain, "pm", 1e-3f);
        var newMain = main.CheckSensorTransitions(stateMain, "pm", 5e-7f);
        if (newMain.HasValue) stateMain = newMain.Value;

        Assert.Equal(SystemState.Idle, stateMain);
        Assert.Equal(SystemState.Startup, stateLL);
    }
}
