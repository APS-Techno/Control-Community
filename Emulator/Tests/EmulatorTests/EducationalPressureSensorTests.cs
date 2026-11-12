using APS.SystemEmulator.Engine;
using APS.Emulator.Devices.S7;
using EmulatorCore.Interfaces;
using EmulatorCore.Models;

namespace EmulatorTests;

public sealed class EducationalPressureSensorTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "aps-gauge-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(760f, true)]
    [InlineData(1e-7f, false)]
    public void Outside_pressure_range_reports_a_non_numeric_status(float pressure, bool above)
    {
        var gauge = LoadSensor("minPressure='1e-4' maxPressure='1e-2'");
        var state = State(pressure);
        gauge.Update(0.1f, state);
        Assert.True(above ? float.IsPositiveInfinity(gauge.CurrentValue) : float.IsNegativeInfinity(gauge.CurrentValue));
    }

    [Fact]
    public void In_range_pressure_remains_a_pressure_measurement()
    {
        var gauge = LoadSensor("minPressure='1e-4' maxPressure='1e-2'");
        gauge.Update(0.1f, State(0.002f));
        Assert.InRange(gauge.CurrentValue, 1e-4f, 1e-2f);
    }

    [Theory]
    [InlineData("-1", "1")]
    [InlineData("1", "1")]
    [InlineData("2", "1")]
    [InlineData("0", "NaN")]
    public void Invalid_pressure_ranges_are_rejected(string minimum, string maximum)
    {
        Assert.ThrowsAny<ArgumentException>(() => LoadSensor($"minPressure='{minimum}' maxPressure='{maximum}'"));
    }

    [Fact]
    public void Simulation_clock_preserves_fractional_seconds_without_measurement_noise()
    {
        var clock = LoadSensor("property='SimTime'");
        var state = State(760f);
        state.SimTime = 12345.125f;
        for (int i = 0; i < 100; i++)
        {
            clock.Update(0.1f, state);
            Assert.Equal(12345.125f, clock.CurrentValue);
        }
        state.SimTime = 0;
        clock.Update(0.1f, state);
        Assert.Equal(0f, clock.CurrentValue);
    }

    [Theory]
    [InlineData(760f, true)]
    [InlineData(1e-7f, false)]
    public void Out_of_range_snapshots_round_trip_without_turning_status_into_zero(float pressure, bool above)
    {
        var gauge = LoadSensor("minPressure='1e-4' maxPressure='1e-2'");
        var state = State(pressure);
        gauge.Update(0.1f, state);
        string path = Path.Combine(directory, "range.emstate");
        SnapshotService.SaveToFile(SnapshotService.Capture(state, [gauge]), path);
        var restored = LoadSensor("minPressure='1e-4' maxPressure='1e-2'");
        SnapshotService.Restore(SnapshotService.LoadFromFile(path), State(pressure), [restored]);
        Assert.True(above ? float.IsPositiveInfinity(restored.CurrentValue) : float.IsNegativeInfinity(restored.CurrentValue));
    }

    private SensorEmulator LoadSensor(string attributes)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "HWConf.xml");
        File.WriteAllText(path, $"<hardware><sensors hwtype='Vendor.S7200Sensor' host='main'><sensor address='104' chamber='Main' {attributes}>cm</sensor></sensors></hardware>");
        return new TestLoader().LoadHWConfig(path).OfType<SensorEmulator>().Single();
    }

    private static SimulationState State(float pressure)
    {
        var state = new SimulationState { Physics = new VacuumPhysicsModel(), DataSource = new NullDataSource() };
        state.SetChamber(new ChamberState { Id = "Main", Pressure = pressure, Volume = 750f });
        return state;
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
