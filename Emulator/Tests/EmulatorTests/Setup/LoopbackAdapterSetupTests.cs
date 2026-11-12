using APS.SystemEmulator.Setup;

namespace EmulatorTests.Setup;

public class LoopbackAdapterSetupTests
{
    [Fact]
    public void Ok_when_loopback_name_resolved()
    {
        var row = LoopbackAdapterSetup.Evaluate("Loopback Pseudo-Interface 1");

        Assert.Equal(CheckStatus.Ok, row.Status);
        Assert.Contains("Loopback Pseudo-Interface 1", row.Detail);
        Assert.False(row.Fixable);
    }

    [Fact]
    public void Ok_when_loopback_name_is_localized_non_english()
    {
        // Detection must NOT depend on the English literal "Loopback": on localized Windows
        // the OS renames the software loopback.
        const string localized = "Псевдоинтерфейс 1";

        Assert.DoesNotContain("Loopback", localized);

        var row = LoopbackAdapterSetup.Evaluate(localized);

        Assert.Equal(CheckStatus.Ok, row.Status);
        Assert.Contains(localized, row.Detail);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Missing_when_no_loopback_resolved(string? name)
    {
        var row = LoopbackAdapterSetup.Evaluate(name);

        Assert.Equal(CheckStatus.Missing, row.Status);
        Assert.False(row.Fixable);
    }

    [Fact]
    public void ResolveLoopbackName_finds_the_local_software_loopback()
    {
        // Every Windows host has a built-in software loopback; the API must find it and return a
        // non-empty name regardless of UI language. This is what keeps the devcon install button
        // hidden in normal operation.
        var name = LoopbackAdapterSetup.ResolveLoopbackName();

        Assert.False(string.IsNullOrEmpty(name));
    }
}
