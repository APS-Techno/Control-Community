using System.Collections.Generic;
using System.Linq;
using APS.ProcessPanel.Contracts;
using APS.SystemControl.GUIControls.Process;
using Xunit;

namespace CoreLogicTests;

public class LoopProgressCardVisibilityTests
{
    private static Control Card(Control panel)
    {
        var hits = panel.Controls.Find("LoopProgressCard", searchAllChildren: true);
        Assert.NotEmpty(hits);
        return hits[0];
    }

    private static IEnumerable<T> FindDescendants<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match) yield return match;
            foreach (T nested in FindDescendants<T>(child)) yield return nested;
        }
    }

    private static RecipeInfo Info(int stack, int total, RecipeState state = RecipeState.Running)
        => new("STACK", 3, 20, "deposit", state, TimeSpan.FromSeconds(30), null, stack, total);

    [Fact]
    public void Card_is_hidden_for_a_recipe_without_a_loop()
    {
        using var panel = new BaseProcessPanel();
        panel.UpdateRecipe(Info(0, 0));

        Assert.False(Card(panel).Visible);
    }

    [Fact]
    public void Card_is_visible_while_the_outer_loop_runs()
    {
        using var panel = new BaseProcessPanel();
        panel.UpdateRecipe(Info(2, 6));

        Assert.True(Card(panel).Visible);
    }

    [Fact]
    public void Card_hides_again_once_the_loop_ends()
    {
        using var panel = new BaseProcessPanel();
        panel.UpdateRecipe(Info(6, 6));
        panel.UpdateRecipe(Info(0, 0));     // runtime zeroes the counters in its finally block

        Assert.False(Card(panel).Visible);
    }

    [Fact]
    public void Card_docks_below_the_current_step_card()
    {
        using var panel = new BaseProcessPanel { Size = new Size(1200, 800) };
        panel.UpdateRecipe(Info(2, 6));
        panel.PerformLayout();

        Control loopCard = Card(panel);
        Control stepCard = FindDescendants<CurrentStepCard>(panel).Single();

        Assert.True(loopCard.Top >= stepCard.Bottom,
            $"loop card (Top={loopCard.Top}) must sit below the step card (Bottom={stepCard.Bottom})");
    }
}
