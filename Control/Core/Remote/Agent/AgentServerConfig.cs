using System.Security.Cryptography;
using System.Text;
using APS.SystemControl.XML;
using APS.SystemControl.XML.Core;

namespace APS.SystemControl.Core.Remote.Agent;

public enum AgentTier { None = 0, Read = 1, Recipe = 2, Manual = 3 }

/// <param name="RecipeFolder">Folder under the recipes directory the key is confined to for
/// upload and listing; null = the whole library.</param>
public sealed record AgentKey(string Name, string Sha256Hex, AgentTier Tier, string? RecipeFolder = null);

public sealed class AgentServerConfig
{
    public bool Enabled { get; init; }
    public int Port { get; init; } = 8026;
    public string Bind { get; init; } = "127.0.0.1";
    public bool AllowRecipe { get; init; } = true;
    public bool AllowManual { get; init; }
    public IReadOnlyList<AgentKey> Keys { get; init; } = [];

    /// <summary>Telemetry sampler period; a subscriber can ask for slower, never faster.</summary>
    public int BusSamplerMs { get; init; } = 250;
    /// <summary>Per-subscriber bounded queue depth (frames) before drop-oldest kicks in.</summary>
    public int BusQueueDepth { get; init; } = 1000;

    /// <summary>&lt;Bus heartbeatMs="30000"/&gt;: re-report every point this often even when nothing
    /// changed, so a quiet machine still produces samples. 0 (default) = change-only.</summary>
    public int BusHeartbeatMs { get; init; }
    /// <summary>&lt;ControlMode restoreRemote="true"/&gt;: come back in Online-Remote after a restart
    /// when the last persisted state (bin\ControlMode.state) was Remote. Default: start in Local.</summary>
    public bool RestoreRemote { get; init; }

    /// <summary>Missing or unreadable file => disabled default (feature off), same as RemoteProfileLoader.</summary>
    public static AgentServerConfig Load(string path)
    {
        if (!File.Exists(path)) return new AgentServerConfig();
        XML_AgentServer? xml;
        try { xml = Serializer<XML_AgentServer>.Load(path); }
        catch { return new AgentServerConfig(); }
        if (xml is null) return new AgentServerConfig();
        return new AgentServerConfig
        {
            Enabled = xml.Enabled,
            Port = xml.Port,
            Bind = xml.Bind,
            AllowRecipe = xml.AllowRecipe,
            AllowManual = xml.AllowManual,
            // An unparseable or explicit "None" tier is a config error on an auth surface —
            // skip the key entirely (fail closed) rather than silently promoting it to Read
            // (fail open).
            Keys = xml.Keys
                .Where(k => !string.IsNullOrWhiteSpace(k.Hash))
                .Where(k => Enum.TryParse<AgentTier>(k.Tier, ignoreCase: true, out var t) && t != AgentTier.None)
                // Same rule for a recipe folder that is not a plain, creatable folder name: a
                // separator, "..", a drive or a device name could put uploads outside the
                // recipes directory, so the key is skipped rather than left unscoped.
                .Where(k => string.IsNullOrWhiteSpace(k.RecipeFolder) || RecipeStore.IsValidRecipeName(k.RecipeFolder))
                .Select(k => new AgentKey(
                    k.Name,
                    k.Hash.Trim(),
                    Enum.Parse<AgentTier>(k.Tier, ignoreCase: true),
                    string.IsNullOrWhiteSpace(k.RecipeFolder) ? null : k.RecipeFolder))
                .ToList(),
            BusSamplerMs = Math.Clamp(xml.Bus?.SamplerMs ?? 250, 100, 10_000),
            BusQueueDepth = Math.Clamp(xml.Bus?.QueueDepth ?? 1000, 10, 100_000),
            // 0 stays 0 (off); anything else is clamped into 1 s .. 1 h.
            BusHeartbeatMs = (xml.Bus?.HeartbeatMs ?? 0) <= 0 ? 0 : Math.Clamp(xml.Bus!.HeartbeatMs, 1000, 3_600_000),
            RestoreRemote = xml.ControlMode?.RestoreRemote ?? false,
        };
    }

    /// <summary>Returns the granted (clamped) tier and key name, or null when no key matches.</summary>
    public (AgentTier Tier, string Name)? Authenticate(string key) =>
        AuthenticateKey(key) is { } k ? (k.Tier, k.Name) : null;

    /// <summary>The matching key with its tier clamped by the allow flags, or null when no key matches.</summary>
    public AgentKey? AuthenticateKey(string key)
    {
        string hex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        foreach (var k in Keys)
            if (string.Equals(k.Sha256Hex, hex, StringComparison.OrdinalIgnoreCase))
                return k with { Tier = Clamp(k.Tier) };
        return null;
    }

    private AgentTier Clamp(AgentTier tier)
    {
        if (tier >= AgentTier.Manual && !AllowManual) tier = AgentTier.Recipe;
        if (tier >= AgentTier.Recipe && !AllowRecipe) tier = AgentTier.Read;
        return tier;
    }
}
