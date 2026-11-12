using APS.Mcp;
using APS.Wire;
using Xunit;

/// <summary>Covers the F10 fix: a dropped connection must not trigger a silent retry for a
/// mutating command, because the bridge cannot know whether the original request already
/// executed on the server before the response was lost. WireClient itself opens a real TLS
/// connection, so this exercises the retry *decision* directly via the extracted static
/// helper rather than driving a full connect/retry through a fake socket.</summary>
public class WireClientRetryTests
{
    [Theory]
    [InlineData("recipe.start")]
    [InlineData("recipe.upload")]
    [InlineData("recipe.abort")]
    [InlineData("recipe.resolve_pause")]
    [InlineData("device.command")]
    public void Mutating_commands_are_not_retryable(string cmd)
    {
        Assert.False(WireClient.IsRetryable(cmd));
    }

    [Theory]
    [InlineData("describe.machine")]
    [InlineData("status.system")]
    [InlineData("logs.tail")]
    [InlineData("recipe.validate")]
    [InlineData("recipe.status")]
    [InlineData("auth")]
    public void Read_and_auth_commands_are_retryable(string cmd)
    {
        Assert.True(WireClient.IsRetryable(cmd));
    }
}
