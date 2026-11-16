using APS.Agent.Cli.Commands;
using APS.Agent.Core.Guards;
using APS.Agent.Core.Providers;

namespace APS.Agent.Cli;

public static class Program
{
    public static Task<int> Main(string[] args) =>
        RunAsync(args, Console.In, Console.Out, Console.Error, CancellationToken.None);

    public static async Task<int> RunAsync(
        string[] args,
        TextReader input,
        TextWriter output,
        TextWriter error,
        CancellationToken ct = default)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            await WriteUsageAsync(output).ConfigureAwait(false);
            return 0;
        }

        try
        {
            string cmd = args[0].ToLowerInvariant();
            string[] rest = args[1..];
            return cmd switch
            {
                "run" => await OperatorCommands.RunSingleAsync(ParseOperatorOptions(rest), output, error, ct).ConfigureAwait(false),
                "repl" => await OperatorCommands.RunReplAsync(ParseOperatorOptions(rest), input, output, ct).ConfigureAwait(false),
                "bench" => await RunBenchCommandAsync(rest, output, error, ct).ConfigureAwait(false),
                _ => await UnknownAsync($"Unknown command '{args[0]}'.", error).ConfigureAwait(false),
            };
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException or FileNotFoundException)
        {
            await error.WriteLineAsync($"Error: {ex.Message}").ConfigureAwait(false);
            return 2;
        }
    }

    private static async Task<int> RunBenchCommandAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken ct)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            await WriteUsageAsync(output).ConfigureAwait(false);
            return 0;
        }

        string sub = args[0].ToLowerInvariant();
        string[] rest = args[1..];
        var opts = ParseKeyValueArgs(rest, out var flags);

        switch (sub)
        {
            case "freeze":
            {
                string campaign = RequireOpt(opts, "--campaign");
                string track = RequireOpt(opts, "--track");
                string modelsRaw = RequireOpt(opts, "--models");
                var models = ParseModelList(modelsRaw);
                opts.TryGetValue("--suite-dir", out string? suiteDir);
                BenchCommands.Freeze(campaign, track, models, suiteDir);
                await output.WriteLineAsync($"Frozen campaign at {Path.GetFullPath(campaign)}").ConfigureAwait(false);
                return 0;
            }
            case "run":
            {
                string campaign = RequireOpt(opts, "--campaign");
                var runOpts = new BenchRunOptions(
                    campaign,
                    opts.TryGetValue("--protocol", out var pr) ? ParseProtocol(pr) : ProviderProtocol.ChatCompletions,
                    opts.TryGetValue("--endpoint", out var ep) ? new Uri(ep, UriKind.Absolute) : null,
                    opts.GetValueOrDefault("--api-key-env"),
                    flags.Contains("--allow-external"),
                    opts.GetValueOrDefault("--task"),
                    opts.TryGetValue("--limit", out var lim) ? int.Parse(lim) : null,
                    opts.TryGetValue("--max-turns", out var mt) ? int.Parse(mt) : 40,
                    opts.TryGetValue("--context-budget", out var cb) ? int.Parse(cb) : 65536,
                    opts.TryGetValue("--max-reasoning", out var mr) ? int.Parse(mr) : null);
                await BenchCommands.RunAsync(runOpts, output, ct).ConfigureAwait(false);
                return 0;
            }
            case "score":
            {
                string campaign = RequireOpt(opts, "--campaign");
                bool batchesOnly = flags.Contains("--batches-only");
                int batchSize = opts.TryGetValue("--batch-size", out var bs) ? int.Parse(bs) : 8;
                IReadOnlyList<string> written = BenchCommands.Score(campaign, batchesOnly, batchSize);
                foreach (string path in written)
                    await output.WriteLineAsync(path).ConfigureAwait(false);
                return 0;
            }
            case "report":
            {
                string campaign = RequireOpt(opts, "--campaign");
                opts.TryGetValue("--out", out string? outDir);
                IReadOnlyList<string> written = BenchCommands.Report(campaign, outDir);
                foreach (string path in written)
                    await output.WriteLineAsync(path).ConfigureAwait(false);
                return 0;
            }
            case "freeze-export":
            {
                string campaign = RequireOpt(opts, "--campaign");
                string outDir = RequireOpt(opts, "--out");
                BenchCommands.FreezeExport(campaign, outDir);
                await output.WriteLineAsync($"Exported clean campaign to {Path.GetFullPath(outDir)}").ConfigureAwait(false);
                return 0;
            }
            default:
                return await UnknownAsync($"Unknown bench subcommand '{args[0]}'.", error).ConfigureAwait(false);
        }
    }

    private static OperatorCliOptions ParseOperatorOptions(string[] args)
    {
        var opts = ParseKeyValueArgs(args, out var flags);
        AgentMode mode = opts.TryGetValue("--mode", out var m)
            ? ParseMode(m)
            : AgentMode.Copilot;
        ProviderProtocol proto = opts.TryGetValue("--protocol", out var pr)
            ? ParseProtocol(pr)
            : ProviderProtocol.ChatCompletions;
        Uri endpoint = opts.TryGetValue("--endpoint", out var ep)
            ? new Uri(ep, UriKind.Absolute)
            : OperatorCommands.DefaultLocalEndpoint;

        return new OperatorCliOptions(
            mode,
            proto,
            endpoint,
            opts.GetValueOrDefault("--model"),
            opts.GetValueOrDefault("--api-key-env"),
            flags.Contains("--allow-external"),
            opts.GetValueOrDefault("--mcp-bin") ?? "APS.Mcp.exe",
            opts.GetValueOrDefault("--mcp-config"),
            opts.GetValueOrDefault("--skill-dir"),
            opts.GetValueOrDefault("--journal"),
            opts.GetValueOrDefault("--prompt"),
            flags.Contains("--eln-sync"),
            flags.Contains("--manage-strata"),
            opts.GetValueOrDefault("--strata-exe"),
            opts.GetValueOrDefault("--strata-config"),
            opts.TryGetValue("--max-turns", out var mt) ? int.Parse(mt) : 40,
            opts.TryGetValue("--context-budget", out var cb) ? int.Parse(cb) : 65536,
            opts.TryGetValue("--max-reasoning", out var mr) ? int.Parse(mr) : null,
            opts.GetValueOrDefault("--reasoning-effort"),
            flags.Contains("--allow-direct-device"));
    }

    private static Dictionary<string, string> ParseKeyValueArgs(string[] args, out HashSet<string> flags)
    {
        var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            string cur = args[i];
            if (!cur.StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Unexpected positional argument '{cur}'.");
            if (cur is "--allow-external" or "--eln-sync" or "--manage-strata" or "--allow-direct-device" or "--batches-only")
            {
                flags.Add(cur);
                continue;
            }
            if (i + 1 >= args.Length)
                throw new ArgumentException($"Missing value for option '{cur}'.");
            kv[cur] = args[++i];
        }
        return kv;
    }

    private static List<(string Selector, string Thinking)> ParseModelList(string raw)
    {
        var list = new List<(string, string)>();
        foreach (string item in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int sep = item.LastIndexOf(':');
            if (sep <= 0 || sep == item.Length - 1)
                throw new ArgumentException($"Model spec '{item}' must have form <selector>:<thinking>.");
            list.Add((item[..sep], item[(sep + 1)..]));
        }
        return list;
    }

    private static AgentMode ParseMode(string raw) =>
        raw.Trim().ToLowerInvariant() switch
        {
            "copilot" => AgentMode.Copilot,
            "autonomous" => AgentMode.Autonomous,
            _ => throw new ArgumentException($"Unknown --mode '{raw}'. Expected copilot or autonomous."),
        };

    private static ProviderProtocol ParseProtocol(string raw) =>
        raw.Trim().ToLowerInvariant() switch
        {
            "chat" or "chatcompletions" => ProviderProtocol.ChatCompletions,
            "responses" or "openairesponses" => ProviderProtocol.OpenAiResponses,
            "anthropic" => ProviderProtocol.Anthropic,
            _ => throw new ArgumentException($"Unknown --protocol '{raw}'. Expected chat, responses, or anthropic."),
        };

    private static string RequireOpt(Dictionary<string, string> opts, string key) =>
        opts.TryGetValue(key, out string? v) && !string.IsNullOrWhiteSpace(v)
            ? v
            : throw new ArgumentException($"Missing required option '{key}'.");

    private static async Task<int> UnknownAsync(string message, TextWriter error)
    {
        await error.WriteLineAsync(message).ConfigureAwait(false);
        return 2;
    }

    private static Task WriteUsageAsync(TextWriter output) =>
        output.WriteLineAsync("""
            Usage: aps-agent <command> [options]

            Commands:
              repl   [--mode copilot|autonomous] [--protocol chat|responses|anthropic] [--endpoint <uri>] [--model <id>]
                     [--api-key-env <var>] [--allow-external] [--mcp-bin <path>] [--mcp-config <path>]
                     [--skill-dir <path>] [--journal <path>] [--manage-strata] [--allow-direct-device]
              run    --prompt <text> [same options as repl] [--eln-sync]
              bench  freeze --campaign <dir> --track sputter|ads|cluster|all --models <sel:thinking,...> [--suite-dir <dir>]
              bench  run --campaign <dir> [--protocol chat|responses|anthropic] [--endpoint <uri>] [--api-key-env <var>]
                     [--allow-external] [--task <id>] [--limit <n>] [--max-turns <n>]
              bench  score --campaign <dir> [--batches-only] [--batch-size <n>]
              bench  report --campaign <dir> [--out <dir>]
              bench  freeze-export --campaign <dir> --out <dir>
            """);
}
