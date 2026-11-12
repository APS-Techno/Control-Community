using APS.RecipeModel.Controls.ScriptEditor;
using Xunit;

namespace APS.RecipeModel.Tests.Controls;

public class ScriptEditorControlTests
{
    [StaFact]
    public void Text_Set_RoundTrips()
    {
        using var ctrl = new ScriptEditorControl();
        ctrl.Text = "valve M1 open";
        Assert.Equal("valve M1 open", ctrl.Text);
    }

    [StaFact]
    public void InnerEditor_IsRichTextBox()
    {
        using var ctrl = new ScriptEditorControl();
        Assert.IsAssignableFrom<RichTextBox>(ctrl.InnerEditor);
    }

    [StaFact]
    public void TextChanged_Fires_WhenTextSet()
    {
        using var ctrl = new ScriptEditorControl();
        int count = 0;
        ctrl.TextChanged += (_, _) => count++;
        ctrl.Text = "valve M1 open";
        Assert.True(count > 0, "TextChanged should fire when Text changes");
    }

    [StaFact]
    public void Gutter_Width_GrowsWithLineCount()
    {
        using var ctrl = new ScriptEditorControl();
        ctrl.Show();          // force handle creation so Lines is populated
        ctrl.Text = "a";
        int w1 = ctrl.GutterWidthForTest;
        ctrl.Text = string.Join("\n", Enumerable.Range(0, 150).Select(i => $"line {i}"));
        int w2 = ctrl.GutterWidthForTest;
        Assert.True(w2 > w1, $"gutter should widen for 3-digit line counts (was {w1}, now {w2})");
    }

    [StaFact]
    public void Highlighter_ColorsKeyword()
    {
        using var ctrl = new ScriptEditorControl();
        ctrl.Show();
        ctrl.Text = "valve M1 open";

        // Force any pending debounced highlight to run synchronously for the test.
        ctrl.ApplyHighlightingNow();

        var inner = ctrl.InnerEditor;
        inner.Select(0, "valve".Length);
        Assert.NotEqual(inner.ForeColor, inner.SelectionColor);
    }

    [StaFact]
    public void Zoom_CtrlPlus_IncreasesFontSize()
    {
        using var ctrl = new ScriptEditorControl();
        ctrl.Show();
        float before = ctrl.InnerEditor.Font.Size;
        ctrl.StepFontSizeForTest(+1);
        Assert.True(ctrl.InnerEditor.Font.Size > before);
    }

    [StaFact]
    public void Zoom_ClampsAtMax()
    {
        using var ctrl = new ScriptEditorControl();
        ctrl.Show();
        for (int i = 0; i < 100; i++) ctrl.StepFontSizeForTest(+1);
        Assert.Equal(EditorFontSizeHelper.Max, ctrl.InnerEditor.Font.Size);
    }

    [StaFact]
    public void SetDevices_PopulatesAutocompleteData()
    {
        using var ctrl = new ScriptEditorControl();
        ctrl.Show();
        ctrl.SetDevices(
            all: new[] { "M1", "M2", "Ar1" },
            byCategory: new Dictionary<string, IReadOnlyList<string>>
            {
                ["Valves"] = new[] { "M1", "M2" },
                ["MFCs"]   = new[] { "Ar1" },
            });

        Assert.Contains("M1", ctrl.KnownDevicesForTest);
        Assert.Contains("Ar1", ctrl.KnownDevicesForTest);
    }

    [StaFact]
    public void SetEmbeddedRecipes_ExposesNames()
    {
        using var ctrl = new ScriptEditorControl();
        ctrl.Show();
        ctrl.SetEmbeddedRecipes(new[] { "LOAD", "UNLOAD" });
        Assert.Contains("LOAD", ctrl.KnownEmbeddedRecipesForTest);
    }

    [StaFact]
    public void SetDevices_DoesNotWipe_EmbeddedRecipes()
    {
        using var ctrl = new ScriptEditorControl();
        ctrl.Show();
        ctrl.SetEmbeddedRecipes(new[] { "LOAD", "UNLOAD" });
        ctrl.SetDevices(
            all: new[] { "M1" },
            byCategory: new Dictionary<string, IReadOnlyList<string>>
            {
                ["Valves"] = new[] { "M1" },
            });
        Assert.Contains("LOAD", ctrl.KnownEmbeddedRecipesForTest);
    }

    [StaFact]
    public void SetVariables_StoresProvidedItems()
    {
        using var ctrl = new ScriptEditorControl();
        ctrl.Show();
        var vars = new[] { new VariableItem("temperature", "float", "25.0") };
        ctrl.SetVariables(vars);
        Assert.Contains(ctrl.ExtraVariablesForTest, v => v.Name == "temperature");
    }
}
