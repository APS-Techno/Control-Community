using APS.SystemControl.Core.CoreClass;
using Xunit;

namespace CoreLogicTests;

/// <summary>
/// The operator confirmation shown before a panel button starts an embedded
/// recipe. The text is declared per recipe (confirm="…" in EmbedRecipes.xml) so
/// it can say what that recipe actually does; a recipe without one still asks,
/// with a generic prompt, so no panel button ever starts a recipe on one click.
/// </summary>
public class EmbeddedRecipeConfirmTests
{
    private const string Xml = """
        <?xml version="1.0" encoding="utf-8"?>
        <recipes>
          <recipe id="VENT" onStepFailure="continue" confirm="Vent the main chamber: GVM closed, A1 opened.">
            <l>valve A1 open</l>
          </recipe>
          <recipe id="PLAIN">
            <l>valve A1 close</l>
          </recipe>
          <recipe id="BLANK" confirm="   ">
            <l>valve A1 close</l>
          </recipe>
        </recipes>
        """;

    private static T WithLoaded<T>(Func<EmbeddedRecipes, T> use)
    {
        string path = Path.Combine(Path.GetTempPath(), $"aps-embed-{Guid.NewGuid():N}.xml");
        File.WriteAllText(path, Xml);
        try { return use(new EmbeddedRecipes(path)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Declared_text_is_the_prompt()
        => Assert.Equal("Vent the main chamber: GVM closed, A1 opened.",
            WithLoaded(r => r.ConfirmPrompt("VENT")));

    [Theory]
    [InlineData("PLAIN")]
    [InlineData("BLANK")]
    [InlineData("NO_SUCH_RECIPE")]
    public void Missing_or_blank_text_falls_back_to_a_generic_prompt(string id)
        => Assert.Equal($"Start {id}?", WithLoaded(r => r.ConfirmPrompt(id)));

    [Fact]
    public void Confirm_attribute_does_not_disturb_the_step_failure_policy()
        => Assert.True(WithLoaded(r => r.ContinueOnStepFailure("VENT")));
}
