using APS.RecipeModel.Ast;

namespace APS.RecipeModel.Tests.Ast;

public class NodeIdTests
{
    [Fact]
    public void Same_Position_And_Kind_Produce_Same_Id()
    {
        var id1 = NodeId.FromPosition("valve", parentPath: "script", indexInParent: 3);
        var id2 = NodeId.FromPosition("valve", parentPath: "script", indexInParent: 3);
        Assert.Equal(id1, id2);
    }

    [Fact]
    public void Different_Index_Produces_Different_Id()
    {
        var id1 = NodeId.FromPosition("valve", parentPath: "script", indexInParent: 3);
        var id2 = NodeId.FromPosition("valve", parentPath: "script", indexInParent: 4);
        Assert.NotEqual(id1, id2);
    }
}
