using System.IO;
using APS.SystemControl.Core.Classes;
using Xunit;

namespace CoreLogicTests;

/// <summary>
/// The <c>name</c> attribute on a &lt;source&gt; is the operator-facing recipe
/// handle. These tests pin that <see cref="SourcesMapLoader"/> reads it into the
/// panel contracts' <c>Effective</c> label (name when set, else material) so the
/// process/cluster panels show the handle the recipe addresses.
/// </summary>
public class SourcesMapLoaderNameTests : IDisposable
{
    private readonly string _dir;

    public SourcesMapLoaderNameTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"aps-srcmap-name-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    private void WriteSources(string body)
    {
        File.WriteAllText(Path.Combine(_dir, "Sources.xml"),
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<sources>\n" + body + "\n</sources>");
    }

    [Fact]
    public void FlangeMap_Effective_prefers_name_over_material()
    {
        WriteSources("""
              <source flange="1" name="C1_Cu"><material>Cu</material></source>
              <source flange="2"><material>W</material></source>
            """);

        var map = SourcesMapLoader.LoadFlangeSourceMap(_dir);

        Assert.Equal("C1_Cu", map[1].Name);
        Assert.Equal("C1_Cu", map[1].Effective);
        Assert.Equal("Cu",    map[1].Material);     // material preserved for keys/erosion

        Assert.Null(map[2].Name);
        Assert.Equal("W", map[2].Effective);        // unnamed → falls back to material
    }

    [Fact]
    public void ChamberSources_Effective_prefers_name_over_material()
    {
        WriteSources("""
              <source flange="1" chamber="C1" name="Left" shutterId="C1_Sh"><material>W</material></source>
              <source flange="2" chamber="C1"><material>Ti</material></source>
            """);

        var byChamber = SourcesMapLoader.LoadChamberSources(_dir);
        var c1 = byChamber["C1"];

        Assert.Equal("Left", c1[0].Name);
        Assert.Equal("Left", c1[0].Effective);
        Assert.Equal("W",    c1[0].Material);

        Assert.Equal("Ti", c1[1].Effective);        // unnamed → material
    }
}
