using APS.SystemControl.Core.Remote.Agent;
using Xunit;

namespace CoreLogicTests;

public class AgentServerConfigTests
{
    private static string WriteTemp(string xml)
    {
        string path = Path.Combine(Path.GetTempPath(), $"AgentServer_{Guid.NewGuid():N}.xml");
        File.WriteAllText(path, xml);
        return path;
    }

    private static AgentServerConfig LoadXml(string xml)
    {
        string path = WriteTemp(xml);
        try { return AgentServerConfig.Load(path); }
        finally { File.Delete(path); }
    }

    // SHA-256("secret") lowercase hex
    private const string SecretHash =
        "2bb80d537b1da3e38bd30361aa855686bde0eacd7162fef6a25fe97bf527a25b";

    [Fact]
    public void Missing_file_yields_disabled_config()
    {
        var cfg = AgentServerConfig.Load(Path.Combine(Path.GetTempPath(), $"none_{Guid.NewGuid():N}.xml"));
        Assert.False(cfg.Enabled);
        Assert.Empty(cfg.Keys);
    }

    [Fact]
    public void Load_parses_attributes_and_keys()
    {
        string path = WriteTemp(
            $"""<AgentServer enabled="true" port="9100" bind="0.0.0.0" allowRecipe="true" allowManual="true"><Key name="dev" hash="{SecretHash}" tier="Manual"/></AgentServer>""");
        try
        {
            var cfg = AgentServerConfig.Load(path);
            Assert.True(cfg.Enabled);
            Assert.Equal(9100, cfg.Port);
            Assert.Equal("0.0.0.0", cfg.Bind);
            var key = Assert.Single(cfg.Keys);
            Assert.Equal("dev", key.Name);
            Assert.Equal(AgentTier.Manual, key.Tier);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Authenticate_matches_key_by_sha256_and_returns_name()
    {
        var cfg = new AgentServerConfig
        {
            Enabled = true, AllowRecipe = true, AllowManual = true,
            Keys = [new AgentKey("dev", SecretHash, AgentTier.Manual)]
        };
        var granted = cfg.Authenticate("secret");
        Assert.NotNull(granted);
        Assert.Equal(AgentTier.Manual, granted.Value.Tier);
        Assert.Equal("dev", granted.Value.Name);
        Assert.Null(cfg.Authenticate("wrong"));
    }

    [Fact]
    public void Authenticate_clamps_tier_by_allow_flags()
    {
        var keys = new[] { new AgentKey("dev", SecretHash, AgentTier.Manual) };
        var noManual = new AgentServerConfig { Enabled = true, AllowRecipe = true, AllowManual = false, Keys = keys };
        Assert.Equal(AgentTier.Recipe, noManual.Authenticate("secret")!.Value.Tier);
        var readOnly = new AgentServerConfig { Enabled = true, AllowRecipe = false, AllowManual = false, Keys = keys };
        Assert.Equal(AgentTier.Read, readOnly.Authenticate("secret")!.Value.Tier);
    }

    // An unparseable or explicit "None" tier is a config error on an auth surface: the key is
    // skipped, not promoted to Read (fail closed).
    [Fact]
    public void Unknown_or_none_tier_key_is_rejected()
    {
        string unknownPath = WriteTemp(
            $"""<AgentServer enabled="true"><Key name="k" hash="{SecretHash}" tier="Root"/></AgentServer>""");
        string nonePath = WriteTemp(
            $"""<AgentServer enabled="true"><Key name="k" hash="{SecretHash}" tier="None"/></AgentServer>""");
        try
        {
            Assert.Empty(AgentServerConfig.Load(unknownPath).Keys);
            Assert.Empty(AgentServerConfig.Load(nonePath).Keys);
        }
        finally { File.Delete(unknownPath); File.Delete(nonePath); }
    }

    [Fact]
    public void Bus_element_is_optional_and_clamped()
    {
        var def = LoadXml("""<AgentServer enabled="true"><Key name="k" hash="ab" tier="Read"/></AgentServer>""");
        Assert.Equal(250, def.BusSamplerMs); Assert.Equal(1000, def.BusQueueDepth);
        var set = LoadXml("""<AgentServer enabled="true"><Bus samplerMs="500" queueDepth="20"/></AgentServer>""");
        Assert.Equal(500, set.BusSamplerMs); Assert.Equal(20, set.BusQueueDepth);
        var wild = LoadXml("""<AgentServer enabled="true"><Bus samplerMs="1" queueDepth="1"/></AgentServer>""");
        Assert.Equal(100, wild.BusSamplerMs); Assert.Equal(10, wild.BusQueueDepth);
    }

    [Fact]
    public void Bus_heartbeatMs_defaults_off_and_is_clamped_when_set()
    {
        Assert.Equal(0, LoadXml("""<AgentServer enabled="true"><Bus samplerMs="250"/></AgentServer>""").BusHeartbeatMs);
        Assert.Equal(0, LoadXml("""<AgentServer enabled="true"/>""").BusHeartbeatMs);
        Assert.Equal(0, LoadXml("""<AgentServer enabled="true"><Bus heartbeatMs="0"/></AgentServer>""").BusHeartbeatMs);
        Assert.Equal(0, LoadXml("""<AgentServer enabled="true"><Bus heartbeatMs="-5"/></AgentServer>""").BusHeartbeatMs);
        Assert.Equal(30000, LoadXml("""<AgentServer enabled="true"><Bus heartbeatMs="30000"/></AgentServer>""").BusHeartbeatMs);
        Assert.Equal(1000, LoadXml("""<AgentServer enabled="true"><Bus heartbeatMs="10"/></AgentServer>""").BusHeartbeatMs);
        Assert.Equal(3_600_000, LoadXml("""<AgentServer enabled="true"><Bus heartbeatMs="99999999"/></AgentServer>""").BusHeartbeatMs);
    }

    [Fact]
    public void ControlMode_restoreRemote_defaults_false_and_parses_true()
    {
        var def = LoadXml("""<AgentServer enabled="true" port="8026"><Key name="k" hash="AB" tier="Read"/></AgentServer>""");
        Assert.False(def.RestoreRemote);
        var set = LoadXml("""<AgentServer enabled="true" port="8026"><Key name="k" hash="AB" tier="Read"/><ControlMode restoreRemote="true"/></AgentServer>""");
        Assert.True(set.RestoreRemote);
    }

    [Fact]
    public void Key_recipeFolder_is_parsed_and_returned_by_authentication()
    {
        var cfg = LoadXml(
            $"""<AgentServer enabled="true" allowRecipe="true"><Key name="agent" hash="{SecretHash}" tier="Manual" recipeFolder="Agent-Paper2"/></AgentServer>""");
        var key = Assert.Single(cfg.Keys);
        Assert.Equal("Agent-Paper2", key.RecipeFolder);

        var granted = cfg.AuthenticateKey("secret");
        Assert.NotNull(granted);
        Assert.Equal("agent", granted.Name);
        Assert.Equal("Agent-Paper2", granted.RecipeFolder);
        Assert.Equal(AgentTier.Recipe, granted.Tier);          // still clamped: allowManual is false
        Assert.Null(cfg.AuthenticateKey("wrong"));
    }

    [Theory]
    [InlineData("recipeFolder=\"\"")]
    [InlineData("recipeFolder=\"   \"")]
    [InlineData("")]
    public void Key_without_a_recipeFolder_is_unscoped(string attribute)
    {
        var cfg = LoadXml(
            $"""<AgentServer enabled="true"><Key name="k" hash="{SecretHash}" tier="Recipe" {attribute}/></AgentServer>""");
        Assert.Null(Assert.Single(cfg.Keys).RecipeFolder);
    }

    // Same fail-closed rule as an unknown tier: a folder that could escape the recipes
    // directory or cannot be created is a config error on an auth surface — skip the key.
    [Theory]
    [InlineData("..")]
    [InlineData("..\\Recipes")]
    [InlineData("a\\b")]
    [InlineData("a/b")]
    [InlineData("C:")]
    [InlineData("\\\\server\\share")]
    [InlineData("CON")]
    [InlineData(".hidden")]
    [InlineData("Agent.")]
    [InlineData("Agent ")]
    [InlineData("a:b")]
    [InlineData("Agent..x")]
    public void Key_with_an_unsafe_recipeFolder_is_rejected(string folder)
    {
        var cfg = LoadXml(
            $"""<AgentServer enabled="true"><Key name="k" hash="{SecretHash}" tier="Recipe" recipeFolder="{folder}"/></AgentServer>""");
        Assert.Empty(cfg.Keys);
    }
}
