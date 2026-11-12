using System.IO;
using System.Xml.Serialization;
using APS.SystemControl.Core.CoreClass;

namespace CoreLogicTests;

public class RecipeLogConfigTests
{
    [Fact]
    public void AppConfig_defaults_user_on_embedded_off()
    {
        var cfg = new AppConfig();
        Assert.True(cfg.LogUserRecipes);
        Assert.False(cfg.LogEmbeddedRecipes);
    }

    [Fact]
    public void Legacy_xml_without_elements_uses_defaults()
    {
        // A RootConfig.xml written before these elements existed.
        const string legacy = "<AppConfig><Prefix>DemoProfile</Prefix></AppConfig>";
        var serializer = new XmlSerializer(typeof(AppConfig));
        using var reader = new StringReader(legacy);
        var cfg = (AppConfig)serializer.Deserialize(reader)!;

        Assert.True(cfg.LogUserRecipes);   // missing element => stays true
        Assert.False(cfg.LogEmbeddedRecipes); // missing element => stays false
    }

    [Fact]
    public void SaveRootConfig_round_trips_both_flags_when_flipped()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "rlcfg-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            var cfg = new AppConfig { Prefix = "P", LogUserRecipes = false, LogEmbeddedRecipes = true };
            Config.SaveRootConfig(cfg, tmp);

            var serializer = new XmlSerializer(typeof(AppConfig));
            using var reader = new StreamReader(tmp);
            var rt = (AppConfig)serializer.Deserialize(reader)!;

            Assert.False(rt.LogUserRecipes);
            Assert.True(rt.LogEmbeddedRecipes);
        }
        finally { try { File.Delete(tmp); } catch { } }
    }

    [Fact]
    public void SnapshotOf_copies_both_flags()
    {
        var src = new AppConfig { LogUserRecipes = false, LogEmbeddedRecipes = true };
        var snap = Config.SnapshotOf(src);
        Assert.False(snap.LogUserRecipes);
        Assert.True(snap.LogEmbeddedRecipes);
    }

    [Fact]
    public void Config_passthrough_reads_from_appconfig()
    {
        var cfg = new Config(new AppConfig { LogUserRecipes = false, LogEmbeddedRecipes = true });
        Assert.False(cfg.LogUserRecipes);
        Assert.True(cfg.LogEmbeddedRecipes);
    }
}
