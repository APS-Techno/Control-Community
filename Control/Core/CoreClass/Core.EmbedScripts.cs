using APS.SystemControl.XML.Core;
using APS.SystemControl.Core.Classes;
using APS.SystemControl.XML;
using System.Linq;

namespace APS.SystemControl.Core.CoreClass;

public class RecipeCollection()
{
    public string? Name { get; set; }
    public string[]? Values { get; set; }

    /// <summary>
    /// True when the recipe declared onStepFailure="continue". See
    /// <see cref="EmbeddedRecipes.ContinueOnStepFailure"/>.
    /// </summary>
    public bool ContinueOnStepFailure { get; set; }

    /// <summary>Declared confirm="…" text, or null. See <see cref="EmbeddedRecipes.ConfirmPrompt"/>.</summary>
    public string? Confirm { get; set; }

    /// <summary>Chamber states declared in notIn="…". See <see cref="EmbeddedRecipes.IsBlockedIn"/>.</summary>
    public IReadOnlySet<SystemState> BlockedIn { get; set; } = new HashSet<SystemState>();
}


public class EmbeddedRecipes
{
    private readonly List<RecipeCollection> recipes;
    private readonly List<string> configErrors = [];

    /// <summary>
    /// Non-fatal problems found while parsing EmbedRecipes.xml. Core logs these
    /// after construction — see Core.LoadEmbeddedRecipes.
    /// </summary>
    public IReadOnlyList<string> ConfigErrors => configErrors;

    /// <summary>
    /// True when the recipe declared onStepFailure="continue": attempt every
    /// step, collect failures, and report INCOMPLETE at the end. False — the
    /// default, and the answer for an unknown id — means abort at the first
    /// failing step.
    /// A teardown's steps each lower the energy in the machine and are mutually
    /// independent, so one failure must not cancel the rest; a deposition or
    /// pump-down must stop dead. That is a property of the recipe's intent,
    /// which only the recipe can declare.
    /// </summary>
    public bool ContinueOnStepFailure(string code) =>
        recipes.Find(s => s.Name == code)?.ContinueOnStepFailure ?? false;

    /// <summary>
    /// The question the operator answers before a panel button starts this recipe:
    /// its declared confirm="…" text, or a generic "Start &lt;id&gt;?" so that every
    /// button still asks. Only the panel path prompts — MQTT, MCP, the Stream Deck
    /// and nested 'call' never do.
    /// </summary>
    public string ConfirmPrompt(string code)
    {
        string? text = recipes.Find(s => s.Name == code)?.Confirm;
        return string.IsNullOrWhiteSpace(text) ? $"Start {code}?" : text.Trim();
    }

    /// <summary>
    /// True when the recipe declared notIn="…" naming <paramref name="state"/>: a panel
    /// button or remote host must not start it while the default chamber is in that state.
    /// False for an unknown id or a recipe without the attribute.
    ///
    /// The single-flight slot only refuses a launch while a recipe RUNS. The chamber can
    /// stay in Processing after a run (gas still flowing keeps it there), and in that
    /// window venting or opening a gate into the chamber must be refused just the same —
    /// only what the profile leaves unlisted (pump-out, load-lock operations) may run.
    /// </summary>
    public bool IsBlockedIn(string code, SystemState state) =>
        recipes.Find(s => s.Name == code)?.BlockedIn.Contains(state) ?? false;

    public string[] Recipe(string code)
    {
        RecipeCollection? recipe = recipes.Find(s => s.Name == code);
        return recipe?.Values ?? [];
    }

    /// <summary>
    /// Distinct from Recipe(name).Length == 0 because some embedded recipes
    /// are intentionally empty placeholders (e.g. UNLOAD_SUBSTRATE).
    /// </summary>
    public bool Contains(string code) => recipes.Any(r => r.Name == code);

    public IEnumerable<string> GetKnownNames() =>
        recipes.Where(r => r.Name != null).Select(r => r.Name!);

    public EmbeddedRecipes(string recipeFile)
    {
        recipes = [];
        LoadRecipes(recipeFile);
    }

    private void LoadRecipes(string recipeFile)
    {
        XML_Recipes xml_recipes = Serializer<XML_Recipes>.Load(recipeFile);
        if (xml_recipes != null)
        {
            foreach (var recipe in xml_recipes.RecipesList!)
            {
                RecipeCollection rcp = new()
                {
                    Name = recipe.Id,
                    Values = [.. recipe.Lines!],
                    ContinueOnStepFailure = ParseStepFailurePolicy(recipe.Id, recipe.OnStepFailure),
                    Confirm = recipe.Confirm,
                    BlockedIn = ParseBlockedStates(recipe.Id, recipe.NotIn)
                };
                recipes.Add(rcp);
            }
        }
    }

    /// <summary>
    /// Parses notIn="…" into the set of blocking states. An unrecognised name is
    /// recorded and skipped rather than throwing, for the same reason as
    /// <see cref="ParseStepFailurePolicy"/>: one typo must not take every recipe in the
    /// file down with it. The config guard (EmbeddedRecipeValidationTests) catches it.
    /// </summary>
    private IReadOnlySet<SystemState> ParseBlockedStates(string? id, string? value)
    {
        var set = new HashSet<SystemState>();
        if (string.IsNullOrWhiteSpace(value)) return set;
        foreach (string token in value.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            SystemState state = ConditionParser.ParseSystemState(token);
            if (state == SystemState.Unknown)
                configErrors.Add(
                    $"Embedded recipe '{id}': notIn=\"{value}\" names unknown state '{token}' " +
                    "(expected startup, idle, maintenance, processing or stopped) — ignoring it.");
            else
                set.Add(state);
        }
        return set;
    }

    /// <summary>
    /// Strict unless declared: absent or "abort" gives false, and only the exact
    /// value "continue" gives true.
    ///
    /// An unrecognised value is recorded and resolves to the strict default
    /// rather than throwing. Refusing to load the file over one typo would take
    /// SYSTEM_AIR_RELEASE and FAILURE_PROTOCOL with it, leaving the operator
    /// unable to vent or make the machine safe. The build-time config guard
    /// (EmbeddedRecipeValidationTests) catches the typo before it ships.
    /// </summary>
    private bool ParseStepFailurePolicy(string? id, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        string v = value.Trim();
        if (v.Equals("continue", StringComparison.OrdinalIgnoreCase)) return true;
        if (v.Equals("abort", StringComparison.OrdinalIgnoreCase)) return false;

        configErrors.Add(
            $"Embedded recipe '{id}': onStepFailure=\"{value}\" is not recognised " +
            "(expected \"continue\" or \"abort\") — treating as \"abort\".");
        return false;
    }
}


