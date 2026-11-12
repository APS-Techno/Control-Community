using APS.ProcessPanel.Contracts;
using APS.SystemControl.Core.Classes;

namespace CoreLogicTests;

public class SourcesMapLoaderTests
{
    [Fact]
    public void LoadFlangeSourceMap_ReadsMaterialPowerIdAndPowerType()
    {
        string dir = Path.Combine(Path.GetTempPath(), "aps-sources-" + System.Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Sources.xml"), """
                <?xml version="1.0"?>
                <sources>
                  <source flange="1">
                    <material>W</material>
                    <powerSupplyId>DC-A</powerSupplyId>
                  </source>
                  <source flange="2">
                    <material>Si</material>
                    <powerSupplyId>IONPSU</powerSupplyId>
                  </source>
                  <source flange="3">
                    <material>BN</material>
                    <powerSupplyId>RF301</powerSupplyId>
                    <powerType>RF (matched)</powerType>
                  </source>
                </sources>
                """);

            var map = SourcesMapLoader.LoadFlangeSourceMap(dir);

            Assert.Equal(3, map.Count);
            Assert.Equal("W",       map[1].Material);
            Assert.Equal("DC-A", map[1].PowerSupplyId);
            Assert.Equal("DC",      map[1].PowerType);          // classifier fallback

            Assert.Equal("Si",      map[2].Material);
            Assert.Equal("IONPSU",  map[2].PowerSupplyId);
            Assert.Equal("Ion source", map[2].PowerType);       // classifier fallback

            Assert.Equal("BN",         map[3].Material);
            Assert.Equal("RF301",      map[3].PowerSupplyId);
            Assert.Equal("RF (matched)", map[3].PowerType);     // XML override
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void LoadFlangeSourceMap_MissingFile_ReturnsEmpty()
    {
        string dir = Path.Combine(Path.GetTempPath(), "aps-sources-missing-" + System.Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            var map = SourcesMapLoader.LoadFlangeSourceMap(dir);
            Assert.Empty(map);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void LoadFlangeSourceMap_SkipsEntriesWithNeitherMaterialNorSupply()
    {
        string dir = Path.Combine(Path.GetTempPath(), "aps-sources-partial-" + System.Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Sources.xml"), """
                <?xml version="1.0"?>
                <sources>
                  <source flange="1"><material>W</material></source>
                  <source flange="2"><material /><powerSupplyId /></source>
                  <source><material>Orphan</material></source>
                </sources>
                """);

            var map = SourcesMapLoader.LoadFlangeSourceMap(dir);

            Assert.Single(map);
            Assert.Equal("W",   map[1].Material);
            Assert.Null(map[1].PowerSupplyId);
            Assert.Null(map[1].PowerType);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void LoadFlangeSourceMap_KeepsEntryWithSupplyButNoMaterial()
    {
        string dir = Path.Combine(Path.GetTempPath(), "aps-sources-supply-only-" + System.Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Sources.xml"), """
                <?xml version="1.0"?>
                <sources>
                  <source flange="4">
                    <material />
                    <powerSupplyId>RF601</powerSupplyId>
                  </source>
                </sources>
                """);

            var map = SourcesMapLoader.LoadFlangeSourceMap(dir);

            Assert.Single(map);
            Assert.Equal("",      map[4].Material);
            Assert.Equal("RF601", map[4].PowerSupplyId);
            Assert.Equal("RF",    map[4].PowerType);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void LoadFlangeSourceMap_MalformedXml_ReturnsEmptyWithoutThrowing()
    {
        string dir = Path.Combine(Path.GetTempPath(), "aps-sources-bad-" + System.Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Sources.xml"), "<not valid xml <<<");

            var map = SourcesMapLoader.LoadFlangeSourceMap(dir);

            Assert.Empty(map);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void LoadChamberSources_GroupsByChamberAndOrdersByFlange()
    {
        string dir = Path.Combine(Path.GetTempPath(), "aps-chsources-" + System.Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            // C1 Ti (flange 5) listed BEFORE C1 Cu (flange 1) to prove flange ordering.
            File.WriteAllText(Path.Combine(dir, "Sources.xml"), """
                <?xml version="1.0"?>
                <sources>
                  <source flange="5" chamber="C1" shutterId="C1_Sh_B">
                    <material>Ti</material>
                    <powerSupplyId>C1_RF_B</powerSupplyId>
                  </source>
                  <source flange="1" chamber="C1" shutterId="C1_Sh">
                    <material>Cu</material>
                    <powerSupplyId>C1_RF</powerSupplyId>
                  </source>
                  <source flange="2" chamber="C2" shutterId="C2_Sh">
                    <material>W</material>
                    <powerSupplyId>C2_RF</powerSupplyId>
                  </source>
                </sources>
                """);

            var map = SourcesMapLoader.LoadChamberSources(dir);

            Assert.Equal(2, map.Count);
            Assert.Equal(new[] { "Cu", "Ti" }, map["C1"].Select(s => s.Material).ToArray());
            Assert.Equal("C1_Sh",  map["C1"][0].ShutterId);
            Assert.Equal("C1_RF",  map["C1"][0].PowerSupplyId);
            Assert.Equal("C1_Sh_B", map["C1"][1].ShutterId);
            Assert.True(map.ContainsKey("c2"));   // chamber lookup is case-insensitive
            Assert.Equal("W", map["c2"].Single().Material);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void LoadChamberSources_SkipsSourcesWithoutChamberOrContent()
    {
        string dir = Path.Combine(Path.GetTempPath(), "aps-chsources-skip-" + System.Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Sources.xml"), """
                <?xml version="1.0"?>
                <sources>
                  <source flange="1"><material>Orphan</material></source>
                  <source flange="2" chamber="C1"><material /><powerSupplyId /></source>
                  <source chamber="C1" shutterId="C1_Sh"><material>Cu</material></source>
                </sources>
                """);

            var map = SourcesMapLoader.LoadChamberSources(dir);

            // Orphan (no chamber) and the all-empty C1 entry are dropped; the
            // flange-less Cu entry survives (flange only affects ordering).
            Assert.Single(map);
            var c1 = Assert.Single(map["C1"]);
            Assert.Equal("Cu", c1.Material);
            Assert.Equal("C1_Sh", c1.ShutterId);
            Assert.Null(c1.PowerSupplyId);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void LoadChamberSources_MissingFile_ReturnsEmpty()
    {
        string dir = Path.Combine(Path.GetTempPath(), "aps-chsources-missing-" + System.Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try { Assert.Empty(SourcesMapLoader.LoadChamberSources(dir)); }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void LoadChamberSources_MalformedXml_ReturnsEmptyWithoutThrowing()
    {
        string dir = Path.Combine(Path.GetTempPath(), "aps-chsources-bad-" + System.Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Sources.xml"), "<not valid xml <<<");
            Assert.Empty(SourcesMapLoader.LoadChamberSources(dir));
        }
        finally { Directory.Delete(dir, true); }
    }
}
