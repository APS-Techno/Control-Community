using System.Text;
using APS.Emulator.Devices.Demo;
using EmulatorTests.Helpers;

namespace EmulatorTests;

public class LanRotaryDriveEmulatorTests
{
    private static string Send(LanRotaryDriveEmulator drive, string line)
        => Encoding.ASCII.GetString(drive.HandleCommand(Encoding.ASCII.GetBytes(line + "\n")));

    [Fact]
    public void Idn_ReturnsProductId()
        => Assert.Equal("APS,LANDRIVE-100,0,1.0\n", Send(new LanRotaryDriveEmulator("Holder", ""), "*IDN?"));

    [Fact]
    public void Pos_Flange_GoesBusyThenSettles()
    {
        var drive = new LanRotaryDriveEmulator("Holder", "");
        var state = TestHelpers.CreateTestState();
        Assert.Equal("", Send(drive, "POS 3"));
        Assert.Equal("1\n", Send(drive, "BUSY?"));
        drive.Update(1f, state);
        Assert.Equal("1\n", Send(drive, "BUSY?"));
        drive.Update(10f, state);
        Assert.Equal("0\n", Send(drive, "BUSY?"));
        Assert.Equal("3\n", Send(drive, "POS?"));
        Assert.Equal("0,\"No error\"\n", Send(drive, "SYST:ERR?"));
    }

    [Fact]
    public void ShutterPosition_IsReportedInMotorSteps()
    {
        var drive = new LanRotaryDriveEmulator("Shutter", "");
        var state = TestHelpers.CreateTestState();
        Assert.True(MathF.Abs(drive.Position) <= 50f);
        Send(drive, "POS 3");
        drive.Update(20f, state);
        Assert.True(drive.Position > 50f);
    }

    [Fact]
    public void Pos_False_Queues222()
    {
        var drive = new LanRotaryDriveEmulator("Holder", "");
        Assert.Equal("", Send(drive, "POS false"));
        Assert.Equal("-222,\"Invalid position\"\n", Send(drive, "SYST:ERR?"));
        Assert.Equal("0\n", Send(drive, "BUSY?"));
        Assert.Equal("Home\n", Send(drive, "POS?"));
    }

    [Fact]
    public void Pos_Nine_Queues222()
    {
        var drive = new LanRotaryDriveEmulator("Holder", "");
        Assert.Equal("", Send(drive, "POS 9"));
        Assert.Equal("-222,\"Invalid position\"\n", Send(drive, "SYST:ERR?"));
        Assert.Equal("0\n", Send(drive, "BUSY?"));
    }

    [Fact]
    public void Home_ReportsHome()
    {
        var drive = new LanRotaryDriveEmulator("Holder", "");
        var state = TestHelpers.CreateTestState();
        Send(drive, "POS 2");
        drive.Update(10f, state);
        Assert.Equal("2\n", Send(drive, "POS?"));
        Send(drive, "pos HOME");
        Assert.Equal("1\n", Send(drive, "BUSY?"));
        drive.Update(10f, state);
        Assert.Equal("0\n", Send(drive, "BUSY?"));
        Assert.Equal("Home\n", Send(drive, "POS?"));
    }
}
