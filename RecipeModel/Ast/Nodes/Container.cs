using System.Collections.Immutable;

namespace APS.RecipeModel.Ast;

public abstract record Container : AstNode
{
    public required ImmutableArray<AstNode> Body { get; init; }
}
