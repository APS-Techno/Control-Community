using APS.SystemControl.Core;
using APS.SystemControl.Core.Classes;

namespace CoreLogicTests;

public class ConditionParserTests
{
    [Theory]
    [InlineData("less", -1)]
    [InlineData("more",  1)]
    [InlineData("equal", 0)]
    [InlineData("LESS", -1)]
    [InlineData("More",  1)]
    [InlineData("EQUAL", 0)]
    public void ParseCondition_known_keywords(string keyword, int expected)
    {
        Assert.Equal(expected, ConditionParser.ParseCondition(keyword, "test"));
    }

    [Fact]
    public void ParseCondition_unknown_throws_with_context()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ConditionParser.ParseCondition("greater", "unit test"));
        Assert.Contains("unit test", ex.Message);
        Assert.Contains("greater", ex.Message);
    }

    [Fact]
    public void ParseCondition_empty_throws_ArgumentException()
    {
        // ArgumentException.ThrowIfNullOrEmpty throws ArgumentException for empty strings.
        Assert.Throws<ArgumentException>(() => ConditionParser.ParseCondition("", "ctx"));
    }

    [Fact]
    public void ParseCondition_null_throws_ArgumentNullException()
    {
        // ArgumentException.ThrowIfNullOrEmpty throws ArgumentNullException for null.
        Assert.Throws<ArgumentNullException>(() => ConditionParser.ParseCondition(null!, "ctx"));
    }

    [Theory]
    [InlineData("startup",    SystemState.Startup)]
    [InlineData("idle",       SystemState.Idle)]
    [InlineData("processing", SystemState.Processing)]
    [InlineData("stopped",    SystemState.Stopped)]
    [InlineData("Startup",    SystemState.Startup)]
    [InlineData("ProCessing", SystemState.Processing)]
    public void ParseSystemState_known(string input, SystemState expected)
    {
        Assert.Equal(expected, ConditionParser.ParseSystemState(input));
    }

    [Theory]
    [InlineData("running")]
    [InlineData("")]
    [InlineData("garbage")]
    public void ParseSystemState_unknown_returns_Unknown(string input)
    {
        Assert.Equal(SystemState.Unknown, ConditionParser.ParseSystemState(input));
    }

    [Fact]
    public void ParseSystemState_recognizes_maintenance()
    {
        Assert.Equal(SystemState.Maintenance, ConditionParser.ParseSystemState("maintenance"));
        Assert.Equal(SystemState.Maintenance, ConditionParser.ParseSystemState("Maintenance"));
        Assert.Equal(SystemState.Maintenance, ConditionParser.ParseSystemState("MAINTENANCE"));
    }

    [Theory]
    [InlineData("startup",     SystemState.Startup)]
    [InlineData("Maintenance", SystemState.Maintenance)]
    [InlineData("PROCESSING",  SystemState.Processing)]
    public void ParseSystemStateStrict_known(string input, SystemState expected)
    {
        Assert.Equal(expected, ConditionParser.ParseSystemStateStrict(input, "ctx"));
    }

    [Theory]
    [InlineData("procesing")]   // typo in a real Alarms.xml state name
    [InlineData("Idel")]
    [InlineData("running")]
    [InlineData("garbage")]
    public void ParseSystemStateStrict_unknown_throws(string input)
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ConditionParser.ParseSystemStateStrict(input, "Alarms.xml <states initial>"));
        Assert.Contains("Alarms.xml <states initial>", ex.Message);
        Assert.Contains(input, ex.Message);
    }

    [Theory]
    [InlineData(null)]   // ArgumentNullException (derived from ArgumentException)
    [InlineData("")]     // ArgumentException
    public void ParseSystemStateStrict_null_or_empty_throws(string? input)
    {
        Assert.ThrowsAny<ArgumentException>(
            () => ConditionParser.ParseSystemStateStrict(input!, "ctx"));
    }

    [Theory]
    [InlineData("gasvalve", GroupKind.GasValve)]
    [InlineData("GasValve", GroupKind.GasValve)]
    [InlineData("gas",      GroupKind.Gas)]
    [InlineData("GAS",      GroupKind.Gas)]
    internal void ParseGroupKind_accepts_known_values(string input, GroupKind expected)
        => Assert.Equal(expected, ConditionParser.ParseGroupKind(input, "test"));

    [Fact]
    public void ParseGroupKind_unknown_throws_with_context()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ConditionParser.ParseGroupKind("plasma", "Alarms.xml transition idle->processing"));
        Assert.Contains("plasma", ex.Message);
        Assert.Contains("transition idle->processing", ex.Message);
    }

    [Theory]
    [InlineData("anyOpen", GroupCondition.AnyOpen)]
    [InlineData("anyopen", GroupCondition.AnyOpen)]
    [InlineData("allOff",  GroupCondition.AllOff)]
    [InlineData("ALLOFF",  GroupCondition.AllOff)]
    internal void ParseGroupCondition_accepts_known_values(string input, GroupCondition expected)
        => Assert.Equal(expected, ConditionParser.ParseGroupCondition(input, "test"));

    [Fact]
    public void ParseGroupCondition_unknown_throws_with_context()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ConditionParser.ParseGroupCondition("sometimes", "ctx"));
        Assert.Contains("sometimes", ex.Message);
        Assert.Contains("ctx", ex.Message);
    }
}
