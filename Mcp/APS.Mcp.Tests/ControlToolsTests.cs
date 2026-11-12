using System.Text.Json;
using APS.Mcp;
using APS.Wire;
using Xunit;

public class ControlToolsTests
{
    private sealed class FakeWire : IWireCaller
    {
        public (string Cmd, object? Args)? Last;
        public JsonElement Next = JsonDocument.Parse("""{"okish":true}""").RootElement;
        public Task<JsonElement> CallAsync(string cmd, object? args, CancellationToken ct = default)
        { Last = (cmd, args); return Task.FromResult(Next); }
    }

    private static JsonElement ArgsOf(FakeWire w) =>
        JsonDocument.Parse(JsonSerializer.Serialize(w.Last!.Value.Args,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })).RootElement;

    [Fact]
    public async Task Describe_machine_maps_to_wire_command()
    {
        var wire = new FakeWire();
        string result = await new ControlTools(wire).DescribeMachine();
        Assert.Equal("describe.machine", wire.Last!.Value.Cmd);
        Assert.Contains("okish", result);
    }

    [Fact]
    public async Task Start_recipe_passes_name_and_parsed_parameters()
    {
        var wire = new FakeWire();
        await new ControlTools(wire).StartRecipe("Cleaning", """{"time":"30"}""");
        Assert.Equal("recipe.start", wire.Last!.Value.Cmd);
        var args = ArgsOf(wire);
        Assert.Equal("Cleaning", args.GetProperty("name").GetString());
        Assert.Equal("30", args.GetProperty("parameters").GetProperty("time").GetString());
    }

    [Fact]
    public async Task Device_command_passes_typed_args_array()
    {
        var wire = new FakeWire();
        await new ControlTools(wire).DeviceCommand("Valve", "A1", """[true]""");
        var args = ArgsOf(wire);
        Assert.Equal("Valve", args.GetProperty("type").GetString());
        Assert.True(args.GetProperty("args")[0].GetBoolean());
    }

    [Fact]
    public async Task Invalid_parameters_json_is_a_local_error_not_a_wire_call()
    {
        var wire = new FakeWire();
        await Assert.ThrowsAnyAsync<Exception>(
            () => new ControlTools(wire).StartRecipe("X", "{not json"));
        Assert.Null(wire.Last);
    }

    // A run lasts tens of minutes and the agent has no way to wait between calls.
    // recipe_status(waitSeconds) blocks on the bridge and returns early only when the run ENDS, not on step changes.
    private sealed class SeqWire : IWireCaller
    {
        public readonly Queue<string> Responses = new();
        public int Calls;
        public Task<JsonElement> CallAsync(string cmd, object? args, CancellationToken ct = default)
        {
            Calls++;
            string json = Responses.Count > 1 ? Responses.Dequeue() : Responses.Peek();
            return Task.FromResult(JsonDocument.Parse(json).RootElement);
        }
    }

    private static string Running(int step) =>
        $$"""{"isRecipeRunning":true,"failureProtocolActive":false,"progress":{"currentStep":{{step}},"state":1},"lastOutcome":null}""";
    private const string Ended =
        """{"isRecipeRunning":false,"failureProtocolActive":false,"progress":{"currentStep":22,"state":4},"lastOutcome":{"state":"Complete","at":"2026-09-02T15:21:32"}}""";

    [Fact]
    public async Task Recipe_status_without_wait_is_a_single_call()
    {
        var wire = new SeqWire(); wire.Responses.Enqueue(Running(3));
        var waited = new List<TimeSpan>();
        var tools = new ControlTools(wire, (d, _) => { waited.Add(d); return Task.CompletedTask; });
        string r = await tools.RecipeStatus();
        Assert.Equal(1, wire.Calls);
        Assert.Empty(waited);
        Assert.DoesNotContain("waitedSeconds", r);
    }

    [Fact]
    public async Task Recipe_status_wait_keeps_polling_through_step_changes_until_the_time_is_up()
    {
        var wire = new SeqWire();
        foreach (int step in new[] { 3, 5, 9, 14, 14, 14 }) wire.Responses.Enqueue(Running(step));
        var waited = new List<TimeSpan>();
        var tools = new ControlTools(wire, (d, _) => { waited.Add(d); return Task.CompletedTask; })
            { PollInterval = TimeSpan.FromSeconds(20) };
        string r = await tools.RecipeStatus(waitSeconds: 60);
        // 0 s: first call; 20, 40, 60 s: three more polls, then the budget is spent.
        Assert.Equal(4, wire.Calls);
        Assert.Equal(3, waited.Count);
        Assert.All(waited, d => Assert.Equal(TimeSpan.FromSeconds(20), d));
        var doc = JsonDocument.Parse(r).RootElement;
        Assert.Equal(14, doc.GetProperty("progress").GetProperty("currentStep").GetInt32());
        Assert.Equal(60, doc.GetProperty("waitedSeconds").GetInt32());
        Assert.False(doc.GetProperty("runEnded").GetBoolean());
    }

    [Fact]
    public async Task Recipe_status_wait_returns_early_when_the_run_ends()
    {
        var wire = new SeqWire();
        wire.Responses.Enqueue(Running(20)); wire.Responses.Enqueue(Running(21)); wire.Responses.Enqueue(Ended);
        var waited = new List<TimeSpan>();
        var tools = new ControlTools(wire, (d, _) => { waited.Add(d); return Task.CompletedTask; })
            { PollInterval = TimeSpan.FromSeconds(10) };
        string r = await tools.RecipeStatus(waitSeconds: 300);
        Assert.Equal(3, wire.Calls);
        Assert.Equal(2, waited.Count);
        var doc = JsonDocument.Parse(r).RootElement;
        Assert.False(doc.GetProperty("isRecipeRunning").GetBoolean());
        Assert.True(doc.GetProperty("runEnded").GetBoolean());
        Assert.Equal(20, doc.GetProperty("waitedSeconds").GetInt32());
    }

    [Fact]
    public async Task Recipe_status_wait_returns_at_once_when_nothing_is_running()
    {
        var wire = new SeqWire(); wire.Responses.Enqueue(Ended);
        var waited = new List<TimeSpan>();
        var tools = new ControlTools(wire, (d, _) => { waited.Add(d); return Task.CompletedTask; });
        string r = await tools.RecipeStatus(waitSeconds: 120);
        Assert.Equal(1, wire.Calls);
        Assert.Empty(waited);
        Assert.True(JsonDocument.Parse(r).RootElement.GetProperty("runEnded").GetBoolean());
    }

    [Fact]
    public async Task Recipe_status_wait_is_capped()
    {
        var wire = new SeqWire(); wire.Responses.Enqueue(Running(1));
        var waited = new List<TimeSpan>();
        var tools = new ControlTools(wire, (d, _) => { waited.Add(d); return Task.CompletedTask; })
            { PollInterval = TimeSpan.FromSeconds(100) };
        string r = await tools.RecipeStatus(waitSeconds: 9999);
        Assert.Equal(ControlTools.MaxWaitSeconds, JsonDocument.Parse(r).RootElement.GetProperty("waitedSeconds").GetInt32());
    }

    [Fact]
    public async Task Compute_deposition_plan_maps_to_the_read_tier_wire_command_with_all_arguments()
    {
        var wire = new FakeWire();
        string result = await new ControlTools(wire).ComputeDepositionPlan(
            period_A: 68.5, gamma: 0.215, heavy_rate_A_per_s: 0.4711, light_rate_A_per_s: 0.4030,
            bilayer_count: 5, adhesion_thickness_A: 197.0, adhesion_rate_A_per_s: 0.4711,
            dead_time_s: 2.02, dead_time_rate_A_per_s: 0.4990);

        Assert.Equal("recipe.compute_plan", wire.Last!.Value.Cmd);
        Assert.Contains("okish", result);
        var args = ArgsOf(wire);
        Assert.Equal(68.5, args.GetProperty("period_A").GetDouble());
        Assert.Equal(0.215, args.GetProperty("gamma").GetDouble());
        Assert.Equal(0.4711, args.GetProperty("heavy_rate_A_per_s").GetDouble());
        Assert.Equal(0.4030, args.GetProperty("light_rate_A_per_s").GetDouble());
        Assert.Equal(5, args.GetProperty("bilayer_count").GetInt32());
        Assert.Equal(197.0, args.GetProperty("adhesion_thickness_A").GetDouble());
        Assert.Equal(0.4711, args.GetProperty("adhesion_rate_A_per_s").GetDouble());
        Assert.Equal(2.02, args.GetProperty("dead_time_s").GetDouble());
        Assert.Equal(0.4990, args.GetProperty("dead_time_rate_A_per_s").GetDouble());
    }

    [Fact]
    public async Task Compute_deposition_plan_leaves_absent_optionals_null()
    {
        var wire = new FakeWire();
        await new ControlTools(wire).ComputeDepositionPlan(68.5, 0.215, 0.4711, 0.4030, 5);

        var args = ArgsOf(wire);
        foreach (string name in new[]
                 { "adhesion_thickness_A", "adhesion_rate_A_per_s", "dead_time_s", "dead_time_rate_A_per_s" })
            Assert.Equal(JsonValueKind.Null, args.GetProperty(name).ValueKind);
    }

    private sealed class RefusingWire(string code, string message) : IWireCaller
    {
        public Task<JsonElement> CallAsync(string cmd, object? args, CancellationToken ct = default) =>
            throw new WireException(code, message);
    }

    [Theory]
    [InlineData(0.0, 0.403)]
    [InlineData(-0.4711, 0.403)]
    [InlineData(0.4711, 0.0)]
    public async Task Compute_deposition_plan_surfaces_a_bad_request_for_a_non_positive_rate(
        double heavyRate, double lightRate)
    {
        var wire = new RefusingWire("bad_request", "heavy_rate_A_per_s must be a finite number > 0");

        var ex = await Assert.ThrowsAsync<ModelContextProtocol.McpException>(() =>
            new ControlTools(wire).ComputeDepositionPlan(68.5, 0.215, heavyRate, lightRate, 5));

        Assert.StartsWith("bad_request:", ex.Message);
    }
}
