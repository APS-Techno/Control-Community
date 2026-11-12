using System.Diagnostics;
using System.Reflection;

namespace APS.SystemControl.Core;

/// <summary>
/// Application version helpers. User-facing version strings use the
/// <b>FileVersion</b> (<c>Version.GitCommitCount</c>, e.g. <c>1.4.0.123</c>),
/// which carries the git-commit build number, rather than the AssemblyVersion
/// — that one is pinned to <c>Version.0</c> for binding stability and so would
/// always read as <c>1.4.0.0</c>.
/// </summary>
public static class AppVersion
{
    /// <summary>
    /// Full version including the git-commit build number (e.g. "1.4.0.123").
    /// Falls back to the AssemblyVersion if no FileVersion resource is present.
    /// </summary>
    public static string Full
    {
        get
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            string? fileVersion = string.IsNullOrEmpty(asm.Location)
                ? null
                : FileVersionInfo.GetVersionInfo(asm.Location).FileVersion;
            return string.IsNullOrWhiteSpace(fileVersion)
                ? asm.GetName().Version?.ToString() ?? "?"
                : fileVersion;
        }
    }
}
