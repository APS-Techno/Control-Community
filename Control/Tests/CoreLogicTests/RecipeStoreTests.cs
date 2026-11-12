using APS.SystemControl.Core.Remote.Agent;
using Xunit;

namespace CoreLogicTests;

public class RecipeStoreTests : IDisposable
{
    private readonly string _dir =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"rcp_{Guid.NewGuid():N}")).FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Finds_recipe_by_name_case_insensitive_and_recursive()
    {
        string sub = Directory.CreateDirectory(Path.Combine(_dir, "sub")).FullName;
        string path = Path.Combine(sub, "Cleaning.rcpx");
        File.WriteAllText(path, "<Recipe/>");
        Assert.Equal(path, RecipeStore.FindRecipePath(_dir, "cleaning"));
        Assert.Null(RecipeStore.FindRecipePath(_dir, "Missing"));
    }

    [Theory]
    [InlineData("Deposition test-01", true)]
    [InlineData("W_B4C.stack", true)]
    [InlineData("..\\escape", false)]
    [InlineData("a/b", false)]
    [InlineData("", false)]
    [InlineData("CON", false)]
    [InlineData("com1", false)]
    [InlineData("NUL", false)]
    [InlineData("ok.name", true)]
    [InlineData("trailing.", false)]
    [InlineData("trailing ", false)]
    [InlineData("CONTROL", true)]
    [InlineData("communication", true)]
    public void Recipe_name_validation(string name, bool valid) =>
        Assert.Equal(valid, RecipeStore.IsValidRecipeName(name));

    [Fact]
    public void Save_path_is_rooted_in_recipes_dir()
    {
        Assert.Equal(Path.Combine(_dir, "New.rcpx"), RecipeStore.SavePathFor(_dir, "New"));
    }

    [Fact]
    public void Scope_dir_is_the_key_folder_under_recipes_dir_or_the_root_without_one()
    {
        Assert.Equal(Path.Combine(_dir, "Agent-Paper2"), RecipeStore.ScopeDir(_dir, "Agent-Paper2"));
        Assert.Equal(_dir, RecipeStore.ScopeDir(_dir, null));
    }

    [Fact]
    public void Listing_a_key_folder_excludes_the_root_and_other_folders()
    {
        void Put(string rel) {
            string p = Path.Combine(_dir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, "<Recipe/>");
        }
        Put("Root.rcpx");
        Put(Path.Combine("Other", "Theirs.rcpx"));
        Put(Path.Combine("Agent", "Mine.rcpx"));
        Put(Path.Combine("Agent", "deep", "MineToo.rcpx"));

        static string[] Names(IEnumerable<string> files) =>
            files.Select(Path.GetFileNameWithoutExtension).Order().ToArray()!;

        Assert.Equal(["Mine", "MineToo"], Names(RecipeStore.ListRecipeFiles(_dir, "Agent")));
        Assert.Equal(["Mine", "MineToo", "Root", "Theirs"], Names(RecipeStore.ListRecipeFiles(_dir, null)));
        Assert.Empty(RecipeStore.ListRecipeFiles(_dir, "NotCreatedYet"));
    }

    private string Put(string rel)
    {
        string p = Path.Combine(_dir, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, "<Recipe/>");
        return p;
    }

    [Fact]
    public void New_recipe_is_saved_in_the_key_folder_or_the_root()
    {
        Assert.Equal(Path.Combine(_dir, "New.rcpx"), RecipeStore.ResolveSavePath(_dir, "New", false, null));
        Assert.Equal(Path.Combine(_dir, "Agent", "New.rcpx"), RecipeStore.ResolveSavePath(_dir, "New", false, "Agent"));
    }

    [Fact]
    public void Existing_name_anywhere_is_refused_without_overwrite()
    {
        Put(Path.Combine("Other", "Cleaning.rcpx"));
        Assert.Equal(AgentErrors.AlreadyExists,
            Assert.Throws<AgentOpException>(() => RecipeStore.ResolveSavePath(_dir, "cleaning", false, null)).Code);
        Assert.Equal(AgentErrors.AlreadyExists,
            Assert.Throws<AgentOpException>(() => RecipeStore.ResolveSavePath(_dir, "cleaning", false, "Agent")).Code);
    }

    // A scoped key must never shadow a recipe outside its folder: recipes are started and listed by
    // name alone, so a same-named copy in the key's folder would be ambiguous with the lab's own.
    [Fact]
    public void Scoped_key_cannot_overwrite_or_shadow_a_recipe_outside_its_folder()
    {
        Put(Path.Combine("Other", "Cleaning.rcpx"));
        var ex = Assert.Throws<AgentOpException>(() => RecipeStore.ResolveSavePath(_dir, "Cleaning", true, "Agent"));
        Assert.Equal(AgentErrors.AlreadyExists, ex.Code);
    }

    // Every same-named file counts, not the first one the enumeration happens to return.
    [Fact]
    public void Scoped_key_is_refused_when_the_name_exists_inside_and_outside_its_folder()
    {
        Put(Path.Combine("Agent", "Cleaning.rcpx"));
        Put(Path.Combine("Other", "Cleaning.rcpx"));
        Assert.Equal(AgentErrors.AlreadyExists,
            Assert.Throws<AgentOpException>(() => RecipeStore.ResolveSavePath(_dir, "Cleaning", true, "Agent")).Code);
    }

    [Fact]
    public void Sibling_folder_sharing_the_prefix_is_outside_the_key_folder()
    {
        Put(Path.Combine("Agent2", "X.rcpx"));
        Assert.Equal(AgentErrors.AlreadyExists,
            Assert.Throws<AgentOpException>(() => RecipeStore.ResolveSavePath(_dir, "X", true, "Agent")).Code);
    }

    [Fact]
    public void Scoped_key_with_two_copies_in_its_folder_is_refused_as_ambiguous()
    {
        Put(Path.Combine("Agent", "X.rcpx"));
        Put(Path.Combine("Agent", "deep", "X.rcpx"));
        Assert.Equal(AgentErrors.AlreadyExists,
            Assert.Throws<AgentOpException>(() => RecipeStore.ResolveSavePath(_dir, "X", true, "Agent")).Code);
    }

    [Fact]
    public void Scoped_key_without_overwrite_is_refused_for_its_own_file()
    {
        Put(Path.Combine("Agent", "X.rcpx"));
        Assert.Equal(AgentErrors.AlreadyExists,
            Assert.Throws<AgentOpException>(() => RecipeStore.ResolveSavePath(_dir, "X", false, "Agent")).Code);
    }

    // recipe.start resolves an embedded recipe before any file, so a same-named file would be listed but never run.
    [Fact]
    public void Scoped_key_cannot_take_an_embedded_recipe_name()
    {
        Assert.Equal(AgentErrors.AlreadyExists,
            Assert.Throws<AgentOpException>(() =>
                RecipeStore.ResolveSavePath(_dir, "SYSTEM_PUMP_OUT", false, "Agent", n => n == "SYSTEM_PUMP_OUT")).Code);
    }

    [Fact]
    public void Scoped_overwrite_replaces_its_own_file_where_it_is()
    {
        string mine = Put(Path.Combine("Agent", "deep", "P2-01.rcpx"));
        Assert.Equal(mine, RecipeStore.ResolveSavePath(_dir, "P2-01", true, "Agent"));
    }

    [Fact]
    public void Unscoped_overwrite_keeps_saving_at_the_root()
    {
        Put(Path.Combine("sub", "Cleaning.rcpx"));
        Assert.Equal(Path.Combine(_dir, "Cleaning.rcpx"), RecipeStore.ResolveSavePath(_dir, "Cleaning", true, null));
    }

    [Fact]
    public void Save_writes_the_content_at_the_resolved_path_creating_the_folder()
    {
        string path = RecipeStore.Save(_dir, "P2-01", "<Recipe a='1'/>", false, "Agent");
        Assert.Equal(Path.Combine(_dir, "Agent", "P2-01.rcpx"), path);
        Assert.Equal("<Recipe a='1'/>", File.ReadAllText(path));
        RecipeStore.Save(_dir, "P2-01", "<Recipe a='2'/>", true, "Agent");
        Assert.Equal("<Recipe a='2'/>", File.ReadAllText(path));
    }

    [Fact]
    public void Concurrent_saves_of_one_new_name_create_exactly_one_file()
    {
        int saved = 0, refused = 0;
        Parallel.For(0, 16, i =>
        {
            try
            {
                RecipeStore.Save(_dir, "Race", $"<Recipe n='{i}'/>", false, i % 2 == 0 ? "Agent" : null);
                Interlocked.Increment(ref saved);
            }
            catch (AgentOpException ex) when (ex.Code == AgentErrors.AlreadyExists)
            {
                Interlocked.Increment(ref refused);
            }
        });
        Assert.Equal(1, saved);
        Assert.Equal(15, refused);
        Assert.Single(Directory.GetFiles(_dir, "Race.rcpx", SearchOption.AllDirectories));
    }

    [Fact]
    public void Save_without_overwrite_never_truncates_an_existing_file()
    {
        string mine = RecipeStore.Save(_dir, "Late", "<Recipe a='1'/>", false, null);
        Assert.Equal(AgentErrors.AlreadyExists,
            Assert.Throws<AgentOpException>(() => RecipeStore.Save(_dir, "Late", "<Recipe a='2'/>", false, null)).Code);
        Assert.Equal("<Recipe a='1'/>", File.ReadAllText(mine));
    }

    [Fact]
    public void Embedded_name_check_ignores_case()
    {
        Assert.True(RecipeStore.IsEmbeddedName(["SYSTEM_PUMP_OUT", "CLEANING"], "system_pump_out"));
        Assert.False(RecipeStore.IsEmbeddedName(["SYSTEM_PUMP_OUT"], "P2-01"));
        Assert.False(RecipeStore.IsEmbeddedName(null, "SYSTEM_PUMP_OUT"));
    }

    // The open itself, past the lookup: a file that exists when the upload is not replacing must be
    // refused and left intact (CreateNew), never truncated (Create).
    [Fact]
    public void Opening_a_new_upload_over_a_file_that_appeared_refuses_and_keeps_it()
    {
        string late = Put(Path.Combine("Agent", "Late.rcpx"));
        var ex = Assert.Throws<AgentOpException>(() => RecipeStore.OpenForWrite(late, replace: false, "Late").Dispose());
        Assert.Equal(AgentErrors.AlreadyExists, ex.Code);
        Assert.Equal("<Recipe/>", File.ReadAllText(late));
    }
}
