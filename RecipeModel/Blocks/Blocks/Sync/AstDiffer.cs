using System.Collections.Immutable;
using APS.RecipeModel.Ast;

namespace APS.RecipeModel.Blocks;

public static class AstDiffer
{
    public static IReadOnlyList<DiffOp> DiffBody(
        IReadOnlyList<AstNode> oldBody,
        IReadOnlyList<AstNode> newBody)
    {
        var ops = new List<DiffOp>();
        int common = Math.Min(oldBody.Count, newBody.Count);

        for (int i = 0; i < common; i++)
        {
            if (oldBody[i].GetType() == newBody[i].GetType())
                ops.Add(new BindInPlaceOp(i, newBody[i]));
            else
                ops.Add(new ReplaceOp(i, newBody[i]));
        }

        for (int i = common; i < newBody.Count; i++)
            ops.Add(new InsertOp(i, newBody[i]));

        for (int i = oldBody.Count - 1; i >= common; i--)
            ops.Add(new RemoveOp(i));

        return ops;
    }
}
