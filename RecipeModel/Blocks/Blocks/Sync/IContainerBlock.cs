namespace APS.RecipeModel.Blocks;

/// <summary>Mouth order must be stable and match the order in which mouths are populated by
/// <see cref="BlockBase.BindTo"/>.</summary>
public interface IContainerBlock
{
    /// <summary>Nested mouths in declaration order. For example, <c>IfBlock</c>
    /// returns [ThenStack, ElseStack].</summary>
    IReadOnlyList<BlockStackControl> Mouths { get; }

    /// <summary>In the same order as <see cref="Mouths"/>. May return empty arrays for
    /// unbound / unused mouths (e.g., null ElseBody).</summary>
    IReadOnlyList<System.Collections.Immutable.ImmutableArray<APS.RecipeModel.Ast.AstNode>> ExtractBodies(APS.RecipeModel.Ast.AstNode node);

    /// <summary>Binds only the header fields (e.g. count, condition) without touching the
    /// mouth stacks, so <see cref="AstPatcher"/> can recurse into the mouths itself while
    /// preserving existing child block identities.</summary>
    void BindHeaderTo(APS.RecipeModel.Ast.AstNode node);
}
