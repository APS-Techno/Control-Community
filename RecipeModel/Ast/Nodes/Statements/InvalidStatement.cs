namespace APS.RecipeModel.Ast;

/// <summary>Emitted when a line does not match any known DSL production.
/// Preserves the original text so pretty-printing reproduces the file.</summary>
public sealed record InvalidStatement(string OriginalText, string Error) : Statement;
