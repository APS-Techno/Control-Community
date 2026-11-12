using APS.RecipeModel.Ast;

namespace APS.RecipeModel.Tests.Ast;

public class RoundTripFixtureTests
{
    private static readonly string FixtureDir = Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "Ast");

    public static IEnumerable<object[]> AllFixtures()
    {
        foreach (var f in Directory.EnumerateFiles(FixtureDir, "*.recipe").OrderBy(p => p))
            yield return new object[] { Path.GetFileName(f) };
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void Fixture_Round_Trips_Byte_Identical(string fixtureName)
    {
        var path = Path.Combine(FixtureDir, fixtureName);
        var src = File.ReadAllText(path).Replace("\r\n", "\n");
        var printed = PrettyPrinter.Print(Parser.Parse(src));
        Assert.Equal(src, printed);
    }
}
