using System.Collections.Immutable;
using APS.RecipeModel.Ast;

namespace APS.RecipeModel.Blocks;

/// <summary>Assigns a fresh <see cref="NodeId"/> to every node; every other field
/// is preserved.</summary>
public static class AstCloner
{
    private static int _counter;

    public static AstNode Clone(AstNode node)
    {
        var fresh = NodeId.FromPosition(node.GetType().Name, "clone", System.Threading.Interlocked.Increment(ref _counter));

        return node switch
        {
            IfElseContainer ife => ife with
            {
                Id = fresh,
                Body = ife.Body.Select(Clone).ToImmutableArray(),
                ElseBody = ife.ElseBody?.Select(Clone).ToImmutableArray(),
            },
            Container c => c with
            {
                Id = fresh,
                Body = c.Body.Select(Clone).ToImmutableArray(),
            },
            _ => node with { Id = fresh },
        };
    }
}
