using APS.SystemEmulator.Engine;
using APS.Emulator.Devices.S7;
using EmulatorCore.Interfaces;
using EmulatorCore.Models;

namespace EmulatorTests;

public class SimulationEngineTests
{
    [Fact]
    public void Tick_FiresStateChangedEvent()
    {
        using var engine = new SimulationEngine();
        engine.Time.Start();
        SimulationState? received = null;
        engine.StateChanged += s => received = s;

        engine.Tick();

        Assert.NotNull(received);
        Assert.Same(engine.State, received);
    }

    [Fact]
    public void Tick_ResetsAggregatesAndUpdatesDevices()
    {
        using var engine = new SimulationEngine();
        engine.Time.Start();
        InitializeChamberDefaults(engine);

        // Set per-chamber pump speed to non-zero to verify per-tick reset
        engine.State.Chambers["Main"].PumpSpeed = 999f;
        engine.State.Chambers["Loadlock"].PumpSpeed = 999f;
        engine.State.Chambers["Main"].GasLoad = 999f;

        var mfc = new MFCEmulator("N21", 160, "Test MFC", "Main");
        mfc.WriteSetpoint(50f);
        engine.AddDevice(mfc);

        engine.Tick();

        // Chamber GasLoad was reset before device update — should equal
        // MFC contribution only, not 999 + contribution.
        Assert.True(engine.State.Chambers["Main"].GasLoad < 100f,
            $"Chamber GasLoad should be reset before device update, got {engine.State.Chambers["Main"].GasLoad}");
    }

    [Fact]
    public void Tick_AdvancesSimTime()
    {
        using var engine = new SimulationEngine();
        engine.TickIntervalMs = 100;
        engine.Time.Start();

        engine.Tick();

        Assert.Equal(0.1f, engine.Time.SimTime, 0.001f);
        Assert.Equal(0.1f, engine.State.SimTime, 0.001f);
    }

    [Fact]
    public void Tick_UpdatesAllDevices()
    {
        using var engine = new SimulationEngine();
        engine.Time.Start();
        InitializeChamberDefaults(engine);

        // Add valves so pump routing works
        var gvl = new ValveEmulator("GVL", 2, 7, "Gate LL");
        gvl.SetState(true); // open — connects loadlock to roughing
        engine.AddDevice(gvl);

        var fl = new ValveEmulator("FL", 2, 6, "Foreline");
        fl.SetState(true); // open — turbo foreline backing
        engine.AddDevice(fl);

        // GVM must be open for turbo to pump through the gate valve
        engine.State.ValveStates["GVM"] = true;

        var pump = new PumpEmulator("SP1", 0, 5, 10f, "Scroll Pump",
            chamberId: "Loadlock", gateValveId: "GVL");
        pump.SetRunning(true);
        engine.AddDevice(pump);

        var turbo = new TurboPumpEmulator("TP", 10, 0, 0, 300f, "Turbo Pump",
            chamberId: "Main", gateValveId: "GVM", forelineValveId: "FL");
        turbo.Command(true);
        engine.AddDevice(turbo);

        // Set main chamber to low pressure so turbo is effective below crossover
        engine.State.Chambers["Main"].Pressure = 0.01f;

        engine.Tick();

        // Pump routes to loadlock through GVL
        Assert.True(engine.State.Chambers["Loadlock"].PumpSpeed > 0f);
        // Turbo contributes to main chamber (GVM + FL open)
        Assert.True(engine.State.Chambers["Main"].PumpSpeed > 0f);
    }

    [Fact]
    public void Tick_AppliesChamberPhysics()
    {
        using var engine = new SimulationEngine();
        engine.Time.Start();
        InitializeChamberDefaults(engine);

        // Gate valve and foreline must be open for turbo to pump
        engine.State.ValveStates["GVM"] = true;
        var fl = new ValveEmulator("FL", 2, 6, "Foreline");
        fl.SetState(true);
        engine.AddDevice(fl);

        // Set initial pressure below turbo crossover so turbo can pump
        engine.State.Chambers["Main"].Pressure = 0.1f;
        float initialPressure = engine.State.Chambers["Main"].Pressure;

        var turbo = new TurboPumpEmulator("TP", 10, 0, 0, 300f, "Turbo Pump",
            chamberId: "Main", gateValveId: "GVM", forelineValveId: "FL");
        turbo.Command(true);
        engine.AddDevice(turbo);

        // Run several ticks to build up pump speed
        for (int i = 0; i < 10; i++)
            engine.Tick();

        Assert.True(engine.State.Chambers["Main"].Pressure < initialPressure,
            $"Pressure {engine.State.Chambers["Main"].Pressure} should be less than initial {initialPressure}");
    }

