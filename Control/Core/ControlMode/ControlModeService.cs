namespace APS.SystemControl.Core;

/// <summary>Machine-wide control state (spec docs/superpowers/specs/2026-09-06-control-mode-design.md §3).
/// Operator-owned: only the UI calls <see cref="Set"/>. Entry points call <see cref="Authorize"/> before
/// writing to the channel pool; nothing on the hardware thread reads this.</summary>
public sealed class ControlModeService
{
    public const string OperatorDenial = "control mode is Remote — operator commands are locked; switch to Local";

    private readonly object _gate = new();
    private ControlMode _mode = ControlMode.Local;
    private string? _persistPath;
    private Action<string>? _log;
    private bool _persistWarned;

    public ControlMode Mode { get { lock (_gate) return _mode; } }
    public bool IsRemote => Mode == ControlMode.Remote;
    public ControlModeChange? LastChange { get; private set; }
    /// <summary>Raised on the caller's thread after a real change; UI subscribers marshal themselves.</summary>
    public event EventHandler<ControlModeChange>? Changed;

    public void Initialize(ControlModeStartup s)
    {
        string why;
        lock (_gate)
        {
            _persistPath = s.PersistPath;
            _log = s.Log;
            _mode = ResolveStartup(s, out why);
        }
        _log?.Invoke($"[ControlMode] startup mode {Mode} ({why})");
    }

    internal static ControlMode ResolveStartup(ControlModeStartup s, out string why)
    {
        if (!string.IsNullOrWhiteSpace(s.DevOverride))
        {
            if (Enum.TryParse<ControlMode>(s.DevOverride.Trim(), ignoreCase: true, out var dev))
            {
                why = "from APS_DEV_CONTROL_MODE (dev override)";
                return dev;
            }
            why = $"APS_DEV_CONTROL_MODE='{s.DevOverride}' not understood; default";
            return ControlMode.Local;
        }
        if (s.RestoreRemote && s.PersistPath is not null)
        {
            try
            {
                if (File.Exists(s.PersistPath)
                    && string.Equals(File.ReadAllText(s.PersistPath).Trim(), nameof(ControlMode.Remote), StringComparison.OrdinalIgnoreCase))
                {
                    why = "restored from last session (restoreRemote)";
                    return ControlMode.Remote;
                }
            }
            catch { /* unreadable file = no restore */ }
        }
        why = "default";
        return ControlMode.Local;
    }

    public bool Set(ControlMode to, string by)
    {
        ControlModeChange change;
        lock (_gate)
        {
            if (_mode == to) return false;
            change = new ControlModeChange(_mode, to, by, DateTime.UtcNow);
            _mode = to;
            LastChange = change;
            Persist(to);
        }
        _log?.Invoke($"[ControlMode] {change.From} → {change.To} by {by}");
        Changed?.Invoke(this, change);
        return true;
    }

    private void Persist(ControlMode mode)
    {
        if (_persistPath is null) return;
        try { File.WriteAllText(_persistPath, mode.ToString()); }
        catch (Exception ex)
        {
            if (_persistWarned) return;
            _persistWarned = true;
            _log?.Invoke($"[ControlMode] cannot persist to {_persistPath}: {ex.Message}");
        }
    }

    /// <summary>Null = allowed; otherwise the exact operator-facing denial text.</summary>
    public string? Authorize(CommandOrigin origin, CommandClass cls)
    {
        if (cls == CommandClass.Read) return null;
        var mode = Mode;
        return origin switch
        {
            CommandOrigin.Operator => cls == CommandClass.Safety || mode != ControlMode.Remote ? null : OperatorDenial,
            // Say who can change it and that waiting is pointless: an agent kept polling for Remote
            // instead of stopping (chamber run 1, agent campaign 2026-09-15, item 8).
            _ => mode == ControlMode.Remote ? null
               : $"control mode is {mode} — host commands need Online-Remote; only an operator can switch it (Service → Control mode…) — do not poll, ask the operator",
        };
    }
}
