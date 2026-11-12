using System.Text.RegularExpressions;
using System.Xml.Linq;
using APS.SystemControl.Tests.Shared;
using Xunit;

namespace CoreLogicTests;

public class ClusterProfileTests
{
    static readonly string[] HwConfFiles = { "Cluster/HWConf.xml", "Cluster/HWConf.Emulator.xml" };

    [Fact]
    public void EveryHwTypeResolvesInOpenLibraries()
    {
        var open = new[] { typeof(APS.Hardware.S7.S7200PLC).Assembly, typeof(APS.Hardware.Demo.LanPowerSupply).Assembly };
        foreach (var file in HwConfFiles)
            foreach (var name in HwConfScan.TypeNames(ConfigsDir.Path(file)))
                Assert.Contains(open, a => a.GetType(name) is not null);
    }

    [Fact]
    public void EveryPanelAndWorkerTypeIsOpen()
    {
        Assert.All(PanelsScan.Types(ConfigsDir.Path("Cluster/Panels.xml")),
            t => Assert.NotNull(typeof(APS.Controls.Generic.LanPsuPanel).Assembly.GetType(t)));
        Assert.Equal("APS.Workers.Generic.Operations", WorkerScan.TypeName(ConfigsDir.Path("Cluster/Worker.xml")));
        Assert.NotNull(typeof(APS.Workers.Generic.Operations));
    }

    [Fact]
    public void NoSerialPorts()
    {
        var text = string.Concat(Directory.GetFiles(ConfigsDir.Path("Cluster"), "*.xml", SearchOption.AllDirectories).Select(File.ReadAllText));
        Assert.DoesNotMatch(new Regex(@"COM\d+"), text);
    }

    [Fact]
    public void Plcs_AreDistinctLoopbackAddresses()
    {
        var expected = new[] { "127.0.0.1", "127.0.0.2", "127.0.0.3", "127.0.0.4", "127.0.0.5" };
        foreach (var f in HwConfFiles)
        {
            var plcs = XDocument.Load(ConfigsDir.Path(f)).Descendants("plc").ToList();
            Assert.Equal(new[] { "tm", "c1", "c2", "c3", "c4" }, plcs.Select(p => p.Value));
            Assert.Equal(expected, plcs.Select(p => (string?)p.Attribute("address")));
        }
    }

    [Fact]
    public void Worker_PwaitUnboundThreeVars()
    {
        var pwait = Ops().Single(o => (string?)o.Attribute("method") == "Pwait");
        Assert.Null(pwait.Attribute("device"));
        Assert.Equal(new[] { "s", "f", "f" }, pwait.Elements("var").Select(v => v.Value));
    }

    [Fact]
    public void Worker_DepositUnboundTwoVars()
    {
        var deposit = Ops().Single(o => (string?)o.Attribute("method") == "Deposit");
        Assert.Null(deposit.Attribute("device"));
        Assert.Equal(new[] { "s", "f" }, deposit.Elements("var").Select(v => v.Value));
    }

    [Fact]
    public void Worker_RobotOpsBoundToRobot()
    {
        foreach (var method in new[] { "Transfer", "Home", "Abort" })
            Assert.Equal("ROBOT", (string?)Ops().Single(o => (string?)o.Attribute("method") == method).Attribute("device"));
    }

    [Fact]
    public void Alarms_SixChambersWithUnless()
    {
        var chambers = XDocument.Load(ConfigsDir.Path("Cluster/Alarms.xml")).Descendants("chamber").ToList();
        Assert.Equal(new[] { "TM", "LL", "C1", "C2", "C3", "C4" }, chambers.Select(c => (string?)c.Attribute("id")));
        foreach (var c in chambers.Where(c => (string?)c.Attribute("id") != "TM"))
        {
            var id = (string?)c.Attribute("id");
            Assert.Contains(c.Elements("alarm"), a => (string?)a.Attribute("unless") == id + "_A1 open");
        }
        var tm = chambers.Single(c => (string?)c.Attribute("id") == "TM");
        Assert.Contains(tm.Elements("alarm"), a => (string?)a.Attribute("device") == "ROBOT" && (string?)a.Attribute("status") == "error");
    }

    [Fact]
    public void Robot_HasFiveFaces()
    {
        foreach (var f in HwConfFiles)
        {
            var robot = XDocument.Load(ConfigsDir.Path(f)).Descendants("robot").Single();
            Assert.Equal(new[] { "LL", "C1", "C2", "C3", "C4" }, robot.Elements("face").Select(x => (string?)x.Attribute("name")));
            Assert.Equal(new[] { "SV_LL", "SV_C1", "SV_C2", "SV_C3", "SV_C4" }, robot.Elements("face").Select(x => (string?)x.Attribute("slit")));
            Assert.Equal("5031", (string?)robot.Attribute("port"));
        }
    }

    [Fact]
    public void PowerSupplies_MatchPortMap()
    {
        var expected = new[]
        {
            ("C1_PSU1", "5025", "PDC"), ("C1_PSU2", "5026", "RF"), ("C2_PSU", "5027", "PDC"),
            ("C3_PSU1", "5028", "RF"), ("C3_PSU2", "5029", "RF"), ("C4_PSU", "5030", "PDC"),
        };
        foreach (var f in HwConfFiles)
        {
            var supplies = XDocument.Load(ConfigsDir.Path(f)).Descendants("powersupply")
                .Select(p => (p.Value, (string?)p.Attribute("port"), (string?)p.Attribute("mode")));
            Assert.Equal(expected, supplies);
        }
    }

    [Fact]
    public void Sources_MatchSpec()
    {
        var expected = new[]
        {
            ("1", "C1", "Cu", "C1_PSU1", "C1_Sh"),
            ("5", "C1", "Ti", "C1_PSU2", "C1_Sh_B"),
            ("2", "C2", "W", "C2_PSU", "C2_Sh"),
            ("3", "C3", "Si", "C3_PSU1", "C3_Sh"),
            ("6", "C3", "B4C", "C3_PSU2", "C3_Sh_B"),
            ("4", "C4", "Cu", "C4_PSU", "C4_Sh"),
        };
        var sources = XDocument.Load(ConfigsDir.Path("Cluster/Sources.xml")).Descendants("source")
            .Select(s => ((string?)s.Attribute("flange"), (string?)s.Attribute("chamber"), (string?)s.Element("material"),
                (string?)s.Element("powerSupplyId"), (string?)s.Attribute("shutterId")));
        Assert.Equal(expected, sources);
        Assert.Equal(new[] { "MAG1", "MAG2", "MAG3", "MAG4", "MAG5", "MAG6" },
            XDocument.Load(ConfigsDir.Path("Cluster/Sources.xml")).Descendants("source").Select(s => (string?)s.Element("magnetronId")));
    }

    static List<XElement> Ops() => XDocument.Load(ConfigsDir.Path("Cluster/Worker.xml")).Descendants("op").ToList();
}
