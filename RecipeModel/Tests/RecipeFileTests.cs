using APS.RecipeModel.Model;

namespace RecipeModel.Tests;

public class RecipeFileTests
{
    [Fact]
    public void GenerateVarBlock_EmitsVarLines()
    {
        var recipe = new RecipeFile
        {
            Parameters =
            [
                new ParamGroup
                {
                    Name = "Bias",
                    Params =
                    [
                        new RecipeParam { Name = "BiasVoltage", Value = "35", ParamType = ParamType.Float },
                        new RecipeParam { Name = "N", Value = "10", ParamType = ParamType.Int }
                    ]
                }
            ]
        };

        string block = recipe.GenerateVarBlock();

        Assert.Contains("var BiasVoltage 35", block);
        Assert.Contains("var N 10", block);
    }

    [Fact]
    public void GenerateVarBlock_Empty_ReturnsEmpty()
    {
        var recipe = new RecipeFile();
        Assert.Equal("", recipe.GenerateVarBlock());
    }

    [Fact]
    public void GenerateVarBlock_MultipleGroups_EmitsAll()
    {
        var recipe = new RecipeFile
        {
            Parameters =
            [
                new ParamGroup
                {
                    Name = "A",
                    Params = [new RecipeParam { Name = "x", Value = "1" }]
                },
                new ParamGroup
                {
                    Name = "B",
                    Params = [new RecipeParam { Name = "y", Value = "2" }]
                }
            ]
        };

        string block = recipe.GenerateVarBlock();
        Assert.Contains("var x 1", block);
        Assert.Contains("var y 2", block);
    }

    [Fact]
    public void FullScript_PrependsVarsToScript()
    {
        var recipe = new RecipeFile
        {
            Script = "start\n  delay 5\nfinish",
            Parameters =
            [
                new ParamGroup
                {
                    Name = "G",
                    Params = [new RecipeParam { Name = "T", Value = "60" }]
                }
            ]
        };

        string full = recipe.FullScript;
        Assert.StartsWith("var T 60", full);
        Assert.Contains("start", full);
        Assert.Contains("delay 5", full);
    }

    [Fact]
    public void ApplyParameterOverrides_ReplacesMatchingValues_InFullScript()
    {
        var recipe = new RecipeFile
        {
            Script = "pwait pm pressure 60",
            Parameters =
            [
                new ParamGroup
                {
                    Name = "Process",
                    Params =
                    [
                        new RecipeParam { Name = "pressure", Value = "2.00" },
                        new RecipeParam { Name = "flow", Value = "5" }
                    ]
                }
            ]
        };

        recipe.ApplyParameterOverrides(new Dictionary<string, string>
        {
            ["pressure"] = "5.00"
        });

        string full = recipe.FullScript;
        Assert.Contains("var pressure 5.00", full);
        Assert.DoesNotContain("var pressure 2.00", full);
        Assert.Contains("var flow 5", full);
    }

    [Fact]
    public void ApplyParameterOverrides_IsCaseInsensitive_OnParamName()
    {
        var recipe = new RecipeFile
        {
            Parameters =
            [
                new ParamGroup
                {
                    Name = "G",
                    Params = [new RecipeParam { Name = "Pressure", Value = "2.00" }]
                }
            ]
        };

        recipe.ApplyParameterOverrides(new Dictionary<string, string>
        {
            ["PRESSURE"] = "9.99"
        });

        Assert.Contains("var Pressure 9.99", recipe.GenerateVarBlock());
    }

    [Fact]
    public void ApplyParameterOverrides_IgnoresUnknownKeys_AndLeavesUnmatchedDefaults()
    {
        var recipe = new RecipeFile
        {
            Parameters =
            [
                new ParamGroup
                {
                    Name = "G",
                    Params = [new RecipeParam { Name = "x", Value = "1" }]
                }
            ]
        };

        recipe.ApplyParameterOverrides(new Dictionary<string, string>
        {
            ["nonexistent"] = "42"
        });

        Assert.Contains("var x 1", recipe.GenerateVarBlock());
        Assert.DoesNotContain("42", recipe.GenerateVarBlock());
    }
}
