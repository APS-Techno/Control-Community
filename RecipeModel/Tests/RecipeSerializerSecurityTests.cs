using System.Xml;
using APS.RecipeModel.Serialization;

namespace RecipeModel.Tests;

public class RecipeSerializerSecurityTests
{
    // Small DOCTYPE payload — not a real billion-laughs bomb, just enough to
    // prove DTD processing is prohibited at parse time (throws before any
    // entity expansion could occur).
    private const string DoctypeXml = """
        <?xml version="1.0"?>
        <!DOCTYPE r [<!ENTITY a "x">]>
        <Recipe><Script>&a;</Script></Recipe>
        """;

    [Fact]
    public void LoadFromString_RejectsDoctype()
    {
        Assert.Throws<XmlException>(() => RecipeSerializer.LoadFromString(DoctypeXml));
    }

    [Fact]
    public void Load_RejectsDoctype()
    {
        string tempFile = Path.GetTempFileName() + ".rcpx";
        try
        {
            File.WriteAllText(tempFile, DoctypeXml);
            Assert.Throws<XmlException>(() => RecipeSerializer.Load(tempFile));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void LoadFromString_NormalRecipe_StillRoundTrips()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <Recipe><Script>delay 1</Script></Recipe>
            """;

        var recipe = RecipeSerializer.LoadFromString(xml);
        Assert.Equal("delay 1", recipe.Script);
    }
}
