using APS.RecipeModel.Validation;
using Xunit;

namespace APS.RecipeModel.Tests.Validation;

public class SourcesReaderTests
{
    private static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Sources.sample.xml");

    [Fact]
    public void Read_Returns_Distinct_Materials_In_Document_Order()
    {
        // The fixture has two nameless Si sources (flange 2 and flange 5) —
        // same material, both nameless, so "Si" is ambiguous per
        // SourceNames.Resolvable and is excluded entirely, not collapsed to one.
        // Only the unique BN and Ion materials survive.
        var materials = SourcesReader.Read(FixturePath);
        Assert.Equal(new[] { "BN", "Ion" }, materials.ToArray());
    }

    [Fact]
    public void Read_Skips_Empty_Material_Elements()
    {
        var materials = SourcesReader.Read(FixturePath);
        Assert.DoesNotContain("", materials);
    }

    [Fact]
    public void Read_Missing_File_Returns_Empty()
    {
        var materials = SourcesReader.Read(Path.Combine(Path.GetTempPath(), "no-such-file.xml"));
        Assert.Empty(materials);
    }

    [Fact]
    public void Read_Malformed_File_Returns_Empty()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp, "<not-xml>>>>");
            var materials = SourcesReader.Read(tmp);
            Assert.Empty(materials);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Read_UsesNameWhenPresent_ElseMaterial()
    {
        string tmp = Path.Combine(Path.GetTempPath(), $"sr-{Guid.NewGuid():N}.xml");
        File.WriteAllText(tmp,
            "<sources>" +
            "<source flange=\"1\"><material>Si</material></source>" +
            "<source flange=\"2\" name=\"W_1\"><material>W</material></source>" +
            "<source flange=\"3\" name=\"W_2\"><material>W</material></source>" +
            "</sources>");
        try
        {
            Assert.Equal(new[] { "Si", "W_1", "W_2" }, SourcesReader.Read(tmp).ToArray());
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Read_HalfNamedDuplicateMaterial_ExcludesAmbiguousMaterial_KeepsUniqueName()
    {
        // One W source named W_1, one nameless W -> effective names {"W_1", "W"}
        // have no collisions themselves, but both sources share material "W",
        // so "W" is ambiguous and must not be offered — "W_1" still resolves.
        string tmp = Path.Combine(Path.GetTempPath(), $"sr-{Guid.NewGuid():N}.xml");
        File.WriteAllText(tmp,
            "<sources>" +
            "<source flange=\"2\" name=\"W_1\"><material>W</material></source>" +
            "<source flange=\"3\"><material>W</material></source>" +
            "</sources>");
        try
        {
            var names = SourcesReader.Read(tmp);
            Assert.Equal(new[] { "W_1" }, names.ToArray());
            Assert.DoesNotContain("W", names);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Read_FullyNamelessDuplicateMaterial_ExcludesBoth()
    {
        string tmp = Path.Combine(Path.GetTempPath(), $"sr-{Guid.NewGuid():N}.xml");
        File.WriteAllText(tmp,
            "<sources>" +
            "<source flange=\"2\"><material>W</material></source>" +
            "<source flange=\"4\"><material>W</material></source>" +
            "</sources>");
        try
        {
            Assert.Empty(SourcesReader.Read(tmp));
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Read_UniqueMaterials_Unchanged_SingleSiSourceStillReturned()
    {
        string tmp = Path.Combine(Path.GetTempPath(), $"sr-{Guid.NewGuid():N}.xml");
        File.WriteAllText(tmp,
            "<sources>" +
            "<source flange=\"1\"><material>Si</material></source>" +
            "</sources>");
        try
        {
            Assert.Equal(new[] { "Si" }, SourcesReader.Read(tmp).ToArray());
        }
        finally { File.Delete(tmp); }
    }
}
