using System.Text;

namespace APS.Agent.Core.Skills;

public sealed record ToolCallReceipt(string Text, bool IsError);

public sealed class SandboxedSkillReadTool
{
    private readonly string _rootWithSep;
    private readonly string _rootNoSep;

    public SandboxedSkillReadTool(string skillRootDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skillRootDir);
        string full = Path.GetFullPath(skillRootDir);
        _rootNoSep = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        _rootWithSep = _rootNoSep + Path.DirectorySeparatorChar;
    }

    public ToolCallReceipt Read(string requestedPath)
    {
        if (string.IsNullOrWhiteSpace(requestedPath))
            return new ToolCallReceipt("path outside the skill snapshot", true);

        string normalized = requestedPath.Replace('\\', '/').Trim();
        if (normalized.StartsWith("//", StringComparison.Ordinal) || Path.IsPathRooted(normalized))
            return new ToolCallReceipt("path outside the skill snapshot", true);

        if (normalized.StartsWith("aps-operator/", StringComparison.OrdinalIgnoreCase))
            normalized = normalized["aps-operator/".Length..];

        string combined = Path.Combine(_rootNoSep, normalized.Replace('/', Path.DirectorySeparatorChar));
        string resolved;
        try
        {
            resolved = Path.GetFullPath(combined);
        }
        catch (Exception)
        {
            return new ToolCallReceipt("path outside the skill snapshot", true);
        }

        if (!resolved.StartsWith(_rootWithSep, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            return new ToolCallReceipt("path outside the skill snapshot", true);

        if (HasReparsePointOnPath(_rootNoSep, resolved))
            return new ToolCallReceipt("path outside the skill snapshot", true);

        if (!File.Exists(resolved))
            return new ToolCallReceipt("file not found", true);

        return new ToolCallReceipt(File.ReadAllText(resolved, Encoding.UTF8), false);
    }

    private static bool HasReparsePointOnPath(string root, string target)
    {
        if (IsReparse(root)) return true;
        string rel = Path.GetRelativePath(root, target);
        string current = root;
        foreach (string seg in rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (string.IsNullOrEmpty(seg)) continue;
            current = Path.Combine(current, seg);
            if (IsReparse(current)) return true;
        }
        return false;
    }

    private static bool IsReparse(string path)
    {
        if (!Path.Exists(path)) return false;
        FileAttributes attrs = File.GetAttributes(path);
        return (attrs & FileAttributes.ReparsePoint) != 0;
    }
}
