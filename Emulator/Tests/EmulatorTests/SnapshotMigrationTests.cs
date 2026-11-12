using EmulatorCore.Interfaces;
using EmulatorCore.Models;
using Xunit;

namespace EmulatorTests;

public class SnapshotMigrationTests
{
    /// <summary>Pre-2026-06 shape: chambers as top-level objects, no Id/Type, no Chambers dict.</summary>
    private const string LegacySnapshot = """
    {
      "Timestamp": "2026-05-11T09:42:15.300867+08:00",
      "SimTime": 13.7,
      "MainChamber": { "Pressure": 760.0, "Volume": 750.0, "BasePressure": 1E-07,
                       "BaseLeakRate": 1E-06, "LeakRate": 1E-06, "PumpSpeed": 0.0,
                       "GasLoad": 0.0, "ThrottlePosition": 100.0 },
      "Loadlock":    { "Pressure": 760.0, "Volume": 20.0, "BasePressure": 0.0005,
                       "BaseLeakRate": 1E-05, "LeakRate": 1E-05, "PumpSpeed": 0.0,
                       "GasLoad": 0.0, "ThrottlePosition": 0.0 },
      "Thermal": { "Temperature": 24.0, "AmbientTemp": 24.0, "HeatLoad": 0.0 },
      "ValveStates": { "V1": false, "A1": true },
      "TotalGasFlow": 0.0,
      "RoughingPumpSpeed": 0.0,
      "DeviceStates": {}
    }
    """;

    private const string CurrentSnapshot = """
    {
      "Timestamp": "2026-06-10T09:59:17.0488282+08:00",
      "SimTime": 67.9,
      "Chambers": {
        "Main":     { "Id": "Main", "Type": 0, "Pressure": 1E-07, "Volume": 750.0,
                      "BasePressure": 1E-07, "BaseLeakRate": 1E-06, "LeakRate": 1E-06,
                      "PumpSpeed": 2300.0, "GasLoad": 0.0, "ThrottlePosition": 100.0 },
        "Loadlock": { "Id": "Loadlock", "Type": 1, "Pressure": 0.0012, "Volume": 20.0,
                      "BasePressure": 0.0005, "BaseLeakRate": 1E-05, "LeakRate": 1E-05,
                      "PumpSpeed": 8.33, "GasLoad": 0.0, "ThrottlePosition": 0.0 }
      },
      "Thermal": { "Temperature": 24.0, "ChamberTemperature": 24.0, "AmbientTemp": 24.0,
                   "HeatLoad": 0.0, "SubstrateHeatLoad": 0.0 },
      "ValveStates": { "V1": false },
      "DeviceStates": {}
    }
    """;

    private static string WriteTemp(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), $"aps-snap-{Guid.NewGuid():N}.emstate");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void LoadFromFile_LegacySnapshot_RestoresBothChambersWithTheirPressures()
    {
        string path = WriteTemp(LegacySnapshot);
        try
        {
            var snap = SnapshotService.LoadFromFile(path);

            Assert.Equal(2, snap.Chambers.Count);
            Assert.Equal(760.0f, snap.Chambers["Main"].Pressure);
            Assert.Equal(760.0f, snap.Chambers["Loadlock"].Pressure);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LoadFromFile_LegacySnapshot_AssignsIdAndChamberType()
    {
        string path = WriteTemp(LegacySnapshot);
        try
        {
            var snap = SnapshotService.LoadFromFile(path);

            Assert.Equal("Main", snap.Chambers["Main"].Id);
            Assert.Equal(ChamberType.Process, snap.Chambers["Main"].Type);
            Assert.Equal("Loadlock", snap.Chambers["Loadlock"].Id);
            Assert.Equal(ChamberType.Loadlock, snap.Chambers["Loadlock"].Type);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LoadFromFile_LegacySnapshot_KeepsTheRestOfTheChamberFields()
    {
        string path = WriteTemp(LegacySnapshot);
        try
        {
            var main = SnapshotService.LoadFromFile(path).Chambers["Main"];

            Assert.Equal(750.0f, main.Volume);
            Assert.Equal(1E-07f, main.BasePressure);
            Assert.Equal(100.0f, main.ThrottlePosition);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LoadFromFile_CurrentSnapshot_IsUnaffectedByMigration()
    {
        string path = WriteTemp(CurrentSnapshot);
        try
        {
            var snap = SnapshotService.LoadFromFile(path);

            Assert.Equal(2, snap.Chambers.Count);
            Assert.Equal(1E-07f, snap.Chambers["Main"].Pressure);
            Assert.Equal(2300.0f, snap.Chambers["Main"].PumpSpeed);
        }
        finally { File.Delete(path); }
    }

    /// <summary>A snapshot that restores NOTHING must not do so quietly: schema drift has to
    /// fail loudly instead of leaving the machine wherever it happened to be.</summary>
    [Fact]
    public void LoadFromFile_SnapshotWithNoRecognisableChambers_ThrowsNamingTheFile()
    {
        string path = WriteTemp("""
        {"Timestamp":"2026-01-01T00:00:00+08:00","SimTime":0,
         "Thermal":{"Temperature":24.0},"ValveStates":{},"DeviceStates":{}}
        """);
        try
        {
            var ex = Assert.Throws<InvalidDataException>(() => SnapshotService.LoadFromFile(path));
            Assert.Contains(path, ex.Message);
            Assert.Contains("chamber", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SaveThenLoad_RoundTripsChamberPressures()
    {
        string path = WriteTemp(CurrentSnapshot);
        try
        {
            var loaded = SnapshotService.LoadFromFile(path);
            string outPath = WriteTemp("{}");
            try
            {
                SnapshotService.SaveToFile(loaded, outPath);
                var again = SnapshotService.LoadFromFile(outPath);
                Assert.Equal(loaded.Chambers["Main"].Pressure, again.Chambers["Main"].Pressure);
                Assert.Equal(loaded.Chambers["Loadlock"].Pressure, again.Chambers["Loadlock"].Pressure);
            }
            finally { File.Delete(outPath); }
        }
        finally { File.Delete(path); }
    }
}
