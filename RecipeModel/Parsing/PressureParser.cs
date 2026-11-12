using System.Globalization;

namespace APS.RecipeModel.Parsing;

/// <summary>
/// Parses recipe pressure tokens into canonical Torr (matching the pm/pl
/// sensor units). A bare number is Torr; a number followed by a unit suffix
/// is converted.
///
/// This is the CANONICAL implementation — see the note on
/// <see cref="TimeParser"/>. <c>APS.SystemControl.Core.Classes.PressureParser</c>
/// forwards here so validator and executor cannot disagree about what a
/// pressure literal means.
/// </summary>
public static class PressureParser
{
    private static readonly (string Suffix, double Factor)[] Suffixes =
    {
        ("nTorr", 1e-9), ("uTorr", 1e-6), ("µTorr", 1e-6),
        ("mTorr", 1e-3), ("kTorr", 1e3),  ("Torr",  1.0),
        ("nT",    1e-9), ("uT",    1e-6), ("µT",    1e-6),
        ("mT",    1e-3), ("kT",    1e3),  ("T",     1.0),
    };

    /// <summary>
    /// Accepts e.g. "0.05", "50mT", "1uTorr", "1e-6T". Bare numbers are Torr.
    /// Uses double arithmetic internally so unit conversion (e.g. 1500 * 1e-3)
    /// doesn't accumulate f32 rounding error before being cast back.
    /// </summary>
    public static float ParseTorr(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new FormatException("Empty pressure value");

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
                    $"Invalid pressure value '{input}': '{numPart}' is not a valid number");
            }
        }

        throw new FormatException(
            $"Invalid pressure value '{input}': expected number with optional suffix " +
            $"(T, mT, uT, nT, kT, or Torr/mTorr/uTorr/nTorr/kTorr)");
    }

    public static bool TryParseTorr(string input, out float torr)
    {
        torr = 0f;
        try
        {
            torr = ParseTorr(input);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
