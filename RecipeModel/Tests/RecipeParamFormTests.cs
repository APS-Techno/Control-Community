using APS.RecipeModel.Controls;
using APS.RecipeModel.Model;

namespace RecipeModel.Tests;

public class RecipeParamFormTests
{
    [StaFact]
    public void LoadRecipe_CreatesGroupBoxesAndEdits()
    {
        var form = new RecipeParamForm();
        var recipe = new RecipeFile
        {
            Description = new RecipeDescription { Title = "Test" },
            Parameters =
            [
                new ParamGroup
                {
                    Name = "Group1",
                    Params =
                    [
                        new RecipeParam { Name = "A", Caption = "Alpha", Unit = "V", Value = "10" },
                        new RecipeParam { Name = "B", Caption = "Beta", Unit = "W", Value = "20" }
                    ]
                }
            ]
        };

        form.LoadRecipe(recipe);

        var values = form.GetParameterValues();
        Assert.Equal(2, values.Count);
        Assert.Equal("10", values["A"]);
        Assert.Equal("20", values["B"]);
    }

    [StaFact]
    public void GetParameterValues_ReturnsEmptyWhenNoRecipe()
    {
        var form = new RecipeParamForm();
        var values = form.GetParameterValues();
        Assert.Empty(values);
    }

    [StaFact]
    public void GetParameterValues_AppendsPressureUnitSuffix()
    {
        var form = new RecipeParamForm();
        var recipe = new RecipeFile
        {
            Description = new RecipeDescription { Title = "T" },
            Parameters =
            [
                new ParamGroup
                {
                    Name = "G",
                    Params =
                    [
                        new RecipeParam { Name = "P_mt",   Unit = "mTorr", Value = "2.00" },
                        new RecipeParam { Name = "P_t",    Unit = "Torr",  Value = "0.05" },
                        new RecipeParam { Name = "P_ut",   Unit = "uTorr", Value = "1" },
                        new RecipeParam { Name = "P_nt",   Unit = "nTorr", Value = "100" },
                        new RecipeParam { Name = "Power",  Unit = "W",     Value = "350" },
                    ],
                }
            ],
        };
        form.LoadRecipe(recipe);

        var v = form.GetParameterValues();
        Assert.Equal("2.00mT",  v["P_mt"]);
        Assert.Equal("0.05",    v["P_t"]);       // "Torr" is canonical → left bare (bare = Torr)
        Assert.Equal("1uT",     v["P_ut"]);
        Assert.Equal("100nT",   v["P_nt"]);
        Assert.Equal("350",     v["Power"]);
    }

    [StaFact]
    public void GetParameterValues_AppendsTimeUnitSuffix()
    {
        var form = new RecipeParamForm();
        var recipe = new RecipeFile
        {
            Description = new RecipeDescription { Title = "T" },
            Parameters =
            [
                new ParamGroup
                {
                    Name = "G",
                    Params =
                    [
                        new RecipeParam { Name = "WarmUpTime", Unit = "min",   Value = "30" },
                        new RecipeParam { Name = "SoakTime",   Unit = "hour",  Value = "1.5" },
                        new RecipeParam { Name = "BiasDelay",  Unit = "sec",   Value = "0" },
                        new RecipeParam { Name = "Height",     Unit = "mm",    Value = "10" },
                    ],
                }
            ],
        };
        form.LoadRecipe(recipe);

        var v = form.GetParameterValues();
        Assert.Equal("30m",   v["WarmUpTime"]); // "min" → minutes suffix (non-canonical: bare would be seconds)
        Assert.Equal("1.5h",  v["SoakTime"]);
        Assert.Equal("0",     v["BiasDelay"]);   // "sec" is canonical → left bare (bare = seconds);
                                                 // suffixing "0s" broke bias delay's float.Parse
        Assert.Equal("10",    v["Height"]);
    }

    [StaFact]
    public void GetParameterValues_DoesNotDoubleSuffixIfOperatorTypedOne()
    {
        var form = new RecipeParamForm();
        var recipe = new RecipeFile
        {
            Description = new RecipeDescription { Title = "T" },
            Parameters =
            [
                new ParamGroup
                {
                    Name = "G",
                    Params =
                    [
                        new RecipeParam { Name = "P", Unit = "mTorr", Value = "5mT" },
                        new RecipeParam { Name = "W", Unit = "min",   Value = "5m" },
                    ],
                }
            ],
        };
        form.LoadRecipe(recipe);

        var v = form.GetParameterValues();
        Assert.Equal("5mT", v["P"]);
        Assert.Equal("5m",  v["W"]);
    }

    [StaFact]
    public void LoadRecipe_SetsTitle()
    {
        var form = new RecipeParamForm();
        var recipe = new RecipeFile
        {
            Description = new RecipeDescription { Title = "My Recipe" },
            Parameters = []
        };

        form.LoadRecipe(recipe);
        Assert.Equal(recipe, form.Recipe);
    }

    /// <summary>
    /// Regression: after LoadRecipe, the rendered GroupBoxes must be in the
    /// live Controls tree (parent chain reaches the form). DarkScrollPanel
    /// reparents children into ContentPanel — if Clear() drops ContentPanel
    /// itself, GroupBoxes get parented to a detached panel and never paint.
    /// </summary>
    [StaFact]
    public void LoadRecipe_GroupBoxesAttachedToFormControlTree()
    {
        var form = new RecipeParamForm();
        var recipe = new RecipeFile
        {
            Description = new RecipeDescription { Title = "T" },
            Parameters =
            [
                new ParamGroup
                {
                    Name = "G",
                    Params = [new RecipeParam { Name = "a", Value = "1" }]
                }
            ]
        };

        form.LoadRecipe(recipe);

        var groupBoxes = AllDescendants(form).OfType<GroupBox>().ToList();
        Assert.Single(groupBoxes);
        Assert.Same(form, RootOf(groupBoxes[0]));
    }

    [StaFact]
    public void LoadRecipe_TwiceClearsPreviousGroupBoxes()
    {
        var form = new RecipeParamForm();
        RecipeFile Build(string groupName) => new()
        {
            Description = new RecipeDescription { Title = "T" },
            Parameters =
            [
                new ParamGroup
                {
                    Name = groupName,
                    Params = [new RecipeParam { Name = "a", Value = "1" }]
                }
            ]
        };

        form.LoadRecipe(Build("First"));
        form.LoadRecipe(Build("Second"));

        var groupBoxes = AllDescendants(form).OfType<GroupBox>().ToList();
        Assert.Single(groupBoxes);
        Assert.Equal("Second", groupBoxes[0].Text);
    }

    private static IEnumerable<Control> AllDescendants(Control root)
    {
        foreach (Control c in root.Controls)
        {
            yield return c;
            foreach (var d in AllDescendants(c)) yield return d;
        }
    }

    private static Control RootOf(Control c)
    {
        while (c.Parent != null) c = c.Parent;
        return c;
    }

    [StaFact]
    public void LoadRecipe_MultipleGroups_AllParamsAccessible()
    {
        var form = new RecipeParamForm();
        var recipe = new RecipeFile
        {
            Parameters =
            [
                new ParamGroup
                {
                    Name = "G1",
                    Params = [new RecipeParam { Name = "x", Value = "1" }]
                },
                new ParamGroup
                {
                    Name = "G2",
                    Params = [new RecipeParam { Name = "y", Value = "2" }]
                }
            ]
        };

        form.LoadRecipe(recipe);
        var values = form.GetParameterValues();
        Assert.Equal("1", values["x"]);
        Assert.Equal("2", values["y"]);
    }
}
