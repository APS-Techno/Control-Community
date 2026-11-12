using System.Xml.Linq;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core;
using APS.SystemControl.Core.HWClasses.Base;
using Xunit;

namespace CoreLogicTests;

public class RobotFaceAngleTests
{
    private sealed class FakeRobot : BaseRobot
    {
        public FakeRobot() : base(new BaseDeviceParams("ROBOT", 0, "Fake robot")) { }
        public override void Poll() { }
        public override void Transfer(byte s, byte d) { }
        public override void Home() { }
        public override void Abort() { }
    }

    [Fact]
    public void ApplyRobotFaces_parses_angle_attribute()
    {
        var xml = XElement.Parse(
            "<robot><face name=\"LL\" idx=\"1\" slit=\"SV_LL\" angle=\"0\"/>" +
            "<face name=\"C1\" idx=\"2\" slit=\"SV_C1\" angle=\"60\"/></robot>");
        var robot = new FakeRobot();

        Core.ApplyRobotFaces(robot, xml);

        Assert.Equal(0,  robot.FaceAngleMap["LL"]);
        Assert.Equal(60, robot.FaceAngleMap["C1"]);
    }

    [Fact]
    public void ApplyRobotFaces_omits_face_without_angle()
    {
        var xml = XElement.Parse("<robot><face name=\"LL\" idx=\"1\"/></robot>");
        var robot = new FakeRobot();

        Core.ApplyRobotFaces(robot, xml);

        Assert.False(robot.FaceAngleMap.ContainsKey("LL"));
    }
}
