using APS.SystemControl.Core.Classes;
using Xunit;

namespace CoreLogicTests;

public class SourceManagerPsuMapTests
{
    private static void SetSource(SourceManager sm, int flange, string psu, string instanceId)
    {
        var s = sm.GetSource(flange)!;
        s.PowerSupplyId = psu;
        s.MagnetronInstanceId = instanceId;
    }

    [Fact]
    public void BuildPsuToMagnetronLabel_MapsPsuToLabel_AndExcludesAmbiguousAndIncomplete()
    {
        var sm = new SourceManager(4);
        sm.MagnetronInstances =
        [
            new MagnetronInstance("g1", "Mag1", null, "MAG-4", null, null, null, "Active"),
            new MagnetronInstance("g2", "Mag2", null, "MAG4",  null, null, null, "Active"),
        ];
        SetSource(sm, 1, "PSU-A", "g1");   // -> Mag1
        SetSource(sm, 2, "RF301",   "g2");   // -> Mag2 ...
        SetSource(sm, 3, "RF301",   "g1");   // ... but RF301 now on 2 sources -> ambiguous, dropped
        SetSource(sm, 4, "RF601",   "");     // incomplete (no instance) -> skipped

        var map = sm.BuildPsuToMagnetronLabel();

        Assert.Equal("Mag1", map["PSU-A"]);
        Assert.False(map.ContainsKey("RF301"));
        Assert.False(map.ContainsKey("RF601"));
        Assert.Single(map);
    }
}
