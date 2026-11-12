using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.HWClasses.Base;
using Xunit;

namespace CoreLogicTests;

public class StepperDriveOffsetTests
{
    private sealed class CapturingDrive : BaseStepperDrive
    {
        public CapturingDrive() : base(new BaseDeviceParams("Holder", 0, "")) { }
    }

    [Fact]
    public void ProcessCommand_SetOffset_AssignsOffsetProperty()
    {
        var drive = new CapturingDrive();
        drive.ProcessCommand("SetOffset", new object[] { 42 });
        Assert.Equal(42, drive.Offset);
    }

    [Fact]
    public void ProcessCommand_SetReverseOffset_AssignsReverseOffsetProperty()
    {
        var drive = new CapturingDrive();
        drive.ProcessCommand("SetReverseOffset", new object[] { 7 });
        Assert.Equal(7, drive.ReverseOffset);
    }
}
