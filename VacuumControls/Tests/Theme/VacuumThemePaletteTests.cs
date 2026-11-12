using System.Drawing;
using APS.Controls.Theme;
using Xunit;

namespace APS.Controls.Tests.Theme;

public sealed class VacuumThemePaletteTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "palette-" + Guid.NewGuid().ToString("N"));
    private readonly Color _original = VacuumTheme.BackgroundDark;

    public VacuumThemePaletteTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        File.WriteAllText(Path.Combine(_dir, "Palette.xml"),
            $"<Palette><BackgroundDark>{_original.R},{_original.G},{_original.B}</BackgroundDark></Palette>");
        VacuumTheme.LoadPalette(_dir);
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void LoadPalette_ReadsOverridesFromTheGivenProfileDirectory()
    {
        File.WriteAllText(Path.Combine(_dir, "Palette.xml"),
            "<Palette><BackgroundDark>#112233</BackgroundDark></Palette>");

        VacuumTheme.LoadPalette(_dir);

        Assert.Equal(Color.FromArgb(0x11, 0x22, 0x33), VacuumTheme.BackgroundDark);
    }

    [Fact]
    public void LoadPalette_WithoutPaletteFile_KeepsDefaults()
    {
        VacuumTheme.LoadPalette(_dir);

        Assert.Equal(_original, VacuumTheme.BackgroundDark);
    }

    [Fact]
    public void LoadPalette_WithNullDirectory_KeepsDefaults()
    {
        VacuumTheme.LoadPalette(null);

        Assert.Equal(_original, VacuumTheme.BackgroundDark);
    }
}
