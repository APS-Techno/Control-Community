using APS.SystemControl.Core.Classes;
using APS.SystemControl.Hosting;

namespace CoreLogicTests;

public class PowerSupplyClassifierTests
{
    [Theory]
    [InlineData("DC-AA",   "DC")]
    [InlineData("DC-A",    "DC")]
    [InlineData("RF301",    "RF")]
    [InlineData("RF601",    "RF")]
    [InlineData("IONPSU",   "Ion source")]
    public void Classify_KnownPrefixesAndExactMatches_ReturnsExpectedType(string id, string expected)
    {
        Assert.Equal(expected, PowerSupplyClassifier.Classify(id));
    }

    [Theory]
    [InlineData("Beam")]
    [InlineData("XYZ_Supply")]
    [InlineData("")]
    public void Classify_UnknownOrEmpty_ReturnsNull(string id)
    {
        Assert.Null(PowerSupplyClassifier.Classify(id));
    }

    [Fact]
    public void Classify_Null_ReturnsNull()
    {
        Assert.Null(PowerSupplyClassifier.Classify(null!));
    }

    [Fact]
    public void Classify_PrefixMatchIsCaseInsensitive()
    {
        Assert.Equal("RF",        PowerSupplyClassifier.Classify("rf301"));
        Assert.Equal("DC",        PowerSupplyClassifier.Classify("dc-a"));
    }

    private sealed class FakeTypeSource(string knownId, string label) : IPowerSupplyTypeSource
    {
        public string? Classify(string id) =>
            string.Equals(id, knownId, StringComparison.OrdinalIgnoreCase) ? label : null;
    }

    [Fact]
    public void PowerSupplyClassifier_extensionKnowsId_returnsExtensionLabel()
    {
        var source = new FakeTypeSource("PSU-X", "Pulsed");

        Assert.Equal("Pulsed", PowerSupplyClassifier.Classify("psu-x", source));
    }

    [Fact]
    public void PowerSupplyClassifier_extensionDoesNotKnowId_fallsBackToPrefixRules()
    {
        var source = new FakeTypeSource("PSU-X", "Pulsed");

        Assert.Equal("RF", PowerSupplyClassifier.Classify("RF301", source));
        Assert.Null(PowerSupplyClassifier.Classify("Beam", source));
    }

    [Fact]
    public void PowerSupplyClassifier_noExtension_unprefixedId_returnsNull()
    {
        Assert.Null(PowerSupplyClassifier.Classify("PSU-X", null));
    }
}
