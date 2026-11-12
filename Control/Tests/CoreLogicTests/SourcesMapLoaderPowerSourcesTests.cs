using APS.SystemControl.Core.Classes;
using Xunit;

namespace CoreLogicTests;

public class SourcesMapLoaderPowerSourcesTests : IDisposable
{
    private readonly string _dir;

    public SourcesMapLoaderPowerSourcesTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "aps_srcload_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void WriteSources(string body) =>
        File.WriteAllText(Path.Combine(_dir, "Sources.xml"),
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<sources>" + body + "</sources>");

    [Fact]
    public void LoadPowerSources_parses_trims_and_drops_empties()
    {
        WriteSources("<powersources>" +
            "<powersource>PSU-A</powersource>" +
            "<powersource>  RF301  </powersource>" +
            "<powersource></powersource>" +
            "</powersources>");

        var list = SourcesMapLoader.LoadPowerSources(_dir);

        Assert.Equal(new[] { "PSU-A", "RF301" }, list);
    }

    [Fact]
    public void LoadPowerSources_missing_file_or_element_returns_empty()
    {
        Assert.Empty(SourcesMapLoader.LoadPowerSources(_dir));          // no file
        WriteSources("<magnetrons/>");                                 // file, no <powersources>
        Assert.Empty(SourcesMapLoader.LoadPowerSources(_dir));
    }

    [Fact]
    public void LoadFlangeSourceMap_populates_MagnetronId_model()
    {
        WriteSources("<source flange=\"2\">" +
            "<material>Si</material><magnetronId>MAG-4</magnetronId>" +
            "<powerSupplyId>PSU-A</powerSupplyId></source>");

        var map = SourcesMapLoader.LoadFlangeSourceMap(_dir);

        Assert.Equal("MAG-4", map[2].MagnetronId);
    }
}
