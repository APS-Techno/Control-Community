namespace APS.RecipeModel.Ast;

/// <summary>Move a wafer between two robot faces. Src and Dst are face names (or a $param reference);
/// face-name validation happens in the validator, not here.</summary>
public sealed record TransferStatement(NodeField Src, NodeField Dst) : Statement;

/// <summary>Home the transfer robot.</summary>
public sealed record HomeStatement() : Statement;

/// <summary>Abort the robot's current motion.</summary>
public sealed record AbortStatement() : Statement;
