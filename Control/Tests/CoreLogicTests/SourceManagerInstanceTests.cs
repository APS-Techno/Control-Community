using APS.SystemControl.Core.Classes;
using Xunit;

namespace CoreLogicTests;

public class SourceManagerInstanceTests
{
    [Fact]
    public void Save_then_load_round_trips_formats_instances_and_flange_ref()
    {
        string path = Path.Combine(Path.GetTempPath(), $"sm-{Guid.NewGuid():N}.xml");
        try
        {
            var magId = Guid.NewGuid().ToString();
            var sm = new SourceManager(6);
            sm.Magnetrons = new() { new MagnetronDef("MAG-4", "MAG 4\" w Chimney") };
            sm.PowerSources = new() { "PSU-A" };
            sm.Formats = new() { new SourceFormat("4in-round", "4\" round") };
            sm.MagnetronInstances = new()
            {
                new MagnetronInstance(magId, "MAG-4 #1", "SN-1", "MAG-4", "4in-round", "{\"a\":1}", "spare", "Active")
            };
            var s2 = sm.GetSource(2)!;
            s2.Material = "Si"; s2.MagnetronId = "MAG-4"; s2.MagnetronInstanceId = magId; s2.PowerSupplyId = "PSU-A";
            sm.Save(path);

            var loaded = new SourceManager(6);
            loaded.Load(path);

            Assert.Equal("4\" round", loaded.Formats.Single().Label);
            Assert.Equal("MAG-4 #1", loaded.MagnetronInstances.Single().Label);
            Assert.Equal("{\"a\":1}", loaded.MagnetronInstances.Single().PhysicsJson);
            Assert.Equal(magId, loaded.GetSource(2)!.MagnetronInstanceId);
            Assert.Equal("MAG-4", loaded.GetSource(2)!.MagnetronId);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
