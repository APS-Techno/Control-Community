using System.Collections.Immutable;

namespace APS.RecipeModel.Ast;

/// <summary>Typically framed by `start`/`finish` markers that live as trivia
/// on the first/last child.</summary>
public sealed record ScriptContainer : Container
{
    public bool HasStartMarker { get; init; }
    public bool HasFinishMarker { get; init; }
}
