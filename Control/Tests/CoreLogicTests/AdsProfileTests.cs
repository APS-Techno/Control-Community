using System.Text.RegularExpressions;
using System.Xml.Linq;
using APS.SystemControl.Core.Logging;
using APS.SystemControl.GUIControls.Process;
using APS.SystemControl.Tests.Shared;
using Xunit;

namespace CoreLogicTests;

public class AdsProfileTests
{
    private static XDocument Load(string file) => XDocument.Load(ConfigsDir.Path("ADS/" + file));

    [Fact]
    public void EveryHwTypeResolvesInOpenLibraries()
    {
        var open = new[] { typeof(APS.Hardware.S7.S7200PLC).Assembly, typeof(APS.Hardware.Demo.LanPowerSupply).Assembly };
        foreach (var name in HwConfScan.TypeNames(ConfigsDir.Path("ADS/HWConf.xml")))
            Assert.Contains(open, a => a.GetType(name) is not null);
    }

    [Fact]
    public void EveryPanelAndWorkerTypeIsOpen()
    {
        Assert.All(PanelsScan.Types(ConfigsDir.Path("ADS/Panels.xml")),
            t => Assert.NotNull(typeof(APS.Controls.Generic.LanPsuPanel).Assembly.GetType(t)));
        Assert.Equal("APS.Workers.Generic.Operations", WorkerScan.TypeName(ConfigsDir.Path("ADS/Worker.xml")));
        Assert.NotNull(typeof(APS.Workers.Generic.Operations));
    }

    [Fact]
    public void NoSerialPorts()
    {
        var text = string.Concat(Directory.GetFiles(ConfigsDir.Path("ADS"), "*.xml", SearchOption.AllDirectories).Select(File.ReadAllText));
        Assert.DoesNotMatch(new Regex(@"COM\d+"), text);
    }

    [Fact]
    public void Worker_PwaitBoundToTc1()
    {
        var pwait = Load("Worker.xml").Descendants("op").Single(o => (string?)o.Attribute("method") == "Pwait");
        Assert.Equal("TC1", (string?)pwait.Attribute("device"));
        Assert.Equal(new[] { "Controller", "s", "f", "f" }, pwait.Elements("var").Select(v => v.Value));
    }

    [Fact]
    public void Worker_DepositBoundToHolderWithFourVars()
    {
        var deposit = Load("Worker.xml").Descendants("op").Single(o => (string?)o.Attribute("method") == "Deposit");
        Assert.Equal("Holder", (string?)deposit.Attribute("device"));
        Assert.Equal(new[] { "Drive", "s", "f", "s" }, deposit.Elements("var").Select(v => v.Value));
    }

    [Fact]
    public void Worker_HasNoBiasOrScanOps()
    {
        var methods = Load("Worker.xml").Descendants("op").Select(o => (string?)o.Attribute("method")).ToList();
        Assert.DoesNotContain("Bias", methods);
        Assert.DoesNotContain("BiasArb", methods);
        Assert.DoesNotContain("Scan", methods);
        Assert.DoesNotContain("ScanR", methods);
    }

    [Fact]
    public void Alarms_HasMainAndLoadlock()
    {
        var chambers = Load("Alarms.xml").Root!.Elements("chamber").Select(c => (string?)c.Attribute("id")).ToList();
        Assert.Equal(new[] { "Main", "Loadlock" }, chambers);

        var loadlock = Load("Alarms.xml").Root!.Elements("chamber").Single(c => (string?)c.Attribute("id") == "Loadlock");
        var toMaintenance = loadlock.Descendants("transition").Single(t => (string?)t.Attribute("to") == "maintenance");
        Assert.Equal("A2", (string?)toMaintenance.Attribute("valve"));
        Assert.Equal("open", (string?)toMaintenance.Attribute("state"));
        Assert.Equal("GVL closed", (string?)toMaintenance.Attribute("require"));
        Assert.Contains(loadlock.Descendants("transition"), t => (string?)t.Attribute("sensor") == "pl" && (string?)t.Attribute("to") == "idle");
    }

    [Fact]
    public void IdleAlarm_Delay60()
    {
        var main = Load("Alarms.xml").Root!.Elements("chamber").Single(c => (string?)c.Attribute("id") == "Main");
        var idle = main.Elements("alarm").Single(a => (string?)a.Attribute("sensor") == "pm" && (string?)a.Attribute("state") == "idle");
        Assert.Equal("60", (string?)idle.Attribute("delay"));
        Assert.Equal("1e-4", (string?)idle.Attribute("val"));
        var processing = main.Elements("alarm").Single(a => (string?)a.Attribute("sensor") == "pm" && (string?)a.Attribute("state") == "processing");
        Assert.Equal("2e-1", (string?)processing.Attribute("val"));
        Assert.Null(processing.Attribute("delay"));
    }

