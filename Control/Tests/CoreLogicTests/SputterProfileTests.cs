using System.Text.RegularExpressions;
using System.Xml.Linq;
using APS.SystemControl.Core.Logging;
using APS.SystemControl.GUIControls.Process;
using APS.SystemControl.Tests.Shared;
using Xunit;

namespace CoreLogicTests;

public class SputterProfileTests
{
    [Fact]
    public void EveryHwTypeResolvesInOpenLibraries()
    {
        var open = new[] { typeof(APS.Hardware.S7.S7200PLC).Assembly, typeof(APS.Hardware.Demo.LanPowerSupply).Assembly };
        foreach (var name in HwConfScan.TypeNames(ConfigsDir.Path("Sputter/HWConf.xml")))
            Assert.Contains(open, a => a.GetType(name) is not null);
    }

    [Fact]
    public void EveryPanelAndWorkerTypeIsOpen()
    {
        Assert.All(PanelsScan.Types(ConfigsDir.Path("Sputter/Panels.xml")),
            t => Assert.NotNull(typeof(APS.Controls.Generic.LanPsuPanel).Assembly.GetType(t)));
        Assert.Equal("APS.Workers.Generic.Operations", WorkerScan.TypeName(ConfigsDir.Path("Sputter/Worker.xml")));
        Assert.NotNull(typeof(APS.Workers.Generic.Operations));
    }

    [Fact]
    public void NoSerialPorts()
    {
        var text = string.Concat(Directory.GetFiles(ConfigsDir.Path("Sputter"), "*.xml", SearchOption.AllDirectories).Select(File.ReadAllText));
        Assert.DoesNotMatch(new Regex(@"COM\d+|RF301"), text);
    }

    static readonly string[] HwConfFiles = { "Sputter/HWConf.xml", "Sputter/HWConf.Emulator.xml" };

    [Fact]
    public void Sources_HaveMoAndSiO2()
    {
        var sources = XDocument.Load(ConfigsDir.Path("Sputter/Sources.xml")).Descendants("source").ToList();
        Assert.Equal("Mo", (string?)sources.Single(s => (string?)s.Attribute("flange") == "1").Element("material"));
        Assert.Equal("SiO2", (string?)sources.Single(s => (string?)s.Attribute("flange") == "2").Element("material"));
    }

    [Fact]
    public void Mfcs_Ungated()
    {
        foreach (var f in HwConfFiles)
            Assert.All(XDocument.Load(ConfigsDir.Path(f)).Descendants("mfc"), m => Assert.Null(m.Attribute("valve")));
    }

    [Fact]
    public void Worker_HasTwoArgDepositAndNoPwait()
    {
        var ops = XDocument.Load(ConfigsDir.Path("Sputter/Worker.xml")).Descendants("op").ToList();
        var deposit = ops.Single(o => (string?)o.Attribute("method") == "Deposit");
        Assert.Equal(new[] { "s", "f" }, deposit.Elements("var").Select(v => v.Value));
        Assert.DoesNotContain(ops, o => (string?)o.Attribute("method") == "Pwait");
    }

    [Fact]
    public void Plc_IsLoopback()
    {
        foreach (var f in HwConfFiles)
            Assert.All(XDocument.Load(ConfigsDir.Path(f)).Descendants("plc"), p => Assert.Equal("127.0.0.1", (string?)p.Attribute("address")));
    }

