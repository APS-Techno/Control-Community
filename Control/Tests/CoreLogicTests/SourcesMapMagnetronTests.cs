using APS.SystemControl.Core.Classes;
using Xunit;

namespace CoreLogicTests;

public class SourcesMapMagnetronTests
{
    [Fact]
    public void Flange_map_exposes_magnetron_instance_id()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"cfg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var magId = Guid.NewGuid().ToString();
            File.WriteAllText(Path.Combine(dir, "Sources.xml"), $@"<?xml version=""1.0""?>
<sources>
  <source flange=""2"">
    <material>Si</material>
    <magnetronId>MAG-4</magnetronId>
    <magnetronInstanceId>{magId}</magnetronInstanceId>
    <powerSupplyId>PSU-A</powerSupplyId>
  </source>
</sources>");

            var map = SourcesMapLoader.LoadFlangeSourceMap(dir);

            Assert.True(map.ContainsKey(2));
            Assert.Equal(magId, map[2].MagnetronInstanceId);
            Assert.Equal("PSU-A", map[2].PowerSupplyId);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