    [Theory]
    [InlineData("HWConf.xml")]
    [InlineData("HWConf.Emulator.xml")]
    public void Mfcs_OrGated(string file)
    {
        var gates = Load(file).Descendants("mfc").ToDictionary(m => m.Value, m => (string?)m.Attribute("valve"));
        Assert.Equal("V2,V3", gates["Ar1"]);
        Assert.Equal("V2,V3", gates["Ar2"]);
        Assert.Equal("V1,V4", gates["N21"]);
        Assert.Equal("V1,V4", gates["N22"]);
    }

    [Fact]
    public void Sources_MatchSpec()
    {
        var sources = Load("Sources.xml").Root!.Elements("source")
            .ToDictionary(s => (int)s.Attribute("flange")!);
        Assert.Equal(6, sources.Count);
        Assert.True(string.IsNullOrEmpty(sources[1].Element("material")?.Value));
        Assert.True(string.IsNullOrEmpty(sources[1].Element("powerSupplyId")?.Value));

        var expected = new Dictionary<int, (string Material, string Psu)>
        {
            [2] = ("Si", "PSU_A"),
            [3] = ("Graphite", "PSU_B"),
            [4] = ("Ion", "PSU_ION"),
            [5] = ("Ru", "PSU_C"),
            [6] = ("Co", "PSU_RF2"),
        };
        foreach (var (flange, (material, psu)) in expected)
        {
            Assert.Equal(material, sources[flange].Element("material")!.Value);
            Assert.Equal(psu, sources[flange].Element("powerSupplyId")!.Value);
            Assert.Equal("MAG" + flange, sources[flange].Element("magnetronId")!.Value);
        }
        Assert.Equal("Si", (string?)sources[2].Attribute("name"));
        Assert.Equal("C", (string?)sources[3].Attribute("name"));
        Assert.Equal("Ru", (string?)sources[5].Attribute("name"));
        Assert.Equal("Co", (string?)sources[6].Attribute("name"));

        var magnetrons = Load("Sources.xml").Root!.Element("magnetrons")!.Elements("magnetron").Select(m => (string?)m.Attribute("id"));
        Assert.Equal(new[] { "MAG1", "MAG2", "MAG3", "MAG4", "MAG5", "MAG6" }, magnetrons);
        Assert.DoesNotContain(Load("Sources.xml").Descendants("powerSupplyId"), p => p.Value == "PSU_RF1");
    }

    [Fact]
    public void LanDevices_UseConstructorAttributeOrder()
    {
        var hw = Load("HWConf.xml");
        Assert.All(hw.Descendants("powersupply"), e => Assert.Equal(new[] { "host", "port", "mode", "descr" }, e.Attributes().Select(a => a.Name.LocalName)));
        Assert.All(hw.Descendants("drive"), e => Assert.Equal(new[] { "host", "port", "descr" }, e.Attributes().Select(a => a.Name.LocalName)));
        var tc = hw.Descendants("controller").Single();
        Assert.Equal(new[] { "host", "port", "descr" }, tc.Attributes().Select(a => a.Name.LocalName).Take(3));
        Assert.Equal("TC1", tc.Value);
    }

    [Fact]
    public void Config_HasManualLoggingClockSensorSimtime500Ms()
    {
        var ml = Load("Config.xml").Root!.Element("ManualLogging");
        Assert.NotNull(ml);
        Assert.Equal("simtime", (string?)ml!.Attribute("clockSensor"));
        Assert.Equal("500", (string?)ml.Attribute("intervalMs"));

        var settings = ManualLoggingSettings.Load(ConfigsDir.Path("ADS"));
        Assert.Equal("simtime", settings.ClockSensorId);
        Assert.Equal(500, settings.IntervalMs);
    }

