using APS.SystemControl.Core.Evidence;
using Xunit;

namespace APS.SystemControl.Tests.CoreLogic;

public class DepositionFingerprintTests
{
    private static FingerprintSource S(int f, string mat, string fmt, string model) => new(f, mat, fmt, model);

    [Fact]
    public void IsDeterministic_AndOrderIndependent()
    {
        string a = DepositionFingerprint.Compute("line1\nline2",
            [S(2, "W", "4\" round", "MAG-4"), S(5, "Si", "4\" round", "MAG-4")]);
        string b = DepositionFingerprint.Compute("line1\nline2",
            [S(5, "Si", "4\" round", "MAG-4"), S(2, "W", "4\" round", "MAG-4")]); // reordered
        Assert.Equal(a, b);
        Assert.StartsWith("sha256:", a);
    }

    [Fact]
    public void RecipeWhitespace_IsNormalized()
    {
        string lf   = DepositionFingerprint.Compute("a\nb",     [S(2, "W", "x", "m")]);
        string crlf = DepositionFingerprint.Compute("a  \r\nb", [S(2, "W", "x", "m")]); // CRLF + trailing spaces
        Assert.Equal(lf, crlf);
    }

    [Fact]
    public void Material_Change_ChangesHash()
    {
        string w  = DepositionFingerprint.Compute("r", [S(2, "W",  "x", "m")]);
        string si = DepositionFingerprint.Compute("r", [S(2, "Si", "x", "m")]);
        Assert.NotEqual(w, si);
    }

    [Fact]
    public void ConnectedPsu_ChangesHash()
    {
        string none = DepositionFingerprint.Compute("r", [S(2, "W", "x", "m")]);
        string dc1  = DepositionFingerprint.Compute("r", [new FingerprintSource(2, "W", "x", "m", "DC1", "PSU-A")]);
        Assert.NotEqual(none, dc1);
    }

    [Fact]
    public void PsuModelSwap_ChangesHash()
    {
        string a = DepositionFingerprint.Compute("r", [new FingerprintSource(2, "W", "x", "m", "DC1", "PSU-A")]);
        string b = DepositionFingerprint.Compute("r", [new FingerprintSource(2, "W", "x", "m", "DC1", "BIAS-1")]);
        Assert.NotEqual(a, b);
    }
}
