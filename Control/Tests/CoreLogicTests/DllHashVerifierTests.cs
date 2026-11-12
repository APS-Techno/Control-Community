using APS.SystemControl.Core.Classes;

namespace CoreLogicTests;

public class DllHashVerifierTests : IDisposable
{
    private readonly string _tmp;

    public DllHashVerifierTests()
    {
        _tmp = Path.Combine(Path.GetTempPath(), "dll-hash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmp);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, true); } catch { /* best-effort */ }
    }

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_tmp, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void ComputeHash_returns_stable_lowercase_hex()
    {
        var path = WriteFile("a.bin", "hello");
        string? h1 = DllHashVerifier.ComputeHash(path);
        string? h2 = DllHashVerifier.ComputeHash(path);
        Assert.NotNull(h1);
        Assert.Equal(h1, h2);
        Assert.Equal(64, h1!.Length);           // SHA-256 = 32 bytes = 64 hex chars
        Assert.Matches("^[0-9a-f]+$", h1);
    }

    [Fact]
    public void ComputeHash_missing_file_returns_null()
    {
        Assert.Null(DllHashVerifier.ComputeHash(Path.Combine(_tmp, "does-not-exist.bin")));
    }

    [Fact]
    public void Verify_matching_hash_returns_true()
    {
        var path = WriteFile("match.bin", "payload");
        string expected = DllHashVerifier.ComputeHash(path)!;
        Assert.True(DllHashVerifier.Verify(path, expected));
    }

    [Fact]
    public void Verify_mismatched_hash_throws_with_expected_and_actual()
    {
        var path = WriteFile("mismatch.bin", "payload");
        string wrong = new string('0', 64);
        var ex = Assert.Throws<DllHashVerifier.HashMismatchException>(
            () => DllHashVerifier.Verify(path, wrong));
        Assert.Equal(path, ex.DllPath);
        Assert.Equal(wrong, ex.Expected);
        Assert.NotEqual(wrong, ex.Actual);
    }

    [Fact]
    public void Verify_case_insensitive_match()
    {
        var path = WriteFile("case.bin", "payload");
        string expected = DllHashVerifier.ComputeHash(path)!.ToUpperInvariant();
        Assert.True(DllHashVerifier.Verify(path, expected));
    }

    [Fact]
    public void Verify_null_or_empty_expected_logs_and_passes()
    {
        var path = WriteFile("unpinned.bin", "payload");
        string? logged = null;
        Assert.True(DllHashVerifier.Verify(path, null, msg => logged = msg));
        Assert.NotNull(logged);
        Assert.Contains("WITHOUT hash pinning", logged);
    }

    [Fact]
    public void Verify_missing_file_with_expected_throws_mismatch()
    {
        string expected = new string('a', 64);
        var ex = Assert.Throws<DllHashVerifier.HashMismatchException>(
            () => DllHashVerifier.Verify(Path.Combine(_tmp, "gone.bin"), expected));
        Assert.Equal(expected, ex.Expected);
        Assert.Equal("<unreadable>", ex.Actual);
    }
}
