namespace APS.RecipeModel.Ast;

/// <remarks>Lhs may be a sensor id, a device id, or a $param reference.
/// Rhs is either a literal or a $param reference.</remarks>
public sealed record ConditionExpr(string Lhs, string Op, NodeField Rhs)
{
    public string ToDsl() => $"{Lhs} {Op} {Rhs.ToDslToken()}";
}
