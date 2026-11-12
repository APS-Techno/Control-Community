using System.Linq;
using APS.SystemControl.Core.Remote.Agent;
using Xunit;

namespace CoreLogicTests;

/// <summary>
/// The DSL cheat sheet served by describe.dsl is hand-maintained, so it can
/// drift from the validator.
/// </summary>
public class DslReferenceTests
{
    [Fact]
    public void Shutter_takes_one_position_argument_like_the_validator()
    {
        Assert.Contains("shutter <pos>", DslReference.Text);
        Assert.DoesNotContain("shutter <source>", DslReference.Text);
        Assert.DoesNotContain("open|close", DslReference.Text.Split('\n').Single(l => l.Contains("shutter <pos>")));
    }

    [Fact]
    public void Control_mode_requirement_is_stated_up_front()
    {
        Assert.Contains("Online-Remote", DslReference.Text);
        Assert.Contains("only an operator", DslReference.Text);
    }

    [Fact]
    public void Pwait_settle_rule_is_documented()
    {
        Assert.Contains("consecutive", DslReference.Text);
    }
}
