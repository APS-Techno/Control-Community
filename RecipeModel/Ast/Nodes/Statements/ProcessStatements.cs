using System.Collections.Immutable;

namespace APS.RecipeModel.Ast;

public sealed record WarmupStatement(NodeField Time, ImmutableArray<string> Sources) : Statement;
public sealed record DepositStatement(NodeField Position, NodeField Time, NodeField Retract) : Statement;
public sealed record RotationStatement(OnOff Action, NodeField? Speed) : Statement;
public sealed record RotateStatement(NodeField Speed) : Statement;
