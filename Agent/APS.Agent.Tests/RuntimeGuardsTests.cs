using System.Text.Json;
using APS.Agent.Core.Guards;
using APS.Agent.Core.Schemas;

namespace APS.Agent.Tests;

public sealed class RuntimeGuardsTests
{
    private static IReadOnlyList<ToolDeclaration> AllTools()
    {
        var emptySchema = JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement;
        string[] names =
        [
            "describe_machine", "describe_hardware", "validate_recipe", "watch_recipe",
            "upload_recipe", "start_recipe", "abort_recipe", "resolve_pause", "device_command",
        ];
        return names.Select(n => new ToolDeclaration(n, n, emptySchema)).ToList();
    }

    [Fact]
    public void Copilot_mode_removes_all_five_mutating_tools_and_blocks_direct_call()
    {
        var exposed = ToolPolicy.FilterDeclarations(AgentMode.Copilot, AllTools(), sessionTier: "Manual", allowDirectDeviceCommand: true, benchmarkAllowedTools: null);
        var names = exposed.Select(t => t.Name).ToHashSet();

        Assert.Contains("describe_machine", names);
        Assert.Contains("validate_recipe", names);
        Assert.Contains("watch_recipe", names);
        Assert.DoesNotContain("upload_recipe", names);
        Assert.DoesNotContain("start_recipe", names);
        Assert.DoesNotContain("abort_recipe", names);
        Assert.DoesNotContain("resolve_pause", names);
        Assert.DoesNotContain("device_command", names);

        var decision = ToolBatchGuard.Evaluate(
            AgentMode.Copilot,
            [new PendingToolCall("c1", "start_recipe", "{}")],
            names);
        Assert.False(decision.Allowed);
        Assert.False(decision.AbortTrial);
        Assert.Equal("mutating_tool_blocked_in_copilot_mode", decision.DenialCategory);
    }

    [Fact]
    public void Autonomous_mode_exposes_device_command_only_when_Manual_tier_and_opted_in()
    {
        var recipeTier = ToolPolicy.FilterDeclarations(AgentMode.Autonomous, AllTools(), "Recipe", allowDirectDeviceCommand: true, null)
            .Select(t => t.Name).ToHashSet();
        Assert.Contains("start_recipe", recipeTier);
        Assert.DoesNotContain("device_command", recipeTier);

        var manualNotOpted = ToolPolicy.FilterDeclarations(AgentMode.Autonomous, AllTools(), "Manual", allowDirectDeviceCommand: false, null)
            .Select(t => t.Name).ToHashSet();
        Assert.DoesNotContain("device_command", manualNotOpted);

        var manualOpted = ToolPolicy.FilterDeclarations(AgentMode.Autonomous, AllTools(), "Manual", allowDirectDeviceCommand: true, null)
            .Select(t => t.Name).ToHashSet();
        Assert.Contains("device_command", manualOpted);
    }

    [Fact]
    public void Benchmark_mode_preserves_exact_allowed_tools_without_client_removal()
    {
        var allow = new HashSet<string> { "describe_machine", "start_recipe", "device_command" };
        var exposed = ToolPolicy.FilterDeclarations(AgentMode.Benchmark, AllTools(), "Manual", allowDirectDeviceCommand: false, allow)
            .Select(t => t.Name).ToHashSet();
        Assert.Equal(allow, exposed);
    }

    [Fact]
    public void Batch_guard_rejects_parallel_calls_when_any_is_mutating()
    {
        var allowed = AllTools().Select(t => t.Name).ToHashSet();

        var readsOk = ToolBatchGuard.Evaluate(
            AgentMode.Benchmark,
            [new PendingToolCall("c1", "describe_machine", "{}"), new PendingToolCall("c2", "describe_hardware", "{}")],
            allowed);
        Assert.True(readsOk.Allowed);

        var autoMixed = ToolBatchGuard.Evaluate(
            AgentMode.Autonomous,
            [new PendingToolCall("c1", "describe_machine", "{}"), new PendingToolCall("c2", "start_recipe", "{}")],
            allowed);
        Assert.False(autoMixed.Allowed);
        Assert.False(autoMixed.AbortTrial);
        Assert.Equal("assistant_batch_denied", autoMixed.DenialCategory);

        var benchMut = ToolBatchGuard.Evaluate(
            AgentMode.Benchmark,
            [new PendingToolCall("c1", "upload_recipe", "{}"), new PendingToolCall("c2", "start_recipe", "{}")],
            allowed);
        Assert.False(benchMut.Allowed);
        Assert.True(benchMut.AbortTrial);
        Assert.Equal("assistant_batch_denied", benchMut.DenialCategory);
    }

    [Fact]
    public void Upload_gate_normalizes_newlines_and_requires_valid_true_receipt()
    {
        var gate = new ValidatedUploadGate();
        string rcpxLf = "<Recipe>\n  <Script>valve V1 open</Script>\n</Recipe>\n";
        string rcpxCrLf = "<Recipe>\r\n  <Script>valve V1 open</Script>\r\n</Recipe>   ";

        var deniedBefore = gate.CanUpload(rcpxLf, requireValidatedUpload: true);
        Assert.False(deniedBefore.Allowed);
        Assert.Equal("validation_required_before_upload", deniedBefore.ErrorCode);

        gate.RecordValidationResult(rcpxLf, valid: false);
        Assert.False(gate.CanUpload(rcpxLf, requireValidatedUpload: true).Allowed);

        gate.RecordValidationResult(rcpxLf, valid: true);
        Assert.True(gate.CanUpload(rcpxCrLf, requireValidatedUpload: true).Allowed);
        Assert.False(gate.CanUpload(rcpxLf + "extra", requireValidatedUpload: true).Allowed);

        Assert.True(gate.CanUpload("<Recipe>unvalidated</Recipe>", requireValidatedUpload: false).Allowed);
    }
}
