namespace APS.SystemControl.Core.Classes;

/// <summary>
/// Forwarder to the canonical <see cref="APS.RecipeModel.Parsing.TimeParser"/>.
///
/// The implementation lives in RecipeModel (the DSL layer) so the pre-run
/// validator and the runtime executor parse recipe time literals with the SAME
/// code; a second implementation here let "deposit Ion 30s 1" pass validation
/// and then fail at execution. Do not reimplement the suffix rules in this
/// file — change RecipeModel/Parsing/TimeParser.cs instead.
/// </summary>
public static class TimeParser
{
    /// <inheritdoc cref="APS.RecipeModel.Parsing.TimeParser.ParseSeconds"/>
    public static float ParseSeconds(string input)
        => APS.RecipeModel.Parsing.TimeParser.ParseSeconds(input);

    /// <inheritdoc cref="APS.RecipeModel.Parsing.TimeParser.TryParseSeconds"/>
    public static bool TryParseSeconds(string input, out float seconds)
        => APS.RecipeModel.Parsing.TimeParser.TryParseSeconds(input, out seconds);
}
