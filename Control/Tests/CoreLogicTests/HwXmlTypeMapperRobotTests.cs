using APS.Controls.Core;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.CoreClass;

namespace CoreLogicTests;

/// <summary>
/// Without <see cref="DeviceType.Robot"/> entries in <see cref="HwXmlTypeMapper"/> and
/// <see cref="HwElementCollection"/> the generic <c>LoadWHSection&lt;BaseRobot&gt;</c> path is unreachable.
/// </summary>
public class HwXmlTypeMapperRobotTests
{
    [Fact]
    public void HwXmlTypeMapper_HasRobotEntry()
    {
        var mapper = new HwXmlTypeMapper();
        Assert.True(mapper.Type.ContainsKey(DeviceType.Robot),
            "HwXmlTypeMapper.Type must contain DeviceType.Robot");
    }

    [Fact]
    public void HwXmlTypeMapper_RobotEntryMapsToBaseRobot()
    {
        var mapper = new HwXmlTypeMapper();
        Assert.Equal(typeof(BaseRobot), mapper.Type[DeviceType.Robot]);
    }

    [Fact]
    public void HwXmlTypeMapper_RobotSectionNameIsRobots()
    {
        Assert.Equal("robots", HwXmlTypeMapper.Name(DeviceType.Robot));
    }

    [Fact]
    public void HwElementCollection_HasRobotBucket()
    {
        var collection = new HwElementCollection();
        Assert.True(collection.Item.ContainsKey(DeviceType.Robot),
            "HwElementCollection.Item must have a DeviceType.Robot bucket");
    }

    [Fact]
    public void HwElementCollection_RobotBucketStartsEmpty()
    {
        var collection = new HwElementCollection();
        Assert.Empty(collection.Item[DeviceType.Robot]);
    }
}
