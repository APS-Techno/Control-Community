using APS.SystemEmulator;

namespace EmulatorTests;

public class LogSourceFilterTests
{
    [Fact]
    public void Items_StartWithAll_ThenDistinctSourcePrefixesSorted()
    {
        var items = LogSourceFilter.Items(["Valve:V1", "Engine", "Valve:V2", "LanPsu:PSU1", "S7Server"]);

        Assert.Equal(["All", "Engine", "LanPsu", "S7Server", "Valve"], items);
    }

    [Fact]
    public void Items_MergesPrefixesCaseInsensitively()
    {
        var items = LogSourceFilter.Items(["Pump:SP1", "pump:SP2"]);

        Assert.Equal(["All", "Pump"], items);
    }

    [Fact]
    public void Items_WithNoEntries_IsJustAll()
        => Assert.Equal(["All"], LogSourceFilter.Items([]));

    [Theory]
    [InlineData("All", "Anything:1", true)]
    [InlineData("Valve", "Valve:V1", true)]
    [InlineData("valve", "Valve:V1", true)]
    [InlineData("Valve", "Pump:SP1", false)]
    public void Matches_FiltersBySourcePrefix(string filter, string source, bool expected)
        => Assert.Equal(expected, LogSourceFilter.Matches(filter, source));
}
