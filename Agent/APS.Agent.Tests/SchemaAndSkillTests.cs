using System.Text.Json;
using APS.Agent.Core.Schemas;
using APS.Agent.Core.Serialization;
using APS.Agent.Core.Skills;

namespace APS.Agent.Tests;

public sealed class SchemaAndSkillTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"aps-skill-test-{Guid.NewGuid():N}");

    public SchemaAndSkillTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void StrictJson_rejects_duplicate_keys_and_non_finite_numbers()
    {
        Assert.ThrowsAny<JsonException>(() => StrictJson.Parse("""{"a":1,"a":2}"""));
        Assert.ThrowsAny<JsonException>(() => StrictJson.Parse("""{"nested":{"k":1,"k":2}}"""));
        Assert.ThrowsAny<JsonException>(() => StrictJson.Parse("""[1.0, NaN]"""));
        Assert.ThrowsAny<JsonException>(() => StrictJson.Parse("""[1.0, Infinity]"""));

        using var doc = StrictJson.Parse("""{"ok":true,"arr":[1,2.5]}""");
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void ToolSchemaValidator_rejects_external_ref_and_external_id()
    {
        var extRef = new ToolDeclaration(
            "bad_ref",
            "Bad",
            JsonDocument.Parse("""{"type":"object","properties":{"x":{"$ref":"https://example.com/s.json"}}}""").RootElement);
        Assert.Throws<InvalidOperationException>(() => ToolSchemaValidator.ValidateToolSchemas([extRef]));

        var extId = new ToolDeclaration(
            "bad_id",
            "Bad",
            JsonDocument.Parse("""{"type":"object","$id":"http://example.com/schema","properties":{}}""").RootElement);
        Assert.Throws<InvalidOperationException>(() => ToolSchemaValidator.ValidateToolSchemas([extId]));

        var localRef = new ToolDeclaration(
            "good_tool",
            "Good",
            JsonDocument.Parse("""{"type":"object","$defs":{"n":{"type":"string"}},"properties":{"x":{"$ref":"#/$defs/n"}},"required":["x"]}""").RootElement);
        ToolSchemaValidator.ValidateToolSchemas([localRef]);

        var okArgs = JsonDocument.Parse("""{"x":"hello"}""").RootElement;
        var clean = ToolSchemaValidator.ValidateArguments(localRef, okArgs);
        Assert.Equal("hello", clean.GetProperty("x").GetString());

        var missingArgs = JsonDocument.Parse("""{}""").RootElement;
        Assert.Throws<ArgumentException>(() => ToolSchemaValidator.ValidateArguments(localRef, missingArgs));
    }

    [Fact]
    public void SkillBundleLoader_joins_skill_and_sorted_references_with_deterministic_digest()
    {
        string refsDir = Path.Combine(_dir, "references");
        Directory.CreateDirectory(refsDir);
        File.WriteAllText(Path.Combine(_dir, "SKILL.md"), "# Operator Skill\nMain rules.");
        File.WriteAllText(Path.Combine(refsDir, "b.md"), "## B");
        File.WriteAllText(Path.Combine(refsDir, "a.md"), "## A");

        var bundle = SkillBundleLoader.Load(_dir);
        Assert.Equal(["SKILL.md", "references/a.md", "references/b.md"], bundle.RelativeFiles);
        int idxMain = bundle.CombinedPromptText.IndexOf("# Operator Skill", StringComparison.Ordinal);
        int idxA = bundle.CombinedPromptText.IndexOf("## A", StringComparison.Ordinal);
        int idxB = bundle.CombinedPromptText.IndexOf("## B", StringComparison.Ordinal);
        Assert.True(idxMain >= 0 && idxA > idxMain && idxB > idxA);
        Assert.Equal(64, bundle.SkillDigestSha256.Length);
        Assert.Equal(bundle.SkillDigestSha256, SkillBundleLoader.Load(_dir).SkillDigestSha256);
    }

    [Fact]
    public void SandboxedSkillReadTool_blocks_traversal_unc_and_symlinks()
    {
        string refsDir = Path.Combine(_dir, "references");
        Directory.CreateDirectory(refsDir);
        File.WriteAllText(Path.Combine(_dir, "SKILL.md"), "SKILL CONTENT");
        File.WriteAllText(Path.Combine(refsDir, "dsl-and-safety.md"), "DSL CONTENT");
        string outside = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}.txt");
        File.WriteAllText(outside, "SECRET");

        try
        {
            var tool = new SandboxedSkillReadTool(_dir);
            Assert.False(tool.Read("SKILL.md").IsError);
            Assert.Equal("SKILL CONTENT", tool.Read("SKILL.md").Text);
            Assert.False(tool.Read("aps-operator/references/dsl-and-safety.md").IsError);
            Assert.True(tool.Read("../outside.txt").IsError);
            Assert.True(tool.Read(@"\\server\share\file.txt").IsError);

            string linkPath = Path.Combine(_dir, "link.txt");
            bool linkCreated = false;
            try
            {
                File.CreateSymbolicLink(linkPath, outside);
                linkCreated = true;
            }
            catch
            {
            }
            if (linkCreated)
                Assert.True(tool.Read("link.txt").IsError);
        }
        finally
        {
            try { File.Delete(outside); } catch { }
        }
    }
}
