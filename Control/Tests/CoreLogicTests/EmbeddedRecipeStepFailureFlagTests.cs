using APS.SystemControl.Core;
using APS.SystemControl.Core.CoreClass;
using Xunit;

namespace CoreLogicTests;

/// <summary>
/// Parsing of the per-recipe step-failure policy, in isolation from any shipped
/// config. Strict unless declared: only the exact value "continue" opts a recipe
/// into continue-on-error, so no existing or future recipe can become tolerant
/// by accident.
///
/// A typo must not be fatal. Refusing to load EmbedRecipes.xml over one bad
/// attribute would take SYSTEM_AIR_RELEASE and FAILURE_PROTOCOL down with it,
/// leaving the operator with no way to vent or make safe. It resolves to the
/// strict default and is reported instead.
/// </summary>
public class EmbeddedRecipeStepFailureFlagTests
{
    private const string Xml = """
        <?xml version="1.0" encoding="utf-8"?>
        <recipes>
          <recipe id="TOLERANT" onStepFailure="continue">
            <l>valve A1 open</l>
          </recipe>
          <recipe id="TOLERANT_MIXED_CASE" onStepFailure="Continue">
            <l>valve A1 open</l>
          </recipe>
          <recipe id="STRICT_EXPLICIT" onStepFailure="abort">
            <l>valve A1 open</l>
          </recipe>
          <recipe id="STRICT_BY_DEFAULT">
            <l>valve A1 open</l>
          </recipe>
          <recipe id="TYPO" onStepFailure="yes">
            <l>valve A1 open</l>
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

    [Theory]
    [InlineData("TOLERANT", true)]
    [InlineData("TOLERANT_MIXED_CASE", true)]
    [InlineData("STRICT_EXPLICIT", false)]
    [InlineData("STRICT_BY_DEFAULT", false)]
    [InlineData("TYPO", false)]              // unrecognised resolves to the strict default
    [InlineData("NO_SUCH_RECIPE", false)]
    public void Parses_the_step_failure_policy(string id, bool expected)
        => Assert.Equal(expected, WithLoaded(r => r.ContinueOnStepFailure(id)));

    [Fact]
    public void Recipe_lines_still_load_alongside_the_attribute()
        => Assert.Equal(["valve A1 open"], WithLoaded(r => r.Recipe("TOLERANT")));

    [Fact]
    public void Unrecognised_value_is_reported_as_a_config_error()
    {
        var errors = WithLoaded(r => r.ConfigErrors.ToList());

        string error = Assert.Single(errors);
        Assert.Contains("TYPO", error);
        Assert.Contains("yes", error);
        Assert.Contains("abort", error);   // says what it fell back to
    }

    [Fact]
    public void A_typo_does_not_prevent_the_other_recipes_from_loading()
    {
        var names = WithLoaded(r => r.GetKnownNames().ToList());
        Assert.Equal(5, names.Count);
    }

    /// <summary>
    /// A recorded config error is worthless if it stays inside the loader. It
    /// must reach the operator event log at startup, next to the "Embedded
    /// recipes loaded from …" line.
    /// </summary>
    [Fact]
    public void Config_errors_are_logged_when_Core_loads_the_recipes()
    {
        string path = Path.Combine(Path.GetTempPath(), $"aps-embed-{Guid.NewGuid():N}.xml");
        File.WriteAllText(path, Xml);
        try
        {
            var pool = new ChannelPool();
            var thread = new CoreThread(pool);

            Assert.True(thread.Core.LoadEmbeddedRecipes(path));

            var log = new List<string>();
            while (pool.GUIReader!.TryRead(out var msg))
                if (msg is ValMsg<string> { Value: not null } v) log.Add(v.Value);

            Assert.Contains(log, l => l.Contains("TYPO") && l.Contains("onStepFailure"));
        }
        finally { File.Delete(path); }
    }
}
