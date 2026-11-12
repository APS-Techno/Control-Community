using System.Security.Cryptography;
using System.Text;

namespace APS.SystemControl.Core.Evidence;

/// <summary>One flange's identity contribution to the fingerprint. Erosion, magnetron instance,
/// and the PSU's responding/ELN state are excluded on purpose; the connected PSU's id+model ARE
/// included so swapping the supply that drives a cathode registers as a setup change.</summary>
public readonly record struct FingerprintSource(
    int Flange, string? Material, string? Format, string? MagnetronModel,
    string? PsuId = null, string? PsuModel = null);

/// <summary>
/// Deterministic SHA-256 over the *reproducible inputs only*: effective recipe text +
/// per-flange material/format/model + the connected-PSU id/model driving that flange. Excludes
/// specimen, operator, timestamps, magnetron instance, erosion depth, PSU responding/output state,
/// ELN links, software/firmware, and the outcome. Hand-built canonical form
/// (all strings/ints) so we need no JCS dependency. See spec §Fingerprint.
/// </summary>
public static class DepositionFingerprint
{
    public static string Compute(string recipeText, IEnumerable<FingerprintSource> sources)
    {
        var sb = new StringBuilder();
        sb.Append("recipe\n").Append(NormalizeText(recipeText)).Append('\n');
        sb.Append("sources\n");
        foreach (var s in sources.OrderBy(s => s.Flange))
            sb.Append(s.Flange).Append('\t')
              .Append(s.Material ?? "").Append('\t')
              .Append(s.Format ?? "").Append('\t')
              .Append(s.MagnetronModel ?? "").Append('\t')
              .Append(s.PsuId ?? "").Append('\t')
              .Append(s.PsuModel ?? "").Append('\n');

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string NormalizeText(string s) =>
        string.Join("\n",
            s.Replace("\r\n", "\n").Replace("\r", "\n")
             .Split('\n')
             .Select(line => line.TrimEnd()));
}
