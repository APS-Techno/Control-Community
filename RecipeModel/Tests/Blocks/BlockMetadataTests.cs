using System;
using System.Linq;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class BlockMetadataTests
{
    [Fact]
    public void Map_Contains_Every_RecipeBlockKind_Value()
    {
        var allKinds = Enum.GetValues<RecipeBlockKind>();
        var missing = allKinds.Where(k => !BlockMetadata.Map.ContainsKey(k)).ToArray();
        Assert.Empty(missing);
    }

    [Fact]
    public void Every_Entry_Has_NonEmpty_Syntax_And_Description()
    {
        foreach (var (kind, info) in BlockMetadata.Map)
        {
            Assert.False(string.IsNullOrWhiteSpace(info.Syntax),
                $"{kind} has empty Syntax");
            Assert.False(string.IsNullOrWhiteSpace(info.Description),
                $"{kind} has empty Description");
        }
    }
}
