using System.Text;
using System.Xml.Linq;
using APS.Emulator.Devices.Demo;
using EmulatorCore.Interfaces;
using EmulatorTests.Helpers;

namespace EmulatorTests;

public class LanTransferRobotEmulatorTests
{
    private const string RobotXml =
        "<robot descr=\"Transfer robot\">\n ROBOT \n" +
        "<face name=\"LL\" idx=\"1\" slit=\"SV_LL\" angle=\"0\" />" +
        "<face name=\"C1\" idx=\"2\" slit=\"SV_C1\" angle=\"60\" />" +
        "<timing rotateMs=\"2000\" extendMs=\"1500\" liftMs=\"800\" valveWaitMs=\"3000\" /></robot>";

    private static LanTransferRobotEmulator Create()
    {
        var ctx = new DeviceCreateContext(XElement.Parse(RobotXml), "robots", "LanTransferRobot", "",
            RegistryTestHelpers.DefaultPhysics());
        return Assert.IsType<LanTransferRobotEmulator>(LanTransferRobotEmulator.Create(ctx));
    }

    private static string Send(LanTransferRobotEmulator robot, string line)
        => Encoding.ASCII.GetString(robot.HandleCommand(Encoding.ASCII.GetBytes(line + "\n")));

    [Fact]
    public void Create_ReadsTrimmedIdAndIdentity()
    {
        var robot = Create();
        Assert.Equal("ROBOT", robot.Id);
        Assert.Equal("APS,LANROBOT-100,0,1.0\n", Send(robot, "*IDN?"));
        Assert.Equal("IDLE\n", Send(robot, "STAT?"));
        Assert.Equal("LL\n", Send(robot, "LOC?"));
    }

    [Fact]
    public void Xfer_StaysTransferringForTimingSum()
    {
        var robot = Create();
        var state = TestHelpers.CreateTestState();
        Assert.Equal("", Send(robot, "XFER LL,C1"));
        Assert.Equal("TRANSFERRING\n", Send(robot, "STAT?"));
        robot.Update(4.299f, state);
        Assert.Equal("TRANSFERRING\n", Send(robot, "STAT?"));
        robot.Update(0.002f, state);
        Assert.Equal("IDLE\n", Send(robot, "STAT?"));
        Assert.Equal("C1\n", Send(robot, "LOC?"));
        Assert.Equal("0,\"No error\"\n", Send(robot, "SYST:ERR?"));
    }

    [Fact]
    public void Xfer_UnknownFace_ErrorState()
    {
        var robot = Create();
        Send(robot, "XFER LL,NOPE");
        Assert.Equal("ERROR\n", Send(robot, "STAT?"));
        Assert.Equal("-222,\"Data out of range\"\n", Send(robot, "SYST:ERR?"));
        Assert.Equal("LL\n", Send(robot, "LOC?"));
    }

    [Fact]
    public void Abor_DuringTransfer_ReturnsError()
    {
        var robot = Create();
        Send(robot, "XFER LL,C1");
        Send(robot, "ABOR");
        Assert.Equal("ERROR\n", Send(robot, "STAT?"));
        robot.Update(10f, TestHelpers.CreateTestState());
        Assert.Equal("ERROR\n", Send(robot, "STAT?"));
        Assert.Equal("LL\n", Send(robot, "LOC?"));
    }

    [Fact]
    public void Home_ReturnsToFirstFace()
    {
        var robot = Create();
        Send(robot, "XFER LL,C1");
        robot.Update(5f, TestHelpers.CreateTestState());
        Assert.Equal("C1\n", Send(robot, "LOC?"));
        Send(robot, "HOME");
        Assert.Equal("LL\n", Send(robot, "LOC?"));
        Assert.Equal("IDLE\n", Send(robot, "STAT?"));
    }

    [Fact]
    public void Home_RecoversFromError()
    {
        var robot = Create();
        Send(robot, "XFER LL,NOPE");
        Send(robot, "HOME");
        Assert.Equal("IDLE\n", Send(robot, "STAT?"));
    }

    [Fact]
    public void UnknownHeader_Queues113()
    {
        var robot = Create();
        Assert.Equal("", Send(robot, "FOO?"));
        Assert.Equal("-113,\"Undefined header\"\n", Send(robot, "SYST:ERR?"));
    }

    [Fact]
    public void Create_WithoutFaces_ReturnsNull()
    {
        var ctx = new DeviceCreateContext(XElement.Parse("<robot>ROBOT</robot>"), "robots", "LanTransferRobot", "",
            RegistryTestHelpers.DefaultPhysics());
        Assert.Null(LanTransferRobotEmulator.Create(ctx));
    }
}
