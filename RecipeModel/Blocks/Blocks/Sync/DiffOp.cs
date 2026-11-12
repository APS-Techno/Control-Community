using APS.RecipeModel.Ast;

namespace APS.RecipeModel.Blocks;

public abstract record DiffOp(int Index);

public sealed record BindInPlaceOp(int Index, AstNode NewNode) : DiffOp(Index);

public sealed record InsertOp(int Index, AstNode NewNode) : DiffOp(Index);

/// <summary>Later ops use indices of the list AFTER this removal (apply ops in reverse
/// order to avoid shifting).</summary>
public sealed record RemoveOp(int Index) : DiffOp(Index);

public sealed record ReplaceOp(int Index, AstNode NewNode) : DiffOp(Index);
