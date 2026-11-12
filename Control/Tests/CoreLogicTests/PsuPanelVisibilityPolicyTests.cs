using APS.ProcessPanel.Contracts;
using APS.SystemControl.Core.Classes;
using Xunit;

namespace CoreLogicTests;

public class PsuPanelVisibilityPolicyTests
{
    private static FlangeSourceInfo F(string mat, string? psu, string? mag = null, string? inst = null)
        => new(mat, psu, "DC", inst, mag);

    private static readonly string[] Sources = { "PSU-A", "PSU-B", "PSU-C", "RF301" };

    [Fact]
    public void Psu_on_magnetron_flange_is_visible()
    {
        var map = new Dictionary<int, FlangeSourceInfo> { [2] = F("Si", "PSU-A", mag: "MAG-4") };
        var vis = PsuPanelVisibilityPolicy.Compute(map, Sources);
        Assert.True(vis["PSU-A"]);
        Assert.False(vis["PSU-B"]);   // unreferenced
    }

    [Fact]
    public void Material_only_flange_keeps_psu_visible()
    {
        var map = new Dictionary<int, FlangeSourceInfo> { [2] = F("Si", "PSU-A") };  // no magnetron
        Assert.True(PsuPanelVisibilityPolicy.Compute(map, Sources)["PSU-A"]);
    }

    [Fact]
    public void Magnetron_only_flange_keeps_psu_visible()
    {
        var map = new Dictionary<int, FlangeSourceInfo> { [2] = F("", "RF301", mag: "MAG4") };
        Assert.True(PsuPanelVisibilityPolicy.Compute(map, Sources)["RF301"]);
    }

    [Fact]
    public void MagnetronInstance_only_flange_keeps_psu_visible()
    {
        // Only MagnetronInstanceId set (no material, no model) -> active source.
        var map = new Dictionary<int, FlangeSourceInfo> { [2] = F("", "RF301", inst: "INST1") };
        Assert.True(PsuPanelVisibilityPolicy.Compute(map, Sources)["RF301"]);
    }

    [Fact]
    public void Psu_wired_only_to_inactive_flange_is_hidden()
    {
        // flange has the supply but no material and no magnetron -> not an active source
        var map = new Dictionary<int, FlangeSourceInfo> { [2] = F("", "PSU-A") };
        Assert.False(PsuPanelVisibilityPolicy.Compute(map, Sources)["PSU-A"]);
    }

    [Fact]
    public void Shared_supply_stays_visible()
    {
        var map = new Dictionary<int, FlangeSourceInfo>
        {
            [2] = F("Si", "PSU-A", mag: "MAG-4"),
            [5] = F("W",  "PSU-A", mag: "MAG-4"),
        };
        Assert.True(PsuPanelVisibilityPolicy.Compute(map, Sources)["PSU-A"]);
    }

    [Fact]
    public void Bias_supply_not_in_sources_is_omitted()
    {
        var map = new Dictionary<int, FlangeSourceInfo> { [2] = F("Si", "PSU-A", mag: "MAG-4") };
        var vis = PsuPanelVisibilityPolicy.Compute(map, Sources);
        Assert.False(vis.ContainsKey("BIAS-1"));   // caller leaves it visible
    }

    [Fact]
    public void Psu_match_is_case_insensitive()
    {
        var map = new Dictionary<int, FlangeSourceInfo> { [2] = F("Si", "psu-a", mag: "MAG-4") };
        Assert.True(PsuPanelVisibilityPolicy.Compute(map, Sources)["PSU-A"]);
    }

    [Fact]
    public void Empty_flange_map_hides_all_sources()
    {
        // No flanges loaded -> nothing is wired -> every magnetron-source PSU is hidden.
        // (Operators recover via the master "Hide Inactive Supplies" toggle.)
        var vis = PsuPanelVisibilityPolicy.Compute(
            new Dictionary<int, FlangeSourceInfo>(), Sources);
        Assert.Equal(Sources.Length, vis.Count);
        Assert.All(vis.Values, v => Assert.False(v));
    }
}
