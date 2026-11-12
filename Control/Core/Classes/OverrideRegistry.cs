namespace APS.SystemControl.Core.Classes;

internal sealed class SuppressedAlarmEventArgs(string id, string message) : EventArgs
{
    public string Id { get; } = id;
    public string Message { get; } = message;
}

/// <summary>
/// Single source of truth for which alarm rules and interlocks are
/// currently overridden (disabled). Consulted on the Core poll loop
/// for alarms and at operation time for interlocks.
///
/// Thread safety: a single sync lock guards the two dictionaries.
/// Hot path is O(1) dictionary ContainsKey so contention is
/// negligible even at 5 Hz poll.
/// </summary>
internal sealed class OverrideRegistry
{
    private readonly Dictionary<string, OverrideEntry> alarmOverrides = new();
    private readonly Dictionary<string, OverrideEntry> interlockOverrides = new();
    private readonly object sync = new();
    private readonly IOverrideLogWriter log;
    private readonly Func<DateTime> clock;

    public event EventHandler? OverridesChanged;

    public OverrideRegistry(IOverrideLogWriter log, Func<DateTime>? clock = null)
    {
        this.log = log;
        this.clock = clock ?? (() => DateTime.Now);
    }

    public int ActiveCount
    {
        get { lock (sync) return alarmOverrides.Count + interlockOverrides.Count; }
    }

    public bool IsAlarmOverridden(string id)
    {
        lock (sync)
        {
            if (alarmOverrides.ContainsKey(id)) return true;
            // Back-compat: pre-chamber persisted entries omit the chamber
            // segment. Treat them as belonging to the default chamber so
            // they keep suppressing alarms after the upgrade.
            if (TryStripDefaultChamberPrefix(id, out var legacy)
                && alarmOverrides.ContainsKey(legacy))
                return true;
            return false;
        }
    }

    public bool IsInterlockOverridden(string id)
    {
        lock (sync) return interlockOverrides.ContainsKey(id);
    }

    public OverrideEntry? PeekAlarm(string id)
    {
        lock (sync)
        {
            if (alarmOverrides.TryGetValue(id, out var e)) return e;
            if (TryStripDefaultChamberPrefix(id, out var legacy)
                && alarmOverrides.TryGetValue(legacy, out var le))
                return le;
            return null;
        }
    }

    /// <summary>
    /// If <paramref name="id"/> is a legacy (pre-chamber-prefix) alarm or
    /// devstatus ID, returns the default-chamber-prefixed form, but only
    /// when that form is in <paramref name="validIds"/>. Otherwise the input
    /// is returned unchanged. New-format IDs pass through untouched.
    /// </summary>
    private static string MigrateLegacyId(string id, HashSet<string> validIds)
    {
        const string AlarmPfx = "alarm:";
        const string DevPfx   = "devstatus:";
        const string ChamberSeg = CoreConstants.DefaultChamberId + "|";

        string pfx;
        if (id.StartsWith(AlarmPfx, StringComparison.Ordinal)) pfx = AlarmPfx;
        else if (id.StartsWith(DevPfx, StringComparison.Ordinal)) pfx = DevPfx;
        else return id;

        string rest = id.Substring(pfx.Length);
        if (rest.StartsWith(ChamberSeg, StringComparison.Ordinal)) return id;

        string migrated = pfx + ChamberSeg + rest;
        return validIds.Contains(migrated) ? migrated : id;
    }

    /// <summary>
    /// If <paramref name="id"/> is a chamber-prefixed alarm/devstatus ID for
    /// the default chamber ("Main"), produces the legacy unprefixed form.
    /// Used so that persisted overrides written before the chamber-prefix
    /// change continue to match runtime-synthesised IDs for the Main chamber.
    /// </summary>
    private static bool TryStripDefaultChamberPrefix(string id, out string legacy)
    {
        const string AlarmMain = "alarm:" + CoreConstants.DefaultChamberId + "|";
        const string DevMain   = "devstatus:" + CoreConstants.DefaultChamberId + "|";

        if (id.StartsWith(AlarmMain, StringComparison.Ordinal))
        {
            legacy = "alarm:" + id.Substring(AlarmMain.Length);
            return true;
        }
        if (id.StartsWith(DevMain, StringComparison.Ordinal))
        {
            legacy = "devstatus:" + id.Substring(DevMain.Length);
            return true;
        }
        legacy = string.Empty;
        return false;
    }

