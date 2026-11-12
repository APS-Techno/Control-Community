using System.Text.Json;
using APS.SystemControl.Core.Remote.Agent;
using Xunit;

namespace CoreLogicTests;

public class AgentProtocolTests
{
    [Fact]
    public void Parses_request_with_args()
    {
        bool ok = AgentProtocol.TryParseRequest(
            """{"v":1,"id":7,"cmd":"recipe.start","args":{"name":"Cleaning"}}""", out var req);
        Assert.True(ok);
        Assert.Equal(7, req.Id);
        Assert.Equal("recipe.start", req.Cmd);
        Assert.Equal("Cleaning", req.Args!.Value.GetProperty("name").GetString());
    }

    [Fact]
    public void Rejects_blank_and_malformed_lines()
    {
        Assert.False(AgentProtocol.TryParseRequest("", out _));
        Assert.False(AgentProtocol.TryParseRequest("not json", out _));
        Assert.False(AgentProtocol.TryParseRequest("""{"v":1,"id":1}""", out _)); // no cmd
    }

    [Fact]
    public void FormatOk_and_FormatError_are_single_line_camel_case()
    {
        string ok = AgentProtocol.FormatOk(3, new { profileName = "DemoProfile" });
        Assert.DoesNotContain('\n', ok);
        var doc = JsonDocument.Parse(ok);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("DemoProfile", doc.RootElement.GetProperty("data").GetProperty("profileName").GetString());

        string err = AgentProtocol.FormatError(4, AgentErrors.TierDenied, "nope");
        var edoc = JsonDocument.Parse(err);
        Assert.False(edoc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("tier_denied", edoc.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("nope", edoc.RootElement.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public void Actor_is_optional_and_sanitised()
    {
        Assert.True(AgentProtocol.TryParseRequest("""{"v":1,"id":1,"cmd":"recipe.abort"}""", out var none));
        Assert.Null(none.Actor);
        Assert.True(AgentProtocol.TryParseRequest("""{"v":1,"id":1,"cmd":"recipe.abort","actor":"  alice\u0007\n "}""", out var some));
        Assert.Equal("alice", some.Actor);
        Assert.True(AgentProtocol.TryParseRequest("""{"v":1,"id":1,"cmd":"recipe.abort","actor":"   "}""", out var blank));
        Assert.Null(blank.Actor);
        string longName = new string('x', 100);
        Assert.True(AgentProtocol.TryParseRequest($$"""{"v":1,"id":1,"cmd":"recipe.abort","actor":"{{longName}}"}""", out var trunc));
        Assert.Equal(64, trunc.Actor!.Length);
    }
}
