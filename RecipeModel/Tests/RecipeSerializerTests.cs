using System.Xml;
using APS.RecipeModel.Model;
using APS.RecipeModel.Serialization;

namespace RecipeModel.Tests;

public class RecipeSerializerTests
{
    private const string SampleRcpx = """
        <?xml version="1.0" encoding="utf-8"?>
        <Recipe>
          <Description type="Sputtering">
            <Title>Test Recipe</Title>
            <Author>John</Author>
            <Created>2026-04-08</Created>
            <Modified>2026-04-08</Modified>
            <Notes>Test notes</Notes>
          </Description>
          <Parameters>
            <Group name="Bias">
              <Param name="BiasV" caption="Bias Voltage" unit="V" type="float">35</Param>
              <Param name="N" caption="Stacks" unit="" type="int">10</Param>
            </Group>
            <Group name="Layer">
              <Param name="timeB" caption="Time" unit="sec" type="float">40</Param>
            </Group>
          </Parameters>
          <Script>
        start
          delay 5
        finish
          </Script>
        </Recipe>
        """;

    [Fact]
    public void Load_ParsesDescription()
    {
        var recipe = RecipeSerializer.LoadFromString(SampleRcpx);
        Assert.Equal("Test Recipe", recipe.Description.Title);
        Assert.Equal("Sputtering", recipe.Description.Type);
        Assert.Equal("John", recipe.Description.Author);
        Assert.Equal("Test notes", recipe.Description.Notes);
    }

    [Fact]
    public void Load_ParsesParameterGroups()
    {
        var recipe = RecipeSerializer.LoadFromString(SampleRcpx);
        Assert.Equal(2, recipe.Parameters.Count);
        Assert.Equal("Bias", recipe.Parameters[0].Name);
        Assert.Equal("Layer", recipe.Parameters[1].Name);
    }

    [Fact]
    public void Load_ParsesParams()
    {
        var recipe = RecipeSerializer.LoadFromString(SampleRcpx);
        var biasGroup = recipe.Parameters[0];
        Assert.Equal(2, biasGroup.Params.Count);
        var biasV = biasGroup.Params[0];
        Assert.Equal("BiasV", biasV.Name);
        Assert.Equal("Bias Voltage", biasV.Caption);
        Assert.Equal("V", biasV.Unit);
        Assert.Equal(ParamType.Float, biasV.ParamType);
        Assert.Equal("35", biasV.Value);
        var n = biasGroup.Params[1];
        Assert.Equal("N", n.Name);
        Assert.Equal(ParamType.Int, n.ParamType);
        Assert.Equal("10", n.Value);
    }

    [Fact]
    public void Load_ParsesScript()
    {
        var recipe = RecipeSerializer.LoadFromString(SampleRcpx);
        Assert.Contains("delay 5", recipe.Script);
        Assert.Contains("start", recipe.Script);
    }

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        var original = new RecipeFile
        {
            Description = new RecipeDescription
            {
                Title = "Round Trip",
                Type = "Cleaning",
                Author = "Test",
                Notes = "RoundTrip test"
            },
            Parameters =
            [
                new ParamGroup
                {
                    Name = "Params",
                    Params =
                    [
                        new RecipeParam
                        {
                            Name = "T", Caption = "Time", Unit = "sec",
                            ParamType = ParamType.Float, Value = "60"
                        }
                    ]
                }
            ],
            Script = "start\n  delay $T\nfinish"
        };

        string tempFile = Path.GetTempFileName() + ".rcpx";
        try
        {
            RecipeSerializer.Save(original, tempFile);
            var loaded = RecipeSerializer.Load(tempFile);
            Assert.Equal("Round Trip", loaded.Description.Title);
            Assert.Equal("Cleaning", loaded.Description.Type);
            Assert.Single(loaded.Parameters);
            Assert.Equal("T", loaded.Parameters[0].Params[0].Name);
            Assert.Equal("60", loaded.Parameters[0].Params[0].Value);
            Assert.Contains("delay $T", loaded.Script);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Load_BoolParam_ParsesCorrectly()
    {
        string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <Recipe>
              <Description type="Test"><Title>T</Title></Description>
              <Parameters>
                <Group name="G">
                  <Param name="flag" caption="Flag" unit="" type="bool">true</Param>
                </Group>
              </Parameters>
              <Script>start</Script>
            </Recipe>
            """;

        var recipe = RecipeSerializer.LoadFromString(xml);
        Assert.Equal(ParamType.Bool, recipe.Parameters[0].Params[0].ParamType);
        Assert.Equal("true", recipe.Parameters[0].Params[0].Value);
    }
}