    public OverrideEntry? PeekInterlock(string id)
    {
        lock (sync) return interlockOverrides.TryGetValue(id, out var e) ? e : null;
    }

    public void SetAlarmOverride(string id, bool enabled, bool persist, string reason, string by)
        => Set(alarmOverrides, "alarm", id, enabled, persist, reason, by);

    public void SetInterlockOverride(string id, bool enabled, bool persist, string reason, string by)
        => Set(interlockOverrides, "interlock", id, enabled, persist, reason, by);

    public void ClearAll(string by)
    {
        List<(string kind, string id)> cleared;
        lock (sync)
        {
            cleared = new List<(string, string)>(alarmOverrides.Count + interlockOverrides.Count);
            foreach (var k in alarmOverrides.Keys) cleared.Add(("alarm", k));
            foreach (var k in interlockOverrides.Keys) cleared.Add(("interlock", k));
            alarmOverrides.Clear();
            interlockOverrides.Clear();
            lastSuppressedLogAt.Clear();   // reset rate-limit state
        }
        foreach (var (kind, id) in cleared)
        {
            log.Write($"[OVERRIDE CLEAR] {kind}:{id} by={by}");
            log.WriteAudit(new AuditEvent
            {
                User = by,
                Event = "override.bulk_clear",
                Kind = kind,
                Id = id,
                State = "enabled",
            });
        }
        OverridesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Set(Dictionary<string, OverrideEntry> target, string kind,
        string id, bool enabled, bool persist, string reason, string by)
    {
        bool changed;
        lock (sync)
        {
            if (enabled)
            {
                target[id] = new OverrideEntry(reason, clock(), by, persist);
                changed = true;
            }
            else
            {
                changed = target.Remove(id);
                if (changed && ReferenceEquals(target, alarmOverrides))
                    lastSuppressedLogAt.Remove(id);
            }
        }
        if (!changed) return;

        log.Write(enabled
            ? $"[OVERRIDE SET] {kind}:{id} by={by} persist={persist.ToString().ToLowerInvariant()} reason=\"{reason}\""
            : $"[OVERRIDE CLEAR] {kind}:{id} by={by}");
        log.WriteAudit(new AuditEvent
        {
            User = by,
            Event = enabled ? "override.set" : "override.clear",
            Kind = kind,
            Id = id,
            State = enabled ? "disabled" : "enabled",
            Persist = enabled ? persist : null,
            Reason = enabled ? reason : "",
        });
        OverridesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Load(string path, HashSet<string> validAlarmIds, HashSet<string> validInterlockIds)
    {
        if (!File.Exists(path)) return;

        XML.XML_Overrides? doc;
        try
        {
            var serializer = new System.Xml.Serialization.XmlSerializer(typeof(XML.XML_Overrides));
            using var s = File.OpenRead(path);
            doc = (XML.XML_Overrides?)serializer.Deserialize(s);
        }
        catch (Exception ex)
        {
            log.Write($"[OVERRIDE ERROR] failed to parse {path}: {ex.Message}");
            log.WriteAudit(new AuditEvent
            {
                Event = "override.error",
                Message = $"failed to parse {path}: {ex.Message}",
            });
            var badPath = $"{path}.bad.{clock():yyyyMMdd-HHmmss}";
            try { File.Move(path, badPath); } catch { /* best effort */ }
            return;
        }
        if (doc == null) return;

        bool anyOrphan = false;
        lock (sync)
        {
            foreach (var e in doc.Alarms)
            {
                if (!e.Disabled || string.IsNullOrEmpty(e.Id)) continue;
                // Migrate pre-chamber-prefix entries to the new prefixed form
                // so they validate against validAlarmIds and round-trip on save.
                string id = MigrateLegacyId(e.Id, validAlarmIds);
                if (!validAlarmIds.Contains(id)) {
                    anyOrphan = true;
                    log.Write($"[OVERRIDE ORPHAN] alarm:{e.Id} — rule no longer present");
                    log.WriteAudit(new AuditEvent {
                        Event = "override.orphan", Kind = "alarm", Id = e.Id,
                        Message = "rule no longer present",
                    });
                    continue;
                }
                if (id != e.Id) anyOrphan = true; // force a rewrite to persist migrated keys
                alarmOverrides[id] = ToEntry(e);
            }
            foreach (var e in doc.Interlocks)
            {
                if (!e.Disabled || string.IsNullOrEmpty(e.Id)) continue;
                if (!validInterlockIds.Contains(e.Id)) {
                    anyOrphan = true;
                    log.Write($"[OVERRIDE ORPHAN] interlock:{e.Id} — rule no longer present");
                    log.WriteAudit(new AuditEvent {
                        Event = "override.orphan", Kind = "interlock", Id = e.Id,
                        Message = "rule no longer present",
                    });
                    continue;
                }
                interlockOverrides[e.Id] = ToEntry(e);
            }
        }

        // Rewrite file without orphans so it stays consistent on next startup.
        if (anyOrphan) Save(path);
        if (ActiveCount > 0) OverridesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Save(string path)
    {
        var doc = new XML.XML_Overrides();
        lock (sync)
        {
            foreach (var kvp in alarmOverrides)
                if (kvp.Value.Persist) doc.Alarms.Add(FromEntry(kvp.Key, kvp.Value));
            foreach (var kvp in interlockOverrides)
                if (kvp.Value.Persist) doc.Interlocks.Add(FromEntry(kvp.Key, kvp.Value));
        }

        // Atomic write-then-rename
        var tmp = path + ".tmp";
        try
        {
            var serializer = new System.Xml.Serialization.XmlSerializer(typeof(XML.XML_Overrides));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var s = File.Create(tmp))
                serializer.Serialize(s, doc);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
        catch (Exception ex)
        {
            log.Write($"[OVERRIDE ERROR] failed to write {path}: {ex.Message}");
            log.WriteAudit(new AuditEvent
            {
                Event = "override.error",
                Message = $"failed to write {path}: {ex.Message}",
            });
            throw;
        }
    }

    private static OverrideEntry ToEntry(XML.XML_Override e)
    {
        DateTime when = DateTime.TryParse(e.DisabledAt, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeLocal, out var t) ? t : DateTime.Now;
        return new OverrideEntry(e.Reason ?? "", when, e.DisabledBy ?? "", Persist: true);
    }

    private static XML.XML_Override FromEntry(string id, OverrideEntry e)
        => new()
        {
            Id = id,
            Disabled = true,
            Reason = e.Reason,
            DisabledAt = e.DisabledAt.ToString("s", System.Globalization.CultureInfo.InvariantCulture),
            DisabledBy = e.DisabledBy,
        };

    private static readonly TimeSpan SuppressLogWindow = TimeSpan.FromSeconds(10);
    private readonly Dictionary<string, DateTime> lastSuppressedLogAt = new();

    public event EventHandler<SuppressedAlarmEventArgs>? SuppressedAlarmRefreshed;

    /// <summary>
    /// Rate-limited to one log line per alarm ID per 10 seconds.
    /// Always fires SuppressedAlarmRefreshed so the UI banner can keep its
    /// dimmed "(suppressed) …" line up to date, regardless of rate limit.
    /// </summary>
    public void LogSuppressedAlarm(string id, string message)
    {
        bool shouldLog;
        lock (sync)
        {
            var now = clock();
            shouldLog = !lastSuppressedLogAt.TryGetValue(id, out var last)
                        || last > now
                        || now - last >= SuppressLogWindow;
            if (shouldLog) lastSuppressedLogAt[id] = now;
        }
        if (shouldLog)
        {
            var reason = ReasonFor(alarmOverrides, id);
            log.Write($"[SUPPRESSED] alarm:{id} \"{message}\" reason=\"{reason}\"");
            log.WriteAudit(new AuditEvent
            {
                Event = "override.suppressed",
                Kind = "alarm",
                Id = id,
                Reason = reason,
                Message = message,
            });
        }
        SuppressedAlarmRefreshed?.Invoke(this, new SuppressedAlarmEventArgs(id, message));
    }

    /// <summary>
    /// No rate limit — interlock checks only fire at operation time.
    /// </summary>
    public void LogSuppressedInterlock(string id, string displayLabel)
    {
        var reason = ReasonFor(interlockOverrides, id);
        log.Write($"[SUPPRESSED] interlock:{id} \"{displayLabel}\" reason=\"{reason}\"");
        log.WriteAudit(new AuditEvent
        {
            Event = "override.suppressed",
            Kind = "interlock",
            Id = id,
            Reason = reason,
            Message = displayLabel,
        });
    }

    private string ReasonFor(Dictionary<string, OverrideEntry> dict, string id)
    {
        lock (sync) return dict.TryGetValue(id, out var e) ? e.Reason : "";
    }
}
