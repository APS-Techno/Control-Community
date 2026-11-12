using APS.ProcessPanel.Contracts;
using APS.SystemControl.Core.Production;
using Xunit;

namespace CoreLogicTests;

public class PsuMagnetronMapTests
{
    private static FlangeSourceInfo F(string? psu, string? mag) =>
        new("mat", psu, "DC", mag);

    [Fact]
    public void Build_maps_each_psu_to_its_unique_magnetron()
    {
        var flangeMap = new Dictionary<int, FlangeSourceInfo>
        {
            [2] = F("PSU-A", "MAG-SI"),
            [3] = F("RF301", "MAG-BN"),
        };
        var map = PsuMagnetronMap.Build(flangeMap);
        Assert.Equal("MAG-SI", map["PSU-A"]);
        Assert.Equal("MAG-BN", map["RF301"]);
    }

    [Fact]
    public void Ambiguous_shared_psu_is_excluded_not_misattributed()
    {
        // PSU-A wired to two cathodes (a hazardous wiring) -> can't know which is live.
        var flangeMap = new Dictionary<int, FlangeSourceInfo>
        {
            [2] = F("PSU-A", "MAG-SI"),
            [5] = F("PSU-A", "MAG-W"),
        };
        var map = PsuMagnetronMap.Build(flangeMap);
        Assert.False(map.ContainsKey("PSU-A"));   // excluded -> erosion attributes to neither (safe)
    }

    [Fact]
    public void Flanges_without_psu_or_magnetron_are_ignored()
    {
        var flangeMap = new Dictionary<int, FlangeSourceInfo>
        {
            [1] = F(null, null),
            [2] = F("PSU-A", null),   // psu but no magnetron mounted
            [4] = F(null, "MAG-ION"),   // magnetron but no psu (e.g. ion source)
        };
        var map = PsuMagnetronMap.Build(flangeMap);
        Assert.Empty(map);
    }
}
