using System;
using System.IO;
using APS.RecipeModel.Validation;
using Xunit;

namespace APS.RecipeModel.Tests.Validation;

public sealed class NonOperationalStageReaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nonop-" + Guid.NewGuid().ToString("N"));

    public NonOperationalStageReaderTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    private string Write(string name, string content)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private const string Worker = """
        <worker><operations>
          <op method="Rotation"><var>s</var></op>
        </operations></worker>
        """;

    private static string HwConf(string attrs) => $"""
        <hardware>
          <stages hwtype="X"><stage address="1" {attrs}>rotation</stage><stage address="2">VS</stage></stages>
        </hardware>
        """;

    [Fact]
    public void Reader_lists_only_stages_declared_not_operational()
    {
        string path = Write("a.xml", HwConf("operational=\"false\""));

        Assert.Equal("rotation", Assert.Single(HWConfDeviceReader.ReadNonOperationalStages(path)));
    }

    [Theory]
    [InlineData("operational=\"true\"")]
    [InlineData("operational=\"bogus\"")]
    [InlineData("")]
    public void Reader_ignores_operational_true_garbage_and_absent(string attrs)
    {
        Assert.Empty(HWConfDeviceReader.ReadNonOperationalStages(Write("b.xml", HwConf(attrs))));
    }

    [Fact]
    public void Reader_returns_empty_for_missing_or_malformed_file()
    {
        Assert.Empty(HWConfDeviceReader.ReadNonOperationalStages(Path.Combine(_dir, "missing.xml")));
        Assert.Empty(HWConfDeviceReader.ReadNonOperationalStages(Write("c.xml", "<hardware><stages")));
    }

    [Fact]
    public void FromConfig_marks_rotation_not_operational_when_the_profile_says_so()
    {
        var semantics = RecipeSemantics.FromConfig(
            Write("w.xml", Worker), Write("d.xml", HwConf("operational=\"false\"")));

        Assert.False(semantics.RotationOperational);
    }

    [Fact]
    public void FromConfig_keeps_rotation_operational_without_the_attribute()
    {
        var semantics = RecipeSemantics.FromConfig(Write("w.xml", Worker), Write("d.xml", HwConf("")));

        Assert.True(semantics.RotationOperational);
    }
}
