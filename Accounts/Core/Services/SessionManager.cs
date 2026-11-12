using APS.Accounts.Audit;
using APS.Accounts.Domain;

namespace APS.Accounts.Services;

public sealed class QuotaEventArgs(TimeSpan remaining) : EventArgs
{
    public TimeSpan Remaining { get; } = remaining;
}

/// <summary>
/// Logout/switch are audited here; login is audited by <see cref="AuthenticationService"/>.
/// Metering pauses during a recipe run (see <see cref="PauseQuota"/>) so an operator is never
/// billed for — or logged out mid — a deposition.
/// </summary>
public sealed class SessionManager
{
    private readonly IClock _clock;
    private readonly IAccountAuditSink _auditSink;
    private readonly TimeSpan? _idleTimeout;
    private readonly IQuotaService? _quota;
    private readonly long _maxMeterStepSeconds;
    private readonly ILoginSessionStore? _sessionStore;

    private AuthenticatedPrincipal? _current;
    private DateTime _lastActivityUtc;
    private bool _idleFired;

    private bool _metered;
    private long _remainingSeconds;
    private long _warnThresholdSeconds;
    private DateTime _lastMeterUtc;
    private bool _quotaPaused;
    private bool _warnFired;
    private bool _exhaustFired;

    public SessionManager(IClock clock, IAccountAuditSink? auditSink = null, TimeSpan? idleTimeout = null,
        IQuotaService? quota = null, long maxMeterStepSeconds = 120, ILoginSessionStore? sessionStore = null)
    {
        _clock = clock;
        _auditSink = auditSink ?? NullAccountAuditSink.Instance;
        _idleTimeout = idleTimeout;
        _quota = quota;
        _maxMeterStepSeconds = maxMeterStepSeconds;
        _sessionStore = sessionStore;
    }

    public AuthenticatedPrincipal? Current => _current;

    public event EventHandler? CurrentChanged;
    public event EventHandler? IdleElapsed;
    public event EventHandler<QuotaEventArgs>? QuotaWarning;
    public event EventHandler<QuotaEventArgs>? QuotaExhausted;

    /// <summary>Time remaining for the current user, or null if the session is unmetered
    /// (no quota service, no allowance, or a quota-exempt principal).</summary>
    public TimeSpan? QuotaRemaining =>
        _metered ? TimeSpan.FromSeconds(Math.Max(0, _remainingSeconds)) : null;

    public bool CanStartRun() => !_metered || _remainingSeconds > 0;

    public void BeginSession(AuthenticatedPrincipal principal)
    {
        _current = principal;
        ResetIdle();
        InitQuota(principal);
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SwitchUser(AuthenticatedPrincipal newPrincipal)
    {
        var previous = _current;
        FinalizeQuota();
        if (previous is not null && !previous.IsOffline)
            _sessionStore?.RecordEnd(previous.SessionId, _clock.UtcNow, "switch-user");
        _current = newPrincipal;
        ResetIdle();
        InitQuota(newPrincipal);
        if (previous is not null)
            _auditSink.SwitchedUser(previous, newPrincipal);
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void EndSession(string reason)
    {
        var ending = _current;
        if (ending is null) return;
        FinalizeQuota();
        if (!ending.IsOffline) _sessionStore?.RecordEnd(ending.SessionId, _clock.UtcNow, reason);
        _current = null;
        _metered = false;
        _auditSink.LoggedOut(ending, reason);
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void NotifyActivity() => ResetIdle();

    /// <summary>Stop metering (e.g. while a recipe runs). Bills time up to now first.</summary>
    public void PauseQuota()
    {
        if (_metered && !_quotaPaused)
        {
            MeterNow();
            _quotaPaused = true;
        }
    }

    /// <summary>Resume metering after a run; the paused interval is not billed.</summary>
    public void ResumeQuota()
    {
        if (_metered && _quotaPaused)
        {
            _quotaPaused = false;
            _lastMeterUtc = _clock.UtcNow;
        }
    }

    public void Tick()
    {
        TickIdle();
        if (_metered && !_quotaPaused) MeterNow();
    }

    private void TickIdle()
    {
        if (_current is null || _idleTimeout is null || _idleFired) return;
        if (_clock.UtcNow - _lastActivityUtc >= _idleTimeout.Value)
        {
            _idleFired = true;
            IdleElapsed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void InitQuota(AuthenticatedPrincipal principal)
    {
        _metered = false;
        _quotaPaused = false;
        _warnFired = false;
        _exhaustFired = false;

        if (_quota is null || principal.Can(Permission.QuotaExempt)) return;

        var state = _quota.GetQuotaState(principal.UserId);
        if (!state.Metered) return;

        _metered = true;
        _remainingSeconds = state.RemainingSeconds;
        _warnThresholdSeconds = state.WarnThresholdSeconds;
        _lastMeterUtc = _clock.UtcNow;
    }

    private void MeterNow()
    {
        if (_current is null || _quota is null) return;

        var now = _clock.UtcNow;
        var from = _lastMeterUtc;
        var raw = (now - from).TotalSeconds;
        if (raw < 0) { _lastMeterUtc = now; return; }   // clock rolled back — don't bill, re-anchor

        var elapsed = (long)Math.Floor(raw);
        if (elapsed <= 0) return;   // <1s: keep the anchor so the sub-second remainder accumulates

        DateTime to;
        if (elapsed > _maxMeterStepSeconds)
        {
            // Pathological forward jump (clock glitch / long sleep): cap the bill and re-anchor,
            // deliberately discarding the excess so a clock leap cannot over-bill the operator.
            elapsed = _maxMeterStepSeconds;
            _lastMeterUtc = now;
            to = now;
        }
        else
        {
            // Advance by exactly what we billed so the sub-second remainder carries forward.
            to = from.AddSeconds(elapsed);
            _lastMeterUtc = to;
        }

        _quota.RecordUsage(_current.UserId, elapsed, from, to);
        _remainingSeconds -= elapsed;

        if (!_warnFired && _remainingSeconds <= _warnThresholdSeconds)
        {
            _warnFired = true;
            QuotaWarning?.Invoke(this, new QuotaEventArgs(TimeSpan.FromSeconds(Math.Max(0, _remainingSeconds))));
        }
        if (!_exhaustFired && _remainingSeconds <= 0)
        {
            _exhaustFired = true;
            QuotaExhausted?.Invoke(this, new QuotaEventArgs(TimeSpan.Zero));
        }
    }

    private void FinalizeQuota()
    {
        if (!_metered || _current is null || _quota is null) return;
        if (!_quotaPaused) MeterNow();
        _quota.Reconcile(_current.UserId);
    }

    /// <summary>Call periodically so concurrent stations converge instead of only flushing at
    /// logout.</summary>
    public void ReconcilePending()
    {
        if (!_metered || _current is null || _quota is null) return;
        _quota.Reconcile(_current.UserId);
    }

    private void ResetIdle()
    {
        _lastActivityUtc = _clock.UtcNow;
        _idleFired = false;
    }
}
