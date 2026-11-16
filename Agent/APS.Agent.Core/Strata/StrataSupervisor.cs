using System.Diagnostics;
using System.Text.Json;
using APS.Agent.Core.Providers;
using APS.Agent.Core.Serialization;

namespace APS.Agent.Core.Strata;

public sealed record StrataModelInfo(string ModelId, int NCtx, bool Loaded);

public sealed class StrataSupervisor : IAsyncDisposable
{
    private Process? _process;

    public static async Task<StrataModelInfo> ProbeRunningAsync(
        Uri baseUri,
        HttpClient? httpClient = null,
        CancellationToken ct = default)
    {
        EndpointGuard.Validate(baseUri, allowExternalEndpoints: false);
        Uri modelsUri = ProviderHttpHelper.AppendPath(baseUri, "models");
        using var ownedClient = httpClient is null
            ? new HttpClient(EndpointGuard.CreateNoRedirectHandler(), disposeHandler: true)
            : null;
        HttpClient client = httpClient ?? ownedClient!;

        using var resp = await client.GetAsync(modelsUri, ct).ConfigureAwait(false);
        string raw = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Strata probe failed with HTTP {(int)resp.StatusCode}: {raw}");

        using var doc = StrictJson.Parse(raw);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Strata /v1/models response has no data array.");

        foreach (var item in data.EnumerateArray())
        {
            string id = item.TryGetProperty("id", out var idP) && idP.ValueKind == JsonValueKind.String
                ? idP.GetString() ?? ""
                : "";
            bool loaded = IsLoadedStatus(item);
            if (!loaded || string.IsNullOrEmpty(id)) continue;

            int nCtx = 0;
            if (item.TryGetProperty("meta", out var meta)
                && meta.ValueKind == JsonValueKind.Object
                && meta.TryGetProperty("n_ctx", out var ctxP)
                && ctxP.TryGetInt32(out int parsedCtx))
            {
                nCtx = parsedCtx;
            }
            return new StrataModelInfo(id, nCtx, true);
        }

        throw new InvalidOperationException("No loaded model reported by Strata /v1/models.");
    }

    public static void ValidateSingleGpuSelection(IReadOnlyList<string> args, string? configPath)
    {
        for (int i = 0; i < args.Count; i++)
        {
            string a = args[i];
            if (a == "--gpu" && i + 1 < args.Count)
                EnsureSingleGpuValue(args[i + 1]);
            else if (a.StartsWith("--gpu=", StringComparison.Ordinal))
                EnsureSingleGpuValue(a["--gpu=".Length..]);
        }

        if (!string.IsNullOrWhiteSpace(configPath) && File.Exists(configPath))
        {
            using var doc = StrictJson.Parse(File.ReadAllText(configPath));
            if (doc.RootElement.TryGetProperty("gpu", out var gpuEl))
            {
                if (gpuEl.ValueKind == JsonValueKind.Array && gpuEl.GetArrayLength() != 1)
                    throw new InvalidOperationException("Managed Strata launch requires single-GPU configuration.");
                if (gpuEl.ValueKind == JsonValueKind.String)
                    EnsureSingleGpuValue(gpuEl.GetString() ?? "");
            }
        }
    }

    public async Task<StrataModelInfo> StartManagedAsync(
        Uri baseUri,
        string? exePath = null,
        IReadOnlyList<string>? args = null,
        string? configPath = null,
        TimeSpan? readyTimeout = null,
        CancellationToken ct = default)
    {
        EndpointGuard.Validate(baseUri, allowExternalEndpoints: false);
        string exe = exePath
            ?? Environment.GetEnvironmentVariable("APS_STRATA_EXE")
            ?? throw new InvalidOperationException("APS_STRATA_EXE is not configured.");
        string? cfg = configPath ?? Environment.GetEnvironmentVariable("APS_STRATA_CONFIG");
        var launchArgs = args?.ToList() ?? SplitArgs(Environment.GetEnvironmentVariable("APS_STRATA_ARGS"));
        if (!string.IsNullOrWhiteSpace(cfg) && !launchArgs.Contains("--config", StringComparer.Ordinal))
        {
            launchArgs.Add("--config");
            launchArgs.Add(cfg);
        }
        ValidateSingleGpuSelection(launchArgs, cfg);

        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string a in launchArgs)
            psi.ArgumentList.Add(a);

        _process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to launch '{exe}'.");
        DateTime deadline = DateTime.UtcNow + (readyTimeout ?? TimeSpan.FromSeconds(120));
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (_process.HasExited)
                throw new InvalidOperationException($"Managed Strata process exited with code {_process.ExitCode}.");
            try
            {
                return await ProbeRunningAsync(baseUri, null, ct).ConfigureAwait(false);
            }
            catch (Exception) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(1000, ct).ConfigureAwait(false);
            }
        }
        throw new TimeoutException($"Timed out waiting for Strata at {baseUri}.");
    }

    private static bool IsLoadedStatus(JsonElement item)
    {
        if (!item.TryGetProperty("status", out var status))
            return false;
        if (status.ValueKind == JsonValueKind.String)
            return string.Equals(status.GetString(), "loaded", StringComparison.OrdinalIgnoreCase);
        if (status.ValueKind == JsonValueKind.Object
            && status.TryGetProperty("value", out var val)
            && val.ValueKind == JsonValueKind.String)
        {
            return string.Equals(val.GetString(), "loaded", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    private static void EnsureSingleGpuValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains(',') || !int.TryParse(value.Trim(), out int idx) || idx < 0)
            throw new InvalidOperationException($"Single non-negative GPU index required (got '{value}').");
    }

    private static List<string> SplitArgs(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    public ValueTask DisposeAsync()
    {
        if (_process is not null)
        {
            try
            {
                if (!_process.HasExited)
                    _process.Kill(entireProcessTree: true);
            }
            catch
            {
            }
            _process.Dispose();
            _process = null;
        }
        return ValueTask.CompletedTask;
    }
}
