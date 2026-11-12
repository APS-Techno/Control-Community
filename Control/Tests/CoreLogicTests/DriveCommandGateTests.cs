using APS.SystemControl.Core;
using Xunit;

namespace CoreLogicTests;

/// <summary>
/// SetOffset / SetReverseOffset are pure in-memory writes that must succeed on
/// disconnected drives; motion commands stay behind ProcessDriveCommand's
/// Connected gate.
/// </summary>
public class DriveCommandGateTests
{
    [Theory]
    [InlineData("SetOffset")]
    [InlineData("SetReverseOffset")]
    public void MetadataCommands_AreRecognized(string command)
    {
        Assert.True(CoreThread.IsDriveMetadataCommand(command));
    }

    [Theory]
    [InlineData("MoveTo")]
    [InlineData("MoveToFlange")]
    [InlineData("Home")]
    [InlineData("Scan")]
    [InlineData("ScanReciprocating")]
    [InlineData("Stop")]
    [InlineData("")]
    [InlineData("setoffset")] // case-sensitive — pattern uses ordinal equality
    public void MotionAndUnknownCommands_AreNotMetadata(string command)
    {
        Assert.False(CoreThread.IsDriveMetadataCommand(command));
    }

    [Fact]
    public void JogTo_is_motion_not_metadata()
    {
        Assert.False(CoreThread.IsDriveMetadataCommand("JogTo"));
    }

    [Fact]
    public void JogTo_is_gated_on_recipe_state()
    {
        Assert.True(CoreThread.IsRecipeGatedDriveCommand("JogTo"));
    }

    [Theory]
    [InlineData("Home")]
    [InlineData("MoveTo")]
    [InlineData("MoveToFlange")]
    [InlineData("Scan")]
    [InlineData("Stop")]
    [InlineData("SetOffset")]
    [InlineData("jogto")] // case-sensitive, like IsDriveMetadataCommand
    public void Other_drive_commands_are_not_recipe_gated(string command)
    {
        Assert.False(CoreThread.IsRecipeGatedDriveCommand(command));
    }
}
