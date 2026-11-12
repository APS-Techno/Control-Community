using System.Text.RegularExpressions;
using System.Xml.Linq;
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
}
