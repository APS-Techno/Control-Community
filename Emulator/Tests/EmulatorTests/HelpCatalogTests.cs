using APS.SystemEmulator.Help;
using APS.SystemEmulator.Hosting;

namespace EmulatorTests;

public sealed class HelpCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "help-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string MakeFolder(string name, string indexJson, params string[] topicFiles)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(dir, "topics"));
        File.WriteAllText(Path.Combine(dir, "help-index.json"), indexJson);
        foreach (var f in topicFiles) File.WriteAllText(Path.Combine(dir, "topics", f), "<html/>");
        return dir;
    }

    private const string BaseIndex = """
        { "topics": [ { "title": "Overview", "file": "overview.html" },
                      { "title": "Reference", "children": [ { "title": "Ports", "file": "ports.html" } ] } ] }
        """;

    [Fact]
    public void Load_WithExtraFolder_MergesItsTopicsAndResolvesItsFiles()
    {
        var basic = MakeFolder("base", BaseIndex, "overview.html", "ports.html");
        var extra = MakeFolder("extra", """
            { "topics": [ { "title": "Reference", "children": [ { "title": "Extra device", "file": "extra.html" } ] },
                          { "title": "Appendix", "file": "appendix.html" } ] }
            """, "extra.html", "appendix.html");

        var catalog = HelpCatalog.Load(basic, [extra]);

        Assert.Equal(["Overview", "Reference", "Appendix"], catalog.Topics.Select(t => t.Title));
        Assert.Equal(["Ports", "Extra device"], catalog.Topics[1].Children!.Select(t => t.Title));
        Assert.Equal(Path.Combine(extra, "topics", "extra.html"), catalog.ResolveTopic("extra.html"));
        Assert.Equal(Path.Combine(basic, "topics", "ports.html"), catalog.ResolveTopic("ports.html"));
    }

    [Fact]
    public void Load_ExtraFolderTopicWithSameFileOverridesTheBaseTopic()
    {
        var basic = MakeFolder("base", BaseIndex, "overview.html", "ports.html");
        var extra = MakeFolder("extra", """
            { "topics": [ { "title": "Overview (extended)", "file": "overview.html" } ] }
            """, "overview.html");

        var catalog = HelpCatalog.Load(basic, [extra]);

        Assert.Equal(["Overview (extended)", "Reference"], catalog.Topics.Select(t => t.Title));
        Assert.Equal(Path.Combine(extra, "topics", "overview.html"), catalog.ResolveTopic("overview.html"));
        Assert.Equal(Path.Combine(basic, "topics", "ports.html"), catalog.ResolveTopic("ports.html"));
    }

    [Fact]
    public void Load_LaterExtraFolderWinsOverEarlierOne()
    {
        var basic = MakeFolder("base", BaseIndex, "overview.html");
        var first = MakeFolder("first", """{ "topics": [ { "title": "A", "file": "overview.html" } ] }""", "overview.html");
        var second = MakeFolder("second", """{ "topics": [ { "title": "B", "file": "overview.html" } ] }""", "overview.html");

        var catalog = HelpCatalog.Load(basic, [first, second]);

        Assert.Equal(["B", "Reference"], catalog.Topics.Select(t => t.Title));
        Assert.Equal(Path.Combine(second, "topics", "overview.html"), catalog.ResolveTopic("overview.html"));
    }

    [Fact]
    public void Load_WithNoExtraFolders_ReturnsOnlyTheBaseIndex()
    {
        var basic = MakeFolder("base", BaseIndex, "overview.html", "ports.html");

        var catalog = HelpCatalog.Load(basic, []);

        Assert.Equal(["Overview", "Reference"], catalog.Topics.Select(t => t.Title));
        Assert.Null(catalog.ResolveTopic("extra.html"));
    }

    [Fact]
    public void Load_SkipsExtraFolderWithoutIndex()
    {
        var basic = MakeFolder("base", BaseIndex, "overview.html");

        var catalog = HelpCatalog.Load(basic, [Path.Combine(_root, "missing")]);

        Assert.Equal(2, catalog.Topics.Count);
    }

    [Fact]
    public void Community_ProvidesNoExtraHelpFolders()
        => Assert.Empty(EmulatorEdition.Community.ExtraHelpFolders);
}
