using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.Classes;
using APS.SystemControl.Core.HWClasses.Base;

namespace CoreLogicTests;

public class InterlockRobotTests
{
    [Theory]
    [InlineData(RobotState.Idle,         "transferring", "notIn",  false)]
    [InlineData(RobotState.Transferring, "transferring", "notIn",  true )]
    [InlineData(RobotState.Transferring, "transferring", "is",     false)]
    [InlineData(RobotState.Idle,         "transferring", "is",     true )]
    [InlineData(RobotState.Idle,         "Idle",         "is",     false)] // case-insensitive state name
    [InlineData(RobotState.Idle,         "idle",         "notIn",  true )]
    [InlineData(RobotState.Error,        "transferring", "notIn",  false)]
    public void RobotFails_StateOpMatrix(RobotState actual, string expected, string op, bool shouldFail)
    {
        bool fails = InterlockEvaluator.RobotFails(actual, expected, op);
        Assert.Equal(shouldFail, fails);
    }

    [Fact]
    public void RobotFails_UnparseableExpected_FailsClosed()
    {
        bool fails = InterlockEvaluator.RobotFails(RobotState.Idle, "ghost", "is");
        Assert.True(fails);
    }

    [Fact]
    public void RobotFails_UnknownOp_FailsClosed()
    {
        bool fails = InterlockEvaluator.RobotFails(RobotState.Idle, "transferring", "foo");
        Assert.True(fails);
    }

    [Fact]
    public void RobotFails_BothBad_FailsClosed()
    {
        bool fails = InterlockEvaluator.RobotFails(RobotState.Idle, "ghost", "bar");
        Assert.True(fails);
    }

    [Theory]
    [InlineData("notIn")]
    [InlineData("NOTIN")]
    [InlineData("NotIn")]
    public void RobotFails_NotInCaseForms_AllRecognised(string op)
    {
        // "notIn" is stored from XML as-is; tolerate any casing.
        bool fails = InterlockEvaluator.RobotFails(RobotState.Transferring, "transferring", op);
        Assert.True(fails);
    }

    [Theory]
    [InlineData("is")]
    [InlineData("IS")]
    [InlineData("Is")]
    public void RobotFails_IsCaseForms_AllRecognised(string op)
    {
        bool fails = InterlockEvaluator.RobotFails(RobotState.Idle, "idle", op);
        Assert.False(fails);
    }
}
