using APS.SystemControl.Core.Classes;
using Xunit;

namespace CoreLogicTests;

public class SourceGroupingTests
{
    private static Dictionary<int, Source> Make(params (int Flange, string? Chamber, string Material)[] xs)
        => Make(xs.Select(x => (x.Flange, x.Chamber, "", x.Material)).ToArray());

    private static Dictionary<int, Source> Make(
        params (int Flange, string? Chamber, string Name, string Material)[] xs)
    {
        var d = new Dictionary<int, Source>();
        foreach (var x in xs)
            d[x.Flange] = new Source
            {
                FlangeNumber = x.Flange, Chamber = x.Chamber, Name = x.Name, Material = x.Material,
            };
        return d;
    }

    [Fact]
    public void No_chamber_yields_one_flat_group_in_flange_order()
    {
        var groups = SourceGrouping.GroupByChamber(Make((2, null, "Si"), (1, null, ""), (3, null, "BN")));
        var g = Assert.Single(groups);
        Assert.Null(g.Chamber);
        Assert.Equal(new[] { 1, 2, 3 }, g.Sources.Select(s => s.Flange).ToArray());
        Assert.Equal("FL 1 — (empty)", g.Sources[0].Label);
        Assert.Equal("FL 2 — Si", g.Sources[1].Label);
    }

    [Fact]
    public void Chambers_group_in_first_seen_order_each_sorted_by_flange()
    {
        // Cluster shape: C1 has flanges 1 & 5, C2 flange 2, C3 flanges 3 & 6.
        var groups = SourceGrouping.GroupByChamber(Make(
            (1, "C1", "Cu"), (5, "C1", "Ti"), (2, "C2", "Ar"), (3, "C3", "Si"), (6, "C3", "B")));
        Assert.Equal(new[] { "C1", "C2", "C3" }, groups.Select(g => g.Chamber).ToArray());
        Assert.Equal(new[] { 1, 5 }, groups[0].Sources.Select(s => s.Flange).ToArray());
        Assert.Equal(new[] { 3, 6 }, groups[2].Sources.Select(s => s.Flange).ToArray());
    }

    [Fact]
    public void Blank_chamber_in_chambered_set_goes_to_unassigned_group()
    {
        var groups = SourceGrouping.GroupByChamber(Make((1, "C1", "Cu"), (2, null, "Ar")));
        Assert.Equal(new[] { "C1", "(unassigned)" }, groups.Select(g => g.Chamber).ToArray());
    }

    [Fact]
    public void Node_label_prefers_source_name_over_material()
    {
        // The tree labels a source with the handle recipes address: its name when it
        // has one, the material only as a fallback.
        var groups = SourceGrouping.GroupByChamber(Make(
            (1, null, "Cu_top", "Cu"),      // named  -> name
            (2, null, "   ",    "W"),       // blank  -> material
            (3, null, "",       "")));      // empty  -> "(empty)"

        var g = Assert.Single(groups);
        Assert.Equal("FL 1 — Cu_top", g.Sources[0].Label);
        Assert.Equal("FL 2 — W",      g.Sources[1].Label);
        Assert.Equal("FL 3 — (empty)", g.Sources[2].Label);
    }
}
