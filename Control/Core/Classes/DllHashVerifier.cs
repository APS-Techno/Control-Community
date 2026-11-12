using System.Security.Cryptography;

namespace APS.SystemControl.Core.Classes;

/// <summary>
/// Computes and verifies SHA-256 hashes of DLLs before they are loaded via
/// <see cref="System.Reflection.Assembly.LoadFrom"/>.
///
/// Hashes can be pinned in HWConf.xml / Worker.xml by adding a <c>sha256</c>
/// attribute to the library element. If the expected hash is present it MUST
/// match — unexpected binaries will refuse to load, which closes the DLL-hijack
/// vector on operator stations with a writable bin path. When the attribute is
/// absent the code logs a warning via <see cref="System.Diagnostics.Trace"/>
/// but allows the load to proceed, so deployments can be pinned incrementally.
/// </summary>
public static class DllHashVerifier
{
    public sealed class HashMismatchException(string dllPath, string expected, string actual)
        : InvalidOperationException(
            $"DLL hash verification failed for '{dllPath}': expected SHA-256 {expected}, got {actual}. Refusing to load.")
    {
        public string DllPath { get; } = dllPath;
        public string Expected { get; } = expected;
        public string Actual { get; } = actual;
    }

    /// <summary>
    /// Computes the SHA-256 of the file at <paramref name="dllPath"/> as a
    /// lower-case hex string, or null when the file cannot be read.
    /// </summary>
    public static string? ComputeHash(string dllPath)
    {
        try
        {
            using var stream = File.OpenRead(dllPath);
            byte[] hash = SHA256.HashData(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning(
                $"DllHashVerifier: unable to hash '{dllPath}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Returns true when it is safe to load the DLL:
    /// - no expected hash supplied (logged as a warning — soft-fail policy), or
    /// - the expected hash matches the computed hash.
    ///
    /// Throws <see cref="HashMismatchException"/> when a hash is supplied and
    /// does not match; the caller MUST NOT load the DLL in that case.
    /// </summary>
    public static bool Verify(string dllPath, string? expectedSha256Hex, Action<string>? logger = null)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256Hex))
        {
            string? computed = ComputeHash(dllPath);
            string warning = computed != null
                ? $"DLL '{Path.GetFileName(dllPath)}' loaded WITHOUT hash pinning. Add sha256=\"{computed}\" to the <library> element to lock this binary."
                : $"DLL '{Path.GetFileName(dllPath)}' loaded WITHOUT hash pinning and could not be hashed for reporting.";
            logger?.Invoke(warning);
            System.Diagnostics.Trace.TraceWarning(warning);
            return true;
        }

        string normalizedExpected = expectedSha256Hex.Trim().ToLowerInvariant();
        string? actual = ComputeHash(dllPath);
        if (actual == null)
            throw new HashMismatchException(dllPath, normalizedExpected, "<unreadable>");

        if (!string.Equals(actual, normalizedExpected, StringComparison.Ordinal))
            throw new HashMismatchException(dllPath, normalizedExpected, actual);

        return true;
    }
}
