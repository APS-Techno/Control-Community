using System.Globalization;

namespace APS.RecipeModel.Parsing;

/// <summary>
/// Parses recipe time tokens into canonical seconds (matching OpDelay and every
/// timeout argument). A bare number is seconds; a number followed by a unit
/// suffix is converted.
///
/// This is the CANONICAL implementation, shared by the pre-run validator and the
/// runtime executor. <c>APS.SystemControl.Core.Classes.TimeParser</c>
/// forwards here; do not reimplement the suffix rules anywhere else. A recipe
/// literal the validator accepts must be one the executor accepts, and vice
/// versa — the 2026-07-06 "deposit Ion 30s 1" incident was a drift of exactly
/// this kind.
/// </summary>
public static class TimeParser
{
    private static readonly (string Suffix, double Factor)[] Suffixes =
    {
        ("h", 3600.0), ("m", 60.0), ("s", 1.0),
    };

    /// <summary>
    /// Accepts e.g. "30", "30s", "30m", "1.5h". Bare numbers are seconds. Uses
    /// double arithmetic internally so unit conversion doesn't accumulate f32
    /// rounding error before being cast back.
    /// </summary>
    public static float ParseSeconds(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new FormatException("Empty time value");

        if (double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out double bare))
            return (float)bare;

        foreach (var (suffix, factor) in Suffixes)
        {
            if (input.EndsWith(suffix, StringComparison.Ordinal))
            {
                string numPart = input[..^suffix.Length];
                if (double.TryParse(numPart, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                    return (float)(v * factor);
                throw new FormatException(
                    $"Invalid time value '{input}': '{numPart}' is not a valid number");
            }
        }

        throw new FormatException(
            $"Invalid time value '{input}': expected number with optional suffix (s, m, or h)");
    }

    public static bool TryParseSeconds(string input, out float seconds)
    {
        seconds = 0f;
        try
        {
            seconds = ParseSeconds(input);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
