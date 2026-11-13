using System.Globalization;
using APS.SystemControl.GUIControls.Process;
using Xunit;

namespace CoreLogicTests;

[Collection("ProcessPanelSpec")]
public class ProcessPanelSpecTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("pps").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private string Write(string body)
    {
        var path = Path.Combine(_dir, "ProcessPanel.xml");
        File.WriteAllText(path, $"<processPanel>{body}</processPanel>");
        return path;
    }

    [Fact]
    public void Load_ValidFile_ReturnsReadingsInDeclarationOrder()
    {
        var spec = ProcessPanelSpec.Load(Write("""
            <readings>
              <reading id="pm" label="pm" group="Pressure" format="E1" unit="Torr" scale="Log" accent="Vacuum"/>
              <reading id="PSU-A_V" label="PSU-A V" group="Power" format="F0" unit="V" device="PSU-A" section="Supplies"/>
              <reading id="Gas1" label="Gas1" group="MassFlow" format="F1" unit="sccm" accent="Ar" sparkline="false"/>
            </readings>
            """));

        Assert.Empty(spec.Errors);
        Assert.Equal(["pm", "PSU-A_V", "Gas1"], spec.Readings.Select(r => r.Id));
        var pm = spec.Readings[0];
        Assert.Equal(ReadingGroup.Pressure, pm.Group);
        Assert.Equal("E1", pm.Format);
        Assert.Equal("Torr", pm.Units);
        Assert.Equal(ReadingScale.Log, pm.Scale);
        Assert.Equal(ReadingPalette.Vacuum, pm.Accent);
        var psu = spec.Readings[1];
        Assert.Equal("PSU-A", psu.Device);
        Assert.Equal("Supplies", psu.Section);
        Assert.False(spec.Readings[2].ShowSparkline);
        Assert.Equal(ReadingPalette.Ar, spec.Readings[2].Accent);
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmptyWithoutErrors()
    {
        var spec = ProcessPanelSpec.Load(Path.Combine(_dir, "nope.xml"));
        Assert.Empty(spec.Readings);
        Assert.Empty(spec.Errors);
        Assert.Empty(ProcessPanelSpec.Load(null).Errors);
    }

    [Fact]
    public void Load_UnknownGroup_SkipsEntryAndReportsIt()
    {
        var spec = ProcessPanelSpec.Load(Write("""
            <readings>
              <reading id="bad1" label="x" group="Nonsense" format="F0" unit="V"/>
              <reading id="good" label="x" group="Power" format="F0" unit="V"/>
            </readings>
            """));
        Assert.Equal(["good"], spec.Readings.Select(r => r.Id));
        Assert.Single(spec.Errors);
        Assert.Contains("bad1", spec.Errors[0]);
    }

    [Fact]
    public void Load_UnknownAccent_SkipsEntryAndReportsIt()
    {
        var spec = ProcessPanelSpec.Load(Write("""
            <readings>
              <reading id="bad2" label="x" group="Power" format="F0" unit="V" accent="Mauve"/>
              <reading id="good" label="x" group="Power" format="F0" unit="V"/>
            </readings>
            """));
        Assert.Equal(["good"], spec.Readings.Select(r => r.Id));
        Assert.Single(spec.Errors);
        Assert.Contains("bad2", spec.Errors[0]);
    }

    [Fact]
    public void Load_DuplicateId_KeepsFirstAndReportsSecond()
    {
        var spec = ProcessPanelSpec.Load(Write("""
            <readings>
              <reading id="pm" label="first" group="Pressure" format="E1" unit="Torr"/>
              <reading id="pm" label="second" group="Pressure" format="E1" unit="Torr"/>
            </readings>
            """));
        Assert.Single(spec.Readings);
        Assert.Equal("first", spec.Readings[0].DisplayLabel);
        Assert.Single(spec.Errors);
        Assert.Contains("pm", spec.Errors[0]);
    }

    [Fact]
    public void Load_EmptyId_SkipsEntry()
    {
        var spec = ProcessPanelSpec.Load(Write("""
            <readings>
              <reading id="" label="x" group="Power" format="F0" unit="V"/>
              <reading label="y" group="Power" format="F0" unit="V"/>
            </readings>
            """));
        Assert.Empty(spec.Readings);
        Assert.Equal(2, spec.Errors.Count);
    }

    [Fact]
    public void Load_MalformedXml_ReturnsEmptyWithOneError()
    {
        var path = Path.Combine(_dir, "ProcessPanel.xml");
        File.WriteAllText(path, "<processPanel><readings>");
        var spec = ProcessPanelSpec.Load(path);
        Assert.Empty(spec.Readings);
        Assert.Single(spec.Errors);
    }

    [Fact]
    public void Load_ValveGroupsAndDrives_PreserveOrderAndText()
    {
        var spec = ProcessPanelSpec.Load(Write("""
            <valveGroups>
              <group header="Gas inlets"><valve id="V1" descr="N2 inlet"/><valve id="V2" descr="Ar inlet"/></group>
              <group header="Chamber"><valve id="V5" descr="Inlet"/></group>
            </valveGroups>
            <drives><drive id="D1" label="Holder"/><drive id="D2" label="Shutter"/></drives>
            """));
        Assert.Empty(spec.Errors);
        Assert.Equal(["Gas inlets", "Chamber"], spec.ValveGroups.Select(g => g.Header));
        Assert.Equal([("V1", "N2 inlet"), ("V2", "Ar inlet")],
            spec.ValveGroups[0].Valves.Select(v => (v.Id, v.Description)));
        Assert.Equal(["Holder", "Shutter"], spec.Drives.Select(d => d.Label));
    }

    [Fact]
    public void Load_UnderRuRuCulture_ParsesIdentically()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ru-RU");
            var spec = ProcessPanelSpec.Load(Write("""
                <readings>
                  <reading id="pm" label="pm" group="Pressure" format="E1" unit="Torr" scale="Log" accent="vacuum"/>
                </readings>
                """));
            Assert.Empty(spec.Errors);
            Assert.Equal(ReadingScale.Log, spec.Readings[0].Scale);
            Assert.Equal(ReadingPalette.Vacuum, spec.Readings[0].Accent);
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }

    [Fact]
    public void Load_InvalidNumberFormat_SkipsEntryAndReportsIt()
    {
        var spec = ProcessPanelSpec.Load(Write("""
            <readings>
              <reading id="bad" label="x" group="Power" format="D2" unit="V"/>
              <reading id="good" label="x" group="Power" format="F1" unit="V"/>
            </readings>
            """));
        Assert.Equal(["good"], spec.Readings.Select(r => r.Id));
        Assert.Single(spec.Errors);
        Assert.Contains("bad", spec.Errors[0]);
    }

    [Fact]
    public void Load_InvalidFirstRowThenValidRowWithSameId_KeepsTheValidRow()
    {
        var spec = ProcessPanelSpec.Load(Write("""
            <readings>
              <reading id="pm" label="bad" group="Nonsense" format="E1" unit="Torr"/>
              <reading id="pm" label="good" group="Pressure" format="E1" unit="Torr"/>
            </readings>
            """));
        Assert.Equal(["good"], spec.Readings.Select(r => r.DisplayLabel));
        Assert.Single(spec.Errors);
        Assert.DoesNotContain("duplicate", spec.Errors[0]);
    }

    [Fact]
    public void Load_DuplicateValveAndDriveIds_KeepFirstAndReportSecond()
    {
        var spec = ProcessPanelSpec.Load(Write("""
            <valveGroups>
              <group header="A"><valve id="V1" descr="first"/></group>
              <group header="B"><valve id="V1" descr="second"/></group>
            </valveGroups>
            <drives><drive id="D1" label="first"/><drive id="D1" label="second"/></drives>
            """));
        Assert.Equal(["first"], spec.ValveGroups.SelectMany(g => g.Valves).Select(v => v.Description));
        Assert.Equal(["first"], spec.Drives.Select(d => d.Label));
        Assert.Equal(2, spec.Errors.Count);
    }

    [Fact]
    public void Load_WrongRootElement_ReportsAnError()
    {
        var path = Path.Combine(_dir, "ProcessPanel.xml");
        File.WriteAllText(path, "<processpanel><readings/></processpanel>");
        var spec = ProcessPanelSpec.Load(path);
        Assert.Empty(spec.Readings);
        Assert.Single(spec.Errors);
    }

    [Fact]
    public void Current_DefaultsToEmpty()
    {
        Assert.Same(ProcessPanelSpec.Empty, ProcessPanelSpec.Current);
    }
}
