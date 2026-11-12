namespace APS.SystemControl.Core.Classes;

/// <summary>
/// Forwarder to the canonical <see cref="APS.RecipeModel.Parsing.PressureParser"/>.
/// See the note on <see cref="TimeParser"/> — the implementation lives in
/// RecipeModel so the validator and the executor agree on what a pressure
/// literal means. Do not reimplement the suffix table here.
/// </summary>
public static class PressureParser
{
    /// <inheritdoc cref="APS.RecipeModel.Parsing.PressureParser.ParseTorr"/>
    public static float ParseTorr(string input)
        => APS.RecipeModel.Parsing.PressureParser.ParseTorr(input);

    /// <inheritdoc cref="APS.RecipeModel.Parsing.PressureParser.TryParseTorr"/>
    public static bool TryParseTorr(string input, out float torr)
        => APS.RecipeModel.Parsing.PressureParser.TryParseTorr(input, out torr);
}
