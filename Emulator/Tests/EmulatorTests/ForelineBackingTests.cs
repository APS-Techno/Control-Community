using APS.SystemEmulator.Engine;
using APS.Emulator.Devices.S7;
using EmulatorCore.Interfaces;
using EmulatorCore.Models;

namespace EmulatorTests;

public sealed class ForelineBackingTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "aps-foreline-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Foreline_reaches_the_start_limit_only_with_running_backing_and_an_open_path()
    {
        using var engine = Open();
        var backing = engine.Devices.OfType<PumpEmulator>().Single();
        var pressure = engine.Devices.OfType<SensorEmulator>().Single();
        backing.SetRunning(true);
        Tick(engine, 200);
        Assert.InRange(pressure.CurrentValue, 700f, 800f);
        ((IValveEmulator)engine.Devices.OfType<ValveEmulator>().Single()).Command(true);
        Tick(engine, 200);
        Assert.InRange(pressure.CurrentValue, 0.0009f, 0.01f);
    }

    [Fact]
    public void Losing_backing_stops_sustained_turbo_pumping_after_foreline_pressure_rises()
    {
        using var engine = Open();
        var backing = engine.Devices.OfType<PumpEmulator>().Single();
        var pressure = engine.Devices.OfType<SensorEmulator>().Single();
        backing.SetRunning(true);
        ((IValveEmulator)engine.Devices.OfType<ValveEmulator>().Single()).Command(true);
        Tick(engine, 200);
        engine.Devices.OfType<InputValveEmulator>().Single().SetState(true);
        engine.Devices.OfType<TurboPumpEmulator>().Single().Command(true);
        Tick(engine, 20);
        backing.SetRunning(false);
        Tick(engine, 300);
        Assert.True(pressure.CurrentValue >= 4.9f);
        Assert.Equal(0f, engine.State.Chambers["Main"].PumpSpeed);
        Assert.True(engine.State.Chambers["Main"].Pressure > 0.001f);
    }

    [Fact]
    public void A_missing_backing_pump_cannot_silently_disable_the_backing_model()
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "missing.xml");
        File.WriteAllText(path, "<hardware><pumps hwtype='Vendor.S7200TurboPump' host='main'><pump address='17' chamber='Main' backingPump='missing'>TP</pump></pumps></hardware>");
        Assert.Throws<InvalidOperationException>(() => new TestLoader().LoadHWConfig(path));
    }

    [Fact]
    public void Restoring_a_pumped_foreline_does_not_reset_it_to_atmosphere()
    {
        using var engine = Open();
        engine.Devices.OfType<PumpEmulator>().Single().SetRunning(true);
        ((IValveEmulator)engine.Devices.OfType<ValveEmulator>().Single()).Command(true);
        Tick(engine, 200);
        float pressure = engine.Devices.OfType<TurboPumpEmulator>().Single().ForelinePressure;
        string path = Path.Combine(directory, "pumped.emstate");
        SnapshotService.SaveToFile(SnapshotService.Capture(engine.State, engine.Devices), path);
        using var restored = Open();
        SnapshotService.Restore(SnapshotService.LoadFromFile(path), restored.State, restored.Devices);
        Assert.Equal(pressure, restored.Devices.OfType<TurboPumpEmulator>().Single().ForelinePressure);
        Tick(restored, 10);
        Assert.InRange(restored.Devices.OfType<SensorEmulator>().Single().CurrentValue, 0.0009f, 0.01f);
    }

    private SimulationEngine Open(string backingId = "SP1")
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "HWConf.Emulator.xml");
        File.WriteAllText(path, $"""
            <hardware>
              <sensors hwtype="Vendor.S7200Sensor" host="main">
                <sensor address="108" chamber="Main" property="ForelinePressure" source="TP">pf</sensor>
              </sensors>
              <valves hwtype="Vendor.S7200Valve" host="main"><valve address="2" index="6">FL</valve></valves>
              <valves hwtype="Vendor.S7200InputValve" host="main"><valve address="2" index="5">GVM</valve></valves>
              <pumps hwtype="Vendor.S7200Pump" host="main"><pump address="0" index="5" speed="10" chamber="Main" gate="PP">SP1</pump></pumps>
              <pumps hwtype="Vendor.S7200TurboPump" host="main">
                <pump address="17" writeaddr="0" writeindex="4" speed="300" chamber="Main" gate="GVM" foreline="FL" backingPump="{backingId}" forelineVolume="2" forelineLimit="5">TP</pump>
              </pumps>
            </hardware>
            """);
        var physics = new PhysicsConfig { TurboSpinUpTime = 1f };
        var devices = new TestLoader(physics).LoadHWConfig(path);
        var engine = new SimulationEngine(physics);
        engine.State.SetChamber(new ChamberState { Id = "Main", Pressure = 0.02f, Volume = 1000f, BasePressure = 1e-7f });
        foreach (var device in devices) engine.AddDevice(device);
        engine.Time.Start();
        return engine;
    }

    private static void Tick(SimulationEngine engine, int count)
    {
        for (int i = 0; i < count; i++) engine.Tick();
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
