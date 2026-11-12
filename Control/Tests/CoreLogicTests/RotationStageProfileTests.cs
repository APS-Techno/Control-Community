using System.Xml.Linq;
using APS.SystemControl.Tests.Shared;
using Xunit;

namespace CoreLogicTests;

public class RotationStageProfileTests
{
    private static IEnumerable<XElement> RotationStages(string file) =>
        XDocument.Load(ConfigsDir.Path(file)).Descendants("stage").Where(s => s.Value.Trim() == "rotation");

    [Theory]
    [InlineData("Sputter/HWConf.xml")]
    [InlineData("Sputter/HWConf.Emulator.xml")]
    [InlineData("ADS/HWConf.xml")]
    [InlineData("ADS/HWConf.Emulator.xml")]
    public void Rotation_stage_is_declared_not_operational(string file)
    {
        var stage = Assert.Single(RotationStages(file));

        Assert.Equal("false", (string?)stage.Attribute("operational"));
    }

    [Theory]
    [InlineData("Cluster/HWConf.xml")]
    public void Profiles_without_a_rotation_stage_declare_none(string file)
    {
        Assert.Empty(RotationStages(file));
    }

    [Theory]
    [InlineData("Sputter/HWConf.xml")]
    [InlineData("ADS/HWConf.xml")]
    public void Rotation_stage_positional_attributes_survive_the_operational_filter(string file)
    {
        var stage = Assert.Single(RotationStages(file));

        var args = APS.SystemControl.XML.XMLHardware.Attributes(stage);

        Assert.Equal(new object[] { "rotation", 1974, 1978, 30, 0, "Substrate Rotation" }, args);
    }
}