    [Fact]
    public void AddDevice_DeviceAppearsInList()
    {
        using var engine = new SimulationEngine();
        var valve = new ValveEmulator("V1", 2, 0, "Test Valve");

        engine.AddDevice(valve);

        Assert.Single(engine.Devices);
        Assert.Same(valve, engine.Devices[0]);
    }

    [Fact]
    public void TimeManager_PausedDoesNotAdvance()
    {
        var time = new TimeManager();
        time.Start();
        time.IsPaused = true;

        float dt = time.Advance(0.1f);

        Assert.Equal(0f, dt);
        Assert.Equal(0f, time.SimTime);
    }

    [Fact]
    public void TimeManager_SpeedMultiplierScalesTime()
    {
        var time = new TimeManager();
        time.Start();
        time.SpeedMultiplier = 2.0f;

        float dt = time.Advance(0.1f);

        Assert.Equal(0.2f, dt, 0.001f);
        Assert.Equal(0.2f, time.SimTime, 0.001f);
    }

    [Fact]
    public void TimeManager_ResetClearsState()
    {
        var time = new TimeManager();
        time.Start();
        time.Advance(1.0f);

        time.Reset();

        Assert.Equal(0f, time.SimTime);
        // After reset, Advance should not work since running is false
        float dt = time.Advance(0.1f);
        Assert.Equal(0f, dt);
    }

    [Fact]
    public void Start_AutoAdvancesSimTime()
    {
        using var engine = new SimulationEngine { TickIntervalMs = 20 };

        engine.Start();
        try
        {
            Thread.Sleep(200); // ~10 ticks of headroom
            Assert.True(engine.State.SimTime > 0f,
                $"SimTime should advance after Start(), got {engine.State.SimTime}");
        }
        finally { engine.Stop(); }
    }

    [Fact]
    public void Stop_HaltsTicking()
    {
        using var engine = new SimulationEngine { TickIntervalMs = 20 };
        engine.Start();
        Thread.Sleep(100);
        engine.Stop();

        float afterStop = engine.State.SimTime;
        Thread.Sleep(100);
        Assert.Equal(afterStop, engine.State.SimTime); // frozen after Stop
    }

    [Fact]
    public void PauseResume_GatesAdvance()
    {
        using var engine = new SimulationEngine { TickIntervalMs = 20 };
        engine.Start();
        Thread.Sleep(60);

        engine.Pause();
        Thread.Sleep(40); // let any in-flight tick settle
        float paused = engine.State.SimTime;
        Thread.Sleep(100);
        Assert.Equal(paused, engine.State.SimTime); // no advance while paused

        engine.Resume();
        Thread.Sleep(120);
        Assert.True(engine.State.SimTime > paused,
            $"SimTime should advance after Resume(), got {engine.State.SimTime} vs {paused}");

        engine.Stop();
    }

    [Fact]
    public void Stop_IsIdempotent_AndDisposeIsSafe()
    {
        var engine = new SimulationEngine { TickIntervalMs = 20 };
        engine.Start();
        Thread.Sleep(50);

        engine.Stop();
        engine.Stop();        // second Stop must not throw or hang
        engine.Dispose();     // Dispose after Stop must not throw
        engine.Dispose();     // double Dispose must be safe
    }

    [Fact]
    public void Start_CalledTwice_DoesNotSpawnSecondDriver()
    {
        using var engine = new SimulationEngine { TickIntervalMs = 20 };
        engine.Start();
        engine.Start(); // second Start must be a no-op, not a second tick thread
        Thread.Sleep(100);

        engine.Stop(); // joins the single driver thread
        float afterStop = engine.State.SimTime;
        Thread.Sleep(100);
        // If a second, unjoined thread were still running, SimTime would keep advancing.
        Assert.Equal(afterStop, engine.State.SimTime);
    }

    /// <summary>
    /// SimulationState does not pre-seed chambers, so engine-based tests must
    /// populate the dict explicitly.
    /// </summary>
    private static void InitializeChamberDefaults(SimulationEngine engine)
    {
        engine.State.ClearChambers();
        engine.State.SetChamber(new ChamberState
        {
            Id = "Main",
            Type = ChamberType.Process,
            Pressure = 760f,
            Volume = 50f,
            BasePressure = 2.2e-4f,
            BaseLeakRate = 1e-5f,
            LeakRate = 1e-5f,
        });
        engine.State.SetChamber(new ChamberState
        {
            Id = "Loadlock",
            Type = ChamberType.Loadlock,
            Pressure = 760f,
            Volume = 5f,
            BasePressure = 5e-2f,
            BaseLeakRate = 1e-5f,
            LeakRate = 1e-5f,
        });
    }
}
