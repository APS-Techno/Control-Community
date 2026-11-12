using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using APS.Mcp;
using APS.Wire;
using ModelContextProtocol;
using Xunit;

/// <summary>
/// The agent has no file tools, so fetch_run_files is its only path to the ELN: it lands artifacts
/// in downloadDir and returns absolute local paths that upload_file takes as-is.
/// </summary>
public sealed class RunFilesToolsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"mcpdl_{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    /// <summary>Scripted Control: one run with a 4-chunk TSV (1040 bytes at 300 per chunk), a one-chunk log, no recipe or snapshot.</summary>
    private sealed class ScriptedWire : IWireCaller
    {
        public readonly List<(string Cmd, string Args)> Calls = [];
        public readonly byte[] Tsv = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("Time\tElapsed\n12:00:00\t0.0\n", 40)));
        public readonly byte[] Log = Encoding.UTF8.GetBytes("--- Process_x.log ---\n12:00:00.100  [Recipe]  Recipe started: R\n");
        public const int Chunk = 300;

        public Task<JsonElement> CallAsync(string cmd, object? args, CancellationToken ct = default)
        {
            string a = JsonSerializer.Serialize(args, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            Calls.Add((cmd, a));
            var req = JsonDocument.Parse(a == "null" ? "{}" : a).RootElement;
            return Task.FromResult(cmd switch
            {
                "runs.list" => Parse("""
                    {"runs":[{"runId":"2026-09-03_12-00-00_R","recipe":"R","startedAt":"2026-09-03T12:00:00","endedAt":null,
                      "outcome":"complete","message":"(01m)","artifacts":[
                        {"kind":"tsv","fileName":"2026-09-03_12-00-00_R.tsv","bytes":1040},
                        {"kind":"log","fileName":"2026-09-03_12-00-00_R.log","bytes":70}]}]}
                    """),
                "runs.file" => File(req),
                _ => throw new InvalidOperationException(cmd),
            });
        }

        private JsonElement File(JsonElement req)
        {
            string kind = req.GetProperty("kind").GetString()!;
            long offset = req.TryGetProperty("offset", out var o) ? o.GetInt64() : 0;
            int max = req.TryGetProperty("maxBytes", out var m) ? m.GetInt32() : 1 << 20;
            byte[] all = kind == "tsv" ? Tsv : Log;
            int len = (int)Math.Min(Math.Min(max, Chunk), all.Length - offset);
            var slice = all.AsSpan((int)offset, len).ToArray();
            return Parse(JsonSerializer.Serialize(new
            {
                runId = "2026-09-03_12-00-00_R", kind,
                fileName = kind == "tsv" ? "2026-09-03_12-00-00_R.tsv" : "2026-09-03_12-00-00_R.log",
                offset, bytes = len, totalBytes = all.Length, eof = offset + len >= all.Length,
                contentBase64 = Convert.ToBase64String(slice),
            }));
        }

        private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;
    }

    private static McpBridgeConfig Cfg(string? downloadDir) => new("127.0.0.1", 8026, "k", null, downloadDir);

    private static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    [Fact]
    public async Task List_runs_maps_to_wire_command_with_count()
    {
        var wire = new ScriptedWire();
        string result = await new ControlTools(wire, cfg: Cfg(null)).ListRuns(count: 7);
        Assert.Equal("runs.list", wire.Calls[0].Cmd);
        Assert.Contains("\"count\":7", wire.Calls[0].Args);
        Assert.Contains("2026-09-03_12-00-00_R", result);
    }

    [Fact]
    public async Task Fetch_without_download_dir_is_a_local_error_and_never_touches_the_wire()
    {
        var wire = new ScriptedWire();
        var ex = await Assert.ThrowsAsync<McpException>(
            () => new ControlTools(wire, cfg: Cfg(null)).FetchRunFiles());
        Assert.Contains("downloadDir", ex.Message);
        Assert.Empty(wire.Calls);
    }

    [Fact]
    public async Task Fetch_latest_reassembles_chunks_and_returns_local_paths_with_hashes()
    {
        var wire = new ScriptedWire();
        string json = await new ControlTools(wire, cfg: Cfg(_dir)).FetchRunFiles();
        var root = JsonDocument.Parse(json).RootElement;

        Assert.Equal("2026-09-03_12-00-00_R", root.GetProperty("runId").GetString());
        Assert.Equal("complete", root.GetProperty("outcome").GetString());
        string dir = root.GetProperty("directory").GetString()!;
        Assert.Equal(Path.Combine(_dir, "2026-09-03_12-00-00_R"), dir);

        var files = root.GetProperty("files").EnumerateArray().ToList();
        Assert.Equal(2, files.Count);

        var tsv = files.Single(f => f.GetProperty("kind").GetString() == "tsv");
        string tsvPath = tsv.GetProperty("path").GetString()!;
        Assert.True(Path.IsPathRooted(tsvPath));
        Assert.Equal(Path.Combine(dir, "2026-09-03_12-00-00_R.tsv"), tsvPath);
        Assert.Equal(wire.Tsv, File.ReadAllBytes(tsvPath));
        Assert.Equal(wire.Tsv.Length, tsv.GetProperty("bytes").GetInt64());
        Assert.Equal(Sha(wire.Tsv), tsv.GetProperty("sha256").GetString());

        var log = files.Single(f => f.GetProperty("kind").GetString() == "log");
        Assert.Equal(wire.Log, File.ReadAllBytes(log.GetProperty("path").GetString()!));

        // latest = runs.list count 1, then one runs.file per chunk (4 tsv + 1 log)
        Assert.Equal("runs.list", wire.Calls[0].Cmd);
        Assert.Contains("\"count\":1", wire.Calls[0].Args);
        Assert.Equal(5, wire.Calls.Count(c => c.Cmd == "runs.file"));
        Assert.Contains("\"offset\":900", wire.Calls[4].Args);
        Assert.Contains("\"kind\":\"log\"", wire.Calls[5].Args);

        var skipped = root.GetProperty("notAvailable").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(["recipe", "snapshot"], skipped);
    }

    [Fact]
    public async Task Fetch_honours_the_kinds_filter()
    {
        var wire = new ScriptedWire();
        string json = await new ControlTools(wire, cfg: Cfg(_dir)).FetchRunFiles(kinds: "log");
        var files = JsonDocument.Parse(json).RootElement.GetProperty("files").EnumerateArray().ToList();
        Assert.Single(files);
        Assert.Equal("log", files[0].GetProperty("kind").GetString());
        Assert.DoesNotContain(wire.Calls, c => c.Cmd == "runs.file" && c.Args.Contains("\"kind\":\"tsv\""));
    }

    [Fact]
    public async Task Fetch_by_explicit_run_id_skips_the_latest_lookup_but_still_lists_to_find_it()
    {
        var wire = new ScriptedWire();
        string json = await new ControlTools(wire, cfg: Cfg(_dir)).FetchRunFiles(runId: "2026-09-03_12-00-00_R");
        Assert.Equal("2026-09-03_12-00-00_R", JsonDocument.Parse(json).RootElement.GetProperty("runId").GetString());
    }

    [Fact]
    public async Task Fetch_of_a_run_id_the_machine_does_not_list_is_an_error()
    {
        var wire = new ScriptedWire();
        var ex = await Assert.ThrowsAsync<McpException>(
            () => new ControlTools(wire, cfg: Cfg(_dir)).FetchRunFiles(runId: "2026-01-01_00-00-00_Nope"));
        Assert.Contains("Nope", ex.Message);
    }

    [Fact]
    public async Task Fetch_rejects_a_bad_kind_before_any_transfer()
    {
        var wire = new ScriptedWire();
        var ex = await Assert.ThrowsAsync<McpException>(
            () => new ControlTools(wire, cfg: Cfg(_dir)).FetchRunFiles(kinds: "tsv,screenshot"));
        Assert.Contains("screenshot", ex.Message);
        Assert.DoesNotContain(wire.Calls, c => c.Cmd == "runs.file");
    }

    [Fact]
    public async Task Fetch_overwrites_a_stale_copy_from_an_earlier_fetch()
    {
        var wire = new ScriptedWire();
        string dir = Path.Combine(_dir, "2026-09-03_12-00-00_R");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "2026-09-03_12-00-00_R.tsv"), "stale");

        await new ControlTools(wire, cfg: Cfg(_dir)).FetchRunFiles(kinds: "tsv");
        Assert.Equal(wire.Tsv, File.ReadAllBytes(Path.Combine(dir, "2026-09-03_12-00-00_R.tsv")));
    }
}
