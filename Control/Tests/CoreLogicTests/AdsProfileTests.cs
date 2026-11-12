using System.Text.RegularExpressions;
using System.Xml.Linq;
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
}
