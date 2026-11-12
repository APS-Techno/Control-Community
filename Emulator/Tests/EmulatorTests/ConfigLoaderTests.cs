using APS.SystemEmulator.Engine;
using APS.Emulator.Devices.S7;

namespace EmulatorTests;

public class ConfigLoaderTests
{
    [Fact]
    public void LoadHWConfig_MinimalXml_ParsesValves()
    {
        string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <hardware>
              <valves lib="Vendor" hwtype="Vendor.S7200GasValve" host="main">
                <valve address="2" index="0" descr="Gas line 1">V1</valve>
                <valve address="2" index="1" descr="Gas line 2">V2</valve>
                <valve address="2" index="2" descr="Gas line 3">V3</valve>
              </valves>
            </hardware>
            """;
        string tempFile = Path.Combine(Path.GetTempPath(), $"hwconf_test_{Guid.NewGuid()}.xml");
        File.WriteAllText(tempFile, xml);

        try
        {
            var loader = new TestLoader();

            var devices = loader.LoadHWConfig(tempFile);

            // LoadHWConfig always appends a hardcoded RotationEmulator
            var valves = devices.Where(d => d is ValveEmulator).ToList();
            Assert.Equal(3, valves.Count);
            Assert.Equal("V1", valves[0].Id);
            Assert.Equal("Gas line 1", valves[0].Description);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void LoadHWConfig_MinimalXml_ParsesMFCs()
    {
        string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <hardware>
              <mfcs lib="Vendor" hwtype="Vendor.S7200MFC" host="main">
                <mfc address="160" descr="N2 MFC #1" chamber="Main">N21</mfc>
                <mfc address="168" descr="Ar MFC" chamber="Main">Ar1</mfc>
              </mfcs>
            </hardware>
            """;
        string tempFile = Path.Combine(Path.GetTempPath(), $"hwconf_test_{Guid.NewGuid()}.xml");
        File.WriteAllText(tempFile, xml);

        try
        {
            var loader = new TestLoader();
            var devices = loader.LoadHWConfig(tempFile);

            // LoadHWConfig always appends a hardcoded RotationEmulator
            var mfcs = devices.Where(d => d is MFCEmulator).ToList();
            Assert.Equal(2, mfcs.Count);
            Assert.Equal("N21", mfcs[0].Id);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void LoadHWConfig_MfcValveAttribute_ParsesInletValveId()
    {
        string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <hardware>
              <mfcs lib="Vendor" hwtype="Vendor.S7200MFC" host="main">
                <mfc address="160" descr="Ar MFC" chamber="Main" valve="V2">Ar1</mfc>
                <mfc address="168" descr="N2 MFC" chamber="Main">N21</mfc>
              </mfcs>
            </hardware>
            """;
        string tempFile = Path.Combine(Path.GetTempPath(), $"hwconf_test_{Guid.NewGuid()}.xml");
        File.WriteAllText(tempFile, xml);

        try
        {
            var loader = new TestLoader();
            var devices = loader.LoadHWConfig(tempFile);

            var mfcs = devices.OfType<MFCEmulator>().ToList();
            Assert.Equal(2, mfcs.Count);
            Assert.Equal("V2", mfcs[0].InletValveId);   // V2 = Ar line 1
            Assert.Equal("", mfcs[1].InletValveId); // no valve= attribute → ungated
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void LoadHWConfig_MinimalXml_ParsesSensors()
    {
        string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <hardware>
              <sensors lib="Vendor" hwtype="Vendor.S7200Sensor" host="main">
                <sensor address="100" chamber="Main" descr="Main chamber pressure">pm</sensor>
                <sensor address="104" chamber="Loadlock" descr="Loadlock pressure">pl</sensor>
              </sensors>
            </hardware>
            """;
        string tempFile = Path.Combine(Path.GetTempPath(), $"hwconf_test_{Guid.NewGuid()}.xml");
        File.WriteAllText(tempFile, xml);

        try
        {
            var loader = new TestLoader();
            var devices = loader.LoadHWConfig(tempFile);

            var sensors = devices.Where(d => d is SensorEmulator).ToList();
            Assert.Equal(2, sensors.Count);
            Assert.Equal("pm", sensors[0].Id);
            Assert.Equal("pl", sensors[1].Id);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void LoadHWConfig_MinimalXml_ParsesPumps()
    {
        string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <hardware>
              <pumps lib="Vendor" hwtype="Vendor.S7200Pump" host="main">
                <pump address="0" index="5" chamber="Main" gate="" descr="Scroll Pump 1">SP1</pump>
              </pumps>
            </hardware>
            """;
        string tempFile = Path.Combine(Path.GetTempPath(), $"hwconf_test_{Guid.NewGuid()}.xml");
        File.WriteAllText(tempFile, xml);

        try
        {
            var loader = new TestLoader();
            var devices = loader.LoadHWConfig(tempFile);

            var pumps = devices.Where(d => d is PumpEmulator).ToList();
            Assert.Single(pumps);
            Assert.Equal("SP1", pumps[0].Id);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void LoadHWConfig_MixedDevices_ParsesAll()
    {
        string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <hardware>
              <valves lib="Vendor" hwtype="Vendor.S7200GasValve" host="main">
                <valve address="2" index="0" descr="Gas line 1">V1</valve>
                <valve address="2" index="1" descr="Gas line 2">V2</valve>
                <valve address="2" index="2" descr="Gas line 3">V3</valve>
                <valve address="2" index="3" descr="Gas line 4">V4</valve>
                <valve address="2" index="4" descr="Gas line 5">V5</valve>
                <valve address="2" index="5" descr="Gas line 6">V6</valve>
                <valve address="2" index="6" descr="Gas line 7">V7</valve>
                <valve address="2" index="7" descr="Gas line 8">V8</valve>
                <valve address="3" index="0" descr="Gas line 9">V9</valve>
                <valve address="3" index="1" descr="Gas line 10">V10</valve>
                <valve address="3" index="2" descr="Gas line 11">V11</valve>
                <valve address="3" index="3" descr="Gas line 12">V12</valve>
                <valve address="3" index="4" descr="Isolation 1">V13</valve>
                <valve address="3" index="5" descr="Isolation 2">V14</valve>
                <valve address="3" index="6" descr="Vent 1">V15</valve>
                <valve address="3" index="7" descr="Vent 2">V16</valve>
              </valves>
              <mfcs lib="Vendor" hwtype="Vendor.S7200MFC" host="main">
                <mfc address="160" descr="N2 MFC #1" chamber="Main">N21</mfc>
              </mfcs>
              <sensors lib="Vendor" hwtype="Vendor.S7200Sensor" host="main">
                <sensor address="100" chamber="Main" descr="Main chamber pressure">pm</sensor>
              </sensors>
              <pumps lib="Vendor" hwtype="Vendor.S7200Pump" host="main">
                <pump address="0" index="5" chamber="Main" gate="" descr="Scroll Pump 1">SP1</pump>
              </pumps>
            </hardware>
            """;
        string tempFile = Path.Combine(Path.GetTempPath(), $"hwconf_test_{Guid.NewGuid()}.xml");
        File.WriteAllText(tempFile, xml);

        try
        {
            var loader = new TestLoader();
            var devices = loader.LoadHWConfig(tempFile);

            // 16 valves + 1 MFC + 1 sensor + 1 pump = 19
            Assert.True(devices.Count >= 16, $"Expected at least 16 valves but got {devices.Count} total devices");
            int valveCount = devices.Count(d => d is ValveEmulator);
            Assert.Equal(16, valveCount);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Loader_creates_VacPLCThrottleEmulator_from_controllers_block()
    {
        string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <hardware>
              <plcs lib="Vendor">
                <plc type="Vendor.S7200PLC" address="127.0.0.1">c1</plc>
              </plcs>
              <controllers lib="Vendor" hwtype="Vendor.S7200ThrottleValve" host="c1">
                <controller address="200" descr="C1 Throttle" chamber="C1">C1_TV</controller>
              </controllers>
            </hardware>
            """;
        string tempFile = Path.Combine(Path.GetTempPath(), $"hwconf_test_{Guid.NewGuid()}.xml");
        File.WriteAllText(tempFile, xml);

        try
        {
            var loader = new TestLoader();
            var devices = loader.LoadHWConfig(tempFile);

            var tv = devices.OfType<VacPLCThrottleEmulator>().SingleOrDefault();
            Assert.NotNull(tv);
            Assert.Equal("C1_TV", tv!.Id);
            Assert.Equal("C1", tv.ChamberId);
            Assert.Equal("c1", tv.Host);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
