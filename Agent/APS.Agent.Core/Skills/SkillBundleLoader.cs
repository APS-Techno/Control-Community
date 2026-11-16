using System.Security.Cryptography;
using System.Text;

namespace APS.Agent.Core.Skills;

public sealed record SkillBundle(
    string RootDirectory,
    string CombinedPromptText,
    string SkillDigestSha256,
    IReadOnlyList<string> RelativeFiles);

public static class SkillBundleLoader
{
    public static SkillBundle Load(string? skillDir = null)
    {
        string? resolved = skillDir
            ?? Environment.GetEnvironmentVariable("APS_SKILL_DIR")
            ?? Environment.GetEnvironmentVariable("ADS_BENCH_SKILL_DIR");

        if (string.IsNullOrWhiteSpace(resolved))
            throw new InvalidOperationException("Skill directory is not configured (set APS_SKILL_DIR or ADS_BENCH_SKILL_DIR).");

        string root = Path.GetFullPath(resolved);
        string skillMdPath = Path.Combine(root, "SKILL.md");
        if (!File.Exists(skillMdPath))
            throw new FileNotFoundException($"Skill file not found: {skillMdPath}", skillMdPath);

        var files = new List<string> { "SKILL.md" };
        var parts = new List<string> { NormalizeNewlines(File.ReadAllText(skillMdPath, Encoding.UTF8)) };

        string refsDir = Path.Combine(root, "references");
        if (Directory.Exists(refsDir))
        {
            foreach (string file in Directory.EnumerateFiles(refsDir, "*.md", SearchOption.TopDirectoryOnly)
                .OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal))
            {
                files.Add($"references/{Path.GetFileName(file)}");
                parts.Add(NormalizeNewlines(File.ReadAllText(file, Encoding.UTF8)));
            }
        }

        string combined = string.Join("\n\n", parts);
        string sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(combined))).ToLowerInvariant();
        return new SkillBundle(root, combined, sha256, files);
    }

    private static string NormalizeNewlines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);
}