    [Fact]
    public void EmbedRecipes_ReferenceOnlyDevicesInHwConf()
    {
        var ids = XDocument.Load(ConfigsDir.Path("Sputter/HWConf.xml")).Descendants()
            .Where(e => !e.HasElements && e.Value.Trim().Length > 0).Select(e => e.Value.Trim()).ToHashSet();
        var commands = new[] { "valve", "pump", "mfc", "power", "stage" };
        var used = XDocument.Load(ConfigsDir.Path("Sputter/EmbedRecipes.xml")).Descendants("l")
            .Select(l => l.Value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(t => t.Length > 1 && commands.Contains(t[0].ToLowerInvariant()))
            .Select(t => t[1]);
        Assert.All(used, d => Assert.Contains(d, ids));
    }

    [Fact]
    public void Config_HasManualLoggingClockSensorSimtime500Ms()
    {
        var ml = XDocument.Load(ConfigsDir.Path("Sputter/Config.xml")).Root!.Element("ManualLogging");
        Assert.NotNull(ml);
        Assert.Equal("simtime", (string?)ml!.Attribute("clockSensor"));
        Assert.Equal("500", (string?)ml.Attribute("intervalMs"));

        var settings = ManualLoggingSettings.Load(ConfigsDir.Path("Sputter"));
        Assert.Equal("simtime", settings.ClockSensorId);
        Assert.Equal(500, settings.IntervalMs);
    }

    [Fact]
    public void HwConf_DeclaresCmPfSimtimeWithEmulatorOnlyModelAttributes()
    {
        var emulatorOnly = new[] { "minPressure", "maxPressure", "source", "backingPump", "forelineVolume", "forelineLimit" };
        foreach (var file in HwConfFiles)
        {
            var doc = XDocument.Load(ConfigsDir.Path(file));
            var sensors = doc.Descendants("sensor").ToDictionary(s => s.Value.Trim(), s => s);
            Assert.Equal("104", (string?)sensors["cm"].Attribute("address"));
            Assert.Equal("108", (string?)sensors["pf"].Attribute("address"));
            Assert.Equal("400", (string?)sensors["simtime"].Attribute("address"));
        }

        var controlDoc = XDocument.Load(ConfigsDir.Path("Sputter/HWConf.xml"));
        var controlSensors = controlDoc.Descendants("sensor").ToDictionary(s => s.Value.Trim(), s => s);
        foreach (var id in new[] { "cm", "pf", "simtime" })
            Assert.Null(controlSensors[id].Attribute("property"));
        Assert.All(controlDoc.Descendants(), e => Assert.All(emulatorOnly, a => Assert.Null(e.Attribute(a))));

        var emuDoc = XDocument.Load(ConfigsDir.Path("Sputter/HWConf.Emulator.xml"));
        var emuSensors = emuDoc.Descendants("sensor").ToDictionary(s => s.Value.Trim(), s => s);
        Assert.Equal("1e-4", (string?)emuSensors["cm"].Attribute("minPressure"));
        Assert.Equal("1e-2", (string?)emuSensors["cm"].Attribute("maxPressure"));
        Assert.Equal("ForelinePressure", (string?)emuSensors["pf"].Attribute("property"));
        Assert.Equal("TP", (string?)emuSensors["pf"].Attribute("source"));
        Assert.Equal("SimTime", (string?)emuSensors["simtime"].Attribute("property"));

        var tp = emuDoc.Descendants("pump").Single(p => p.Value.Trim() == "TP");
        Assert.Equal("SP1", (string?)tp.Attribute("backingPump"));
        Assert.Equal("2", (string?)tp.Attribute("forelineVolume"));
        Assert.Equal("5", (string?)tp.Attribute("forelineLimit"));
    }

    [Fact]
    public void ControlsAndProcessPanel_ExposeCmAndPfWithoutSimtime()
    {
        var ctrlSensors = XDocument.Load(ConfigsDir.Path("Sputter/Controls.xml")).Descendants("sensor")
            .ToDictionary(s => (string)s.Attribute("id")!, s => s);
        Assert.True(ctrlSensors.ContainsKey("cm"));
        Assert.True(ctrlSensors.ContainsKey("pf"));
        Assert.False(ctrlSensors.ContainsKey("simtime"));

        var spec = ProcessPanelSpec.Load(ConfigsDir.Path("Sputter/ProcessPanel.xml"));
        Assert.Empty(spec.Errors);
        var ids = spec.Readings.Select(r => r.Id).ToHashSet();
        Assert.Contains("cm", ids);
        Assert.Contains("pf", ids);
        Assert.DoesNotContain("simtime", ids);
        Assert.Null(spec.Readings.Single(r => r.Id == "cm").Device);
        Assert.Null(spec.Readings.Single(r => r.Id == "pf").Device);
    }
}