    [Fact]
    public void HwConf_DeclaresCmPfSimtimeThickWithEmulatorOnlyModelAttributes()
    {
        var emulatorOnly = new[] { "minPressure", "maxPressure", "source", "backingPump", "forelineVolume", "forelineLimit" };
        foreach (var file in new[] { "HWConf.xml", "HWConf.Emulator.xml" })
        {
            var sensors = Load(file).Descendants("sensor").ToDictionary(s => s.Value.Trim(), s => s);
            Assert.Equal("104", (string?)sensors["cm"].Attribute("address"));
            Assert.Equal("108", (string?)sensors["pf"].Attribute("address"));
            Assert.Equal("400", (string?)sensors["simtime"].Attribute("address"));
            Assert.Equal("404", (string?)sensors["thick"].Attribute("address"));
        }

        var controlDoc = Load("HWConf.xml");
        var controlSensors = controlDoc.Descendants("sensor").ToDictionary(s => s.Value.Trim(), s => s);
        foreach (var id in new[] { "cm", "pf", "simtime", "thick" })
            Assert.Null(controlSensors[id].Attribute("property"));
        Assert.Contains("thickness", (string?)controlSensors["thick"].Attribute("descr") ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.All(controlDoc.Descendants(), e => Assert.All(emulatorOnly, a => Assert.Null(e.Attribute(a))));

        var emuDoc = Load("HWConf.Emulator.xml");
        var emuSensors = emuDoc.Descendants("sensor").ToDictionary(s => s.Value.Trim(), s => s);
        Assert.Equal("1e-4", (string?)emuSensors["cm"].Attribute("minPressure"));
        Assert.Equal("1e-2", (string?)emuSensors["cm"].Attribute("maxPressure"));
        Assert.Equal("ForelinePressure", (string?)emuSensors["pf"].Attribute("property"));
        Assert.Equal("TP", (string?)emuSensors["pf"].Attribute("source"));
        Assert.Equal("SimTime", (string?)emuSensors["simtime"].Attribute("property"));
        Assert.Equal("Thickness", (string?)emuSensors["thick"].Attribute("property"));

        var tp = emuDoc.Descendants("pump").Single(p => p.Value.Trim() == "TP");
        Assert.Equal("SP1", (string?)tp.Attribute("backingPump"));
        Assert.Equal("2", (string?)tp.Attribute("forelineVolume"));
        Assert.Equal("5", (string?)tp.Attribute("forelineLimit"));
    }

    [Fact]
    public void ControlsAndProcessPanel_ExposeCmPfAndThickWithoutSimtime()
    {
        var ctrlSensors = Load("Controls.xml").Descendants("sensor")
            .ToDictionary(s => (string)s.Attribute("id")!, s => s);
        Assert.True(ctrlSensors.ContainsKey("cm"));
        Assert.True(ctrlSensors.ContainsKey("pf"));
        Assert.True(ctrlSensors.ContainsKey("thick"));
        Assert.Equal("Film", ctrlSensors["thick"].Value.Trim());
        Assert.Equal("nm", (string?)ctrlSensors["thick"].Attribute("unit"));
        Assert.False(ctrlSensors.ContainsKey("simtime"));

        var spec = ProcessPanelSpec.Load(ConfigsDir.Path("ADS/ProcessPanel.xml"));
        Assert.Empty(spec.Errors);
        var byId = spec.Readings.ToDictionary(r => r.Id);
        Assert.True(byId.ContainsKey("cm"));
        Assert.True(byId.ContainsKey("pf"));
        Assert.True(byId.ContainsKey("thick"));
        Assert.False(byId.ContainsKey("simtime"));
        Assert.Equal("Film", byId["thick"].DisplayLabel);
        Assert.Equal("nm", byId["thick"].Units);
        Assert.Null(byId["cm"].Device);
        Assert.Null(byId["pf"].Device);
        Assert.Null(byId["thick"].Device);
    }

    [Fact]
    public void InterlocksMain_EnforcesValvePpOpenAndValveFlClose()
    {
        var interlocks = Load("Interlocks/Interlocks.Main.xml").Descendants("interlock")
            .ToDictionary(i => (string)i.Attribute("id")!, i => i.Elements("l").Select(l => l.Value.Trim()).ToList());

        Assert.Equal(["Valve GVM closed", "Valve FL closed", "Pump SP1 on", "Pump TP off"], interlocks["Valve_PP_open"]);
        Assert.Equal(["Valve GVM closed"], interlocks["Valve_FL_close"]);
    }

    [Fact]
    public void EmbedRecipes_FailureProtocol_ShutsReactiveGasBeforePowerOff()
    {
        var failure = Load("EmbedRecipes.xml").Descendants("recipe")
            .Single(r => (string?)r.Attribute("id") == "FAILURE_PROTOCOL")
            .Elements("l").Select(l => l.Value.Trim()).ToList();

        int firstPowerOff = failure.FindIndex(l => l.StartsWith("power ", StringComparison.Ordinal));
        Assert.True(firstPowerOff >= 0);
        foreach (var reactiveStep in new[] { "shutter Home", "holder Home", "MFC N21 0", "MFC N22 0", "valve V1 close", "valve V4 close" })
        {
            int idx = failure.IndexOf(reactiveStep);
            Assert.True(idx >= 0 && idx < firstPowerOff, $"Expected '{reactiveStep}' before first power off, index={idx}, firstPowerOff={firstPowerOff}");
        }
        foreach (var psu in new[] { "PSU_A", "PSU_B", "PSU_C", "PSU_RF1", "PSU_RF2" })
            Assert.Contains($"power {psu} off", failure);
    }
}
