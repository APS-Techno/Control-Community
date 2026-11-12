using System.Collections.Immutable;

namespace APS.RecipeModel.Ast;

public sealed record LabelStatement(string Name) : Statement;
public sealed record GotoStatement(string Target) : Statement;
public sealed record CallStatement(string RecipeName) : Statement;
public sealed record VarStatement(string Name, NodeField Value) : Statement;
public sealed record FunctionCallStatement(string Name, ImmutableArray<NodeField> Args) : Statement;

/// <summary>Sets the system/alarm state machine state explicitly:
/// <c>state startup|idle|processing</c>. NodeField so a <c>$param</c> round-trips.</summary>
public sealed record StateStatement(NodeField State) : Statement;
