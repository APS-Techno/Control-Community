using APS.SystemControl.Core.Remote.Agent;
using Xunit;

namespace CoreLogicTests;

/// <summary>
/// Mapping rules behind the status-strip MCP indicator. The colour band tracks the
/// AUTHORITY currently held by a connected agent, not link health: gray = nobody can
/// touch anything, green = read-only telemetry, orange = can run recipes, red = can
/// command devices directly.
/// </summary>
public class AgentStatusViewTests
{
    private const string Endpoint = "127.0.0.1:8026";

    private static AgentSession S(string name, AgentTier tier) => new(name, tier);

    [Fact]
    public void Not_listening_reports_off_with_the_reason_in_the_tooltip()
    {
        var v = AgentStatusView.Describe(
            listening: false,
            offReason: "license lacks 'agent-mcp'",
            endpoint: Endpoint,
            sessions: []);

        Assert.Equal(AgentStatusLevel.Off, v.Level);
        Assert.Equal("MCP OFF", v.Text);
        Assert.Contains("license lacks 'agent-mcp'", v.Tooltip);
    }

    [Fact]
    public void Not_listening_without_a_reason_still_has_a_usable_tooltip()
    {
        var v = AgentStatusView.Describe(false, null, Endpoint, []);

        Assert.Equal(AgentStatusLevel.Off, v.Level);
        Assert.False(string.IsNullOrWhiteSpace(v.Tooltip));
    }

    [Fact]
    public void Listening_with_no_client_is_idle_and_still_gray()
    {
        var v = AgentStatusView.Describe(true, null, Endpoint, []);

        Assert.Equal(AgentStatusLevel.Off, v.Level);
        Assert.Equal("MCP IDLE", v.Text);
        Assert.Contains(Endpoint, v.Tooltip);
    }

    // One Fact per tier rather than a [Theory]: AgentStatusLevel is internal (reached via
    // InternalsVisibleTo), so it cannot appear in a public test method's signature.
    [Fact]
    public void A_read_session_is_green()
    {
        var v = AgentStatusView.Describe(true, null, Endpoint, [S("claude-dev", AgentTier.Read)]);

        Assert.Equal(AgentStatusLevel.Read, v.Level);
        Assert.Equal("MCP READ", v.Text);
        Assert.Contains("claude-dev", v.Tooltip);
    }

    [Fact]
    public void A_recipe_session_is_orange()
    {
        var v = AgentStatusView.Describe(true, null, Endpoint, [S("claude-dev", AgentTier.Recipe)]);

        Assert.Equal(AgentStatusLevel.Recipe, v.Level);
        Assert.Equal("MCP RECIPE", v.Text);
    }

    [Fact]
    public void A_manual_session_is_red()
    {
        var v = AgentStatusView.Describe(true, null, Endpoint, [S("claude-dev", AgentTier.Manual)]);

        Assert.Equal(AgentStatusLevel.Manual, v.Level);
        Assert.Equal("MCP MANUAL", v.Text);
    }

    [Fact]
    public void Several_sessions_report_the_highest_tier_present()
    {
        var v = AgentStatusView.Describe(true, null, Endpoint,
            [S("reader", AgentTier.Read), S("operator", AgentTier.Manual), S("runner", AgentTier.Recipe)]);

        Assert.Equal(AgentStatusLevel.Manual, v.Level);
        Assert.StartsWith("MCP MANUAL", v.Text);
    }

    [Fact]
    public void Several_sessions_show_the_client_count()
    {
        var v = AgentStatusView.Describe(true, null, Endpoint,
            [S("a", AgentTier.Read), S("b", AgentTier.Read)]);

        Assert.Equal("MCP READ (2)", v.Text);
    }

    [Fact]
    public void One_session_shows_no_count_suffix()
    {
        var v = AgentStatusView.Describe(true, null, Endpoint, [S("a", AgentTier.Read)]);

        Assert.Equal("MCP READ", v.Text);
    }

    [Fact]
    public void Tooltip_lists_every_connected_key_with_its_tier()
    {
        var v = AgentStatusView.Describe(true, null, Endpoint,
            [S("reader", AgentTier.Read), S("operator", AgentTier.Manual)]);

        Assert.Contains("reader", v.Tooltip);
        Assert.Contains("read", v.Tooltip);
        Assert.Contains("operator", v.Tooltip);
        Assert.Contains("manual", v.Tooltip);
    }

    [Fact]
    public void A_None_tier_session_cannot_light_the_indicator()
    {
        // The server never registers an unauthenticated client, but a None tier must
        // fail closed to gray rather than being promoted into a colour band.
        var v = AgentStatusView.Describe(true, null, Endpoint, [S("ghost", AgentTier.None)]);

        Assert.Equal(AgentStatusLevel.Off, v.Level);
        Assert.Equal("MCP IDLE", v.Text);
    }

    [Fact]
    public void Sessions_are_ignored_while_the_server_is_not_listening()
    {
        // Stale sessions must never outlive the listener in the display.
        var v = AgentStatusView.Describe(false, "disabled in AgentServer.xml", Endpoint,
            [S("claude-dev", AgentTier.Manual)]);

        Assert.Equal(AgentStatusLevel.Off, v.Level);
        Assert.Equal("MCP OFF", v.Text);
    }
}
