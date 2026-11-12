using System.Xml.Serialization;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.XML.Core;

namespace APS.Workers.Generic;

public static partial class Operations
{
    private const string ShutterDriveId = "Shutter";
    private const int MaxFlange = 8;
    private const int HomeFlange = 0;

    /// <summary>Resolver: drive id → drive. Wired by Core.LoadWorker.</summary>
    public static Func<string, BaseStepperDrive?>? DriveResolver;

    /// <summary>Reports that a deposit dwell starts now on a flange. Args: (flange, seconds). Wired by Core.LoadWorker.</summary>
    public static Action<int, double>? DepositReporter;

    /// <summary>Resolver: shutter valve id → valve. Wired by Core.LoadWorker.</summary>
    public static Func<string, BaseValve?>? ShutterResolver;

    /// <summary>
    /// Pre-open veto (holder, source, seconds, retract) → blocking reason or null.
    /// Checked after the holder reaches the source and before the shutter moves.
    /// </summary>
    public static Func<BaseStepperDrive, string, float, string, string?>? DepositGuard;

    internal static int DriveIdleTimeoutMs = 30_000;
    internal static int DriveSettleMs = 200;
    internal static int DrivePollMs = 50;

    private sealed record SourceEntry(string Name, int Flange, string? Chamber, string? ShutterId);

    private static volatile Dictionary<string, List<SourceEntry>> _sources =
        new(StringComparer.OrdinalIgnoreCase);

    private static volatile string? _currentChamber;

    private static void LoadSourceMap(string configDir)
    {
        var map = new Dictionary<string, List<SourceEntry>>(StringComparer.OrdinalIgnoreCase);
        string path = Path.Combine(configDir, "Sources.xml");
        if (File.Exists(path))
        {
            try
            {
                XML_Sources? doc;
                using (var reader = File.OpenText(path))
                    doc = (XML_Sources?)new XmlSerializer(typeof(XML_Sources)).Deserialize(reader);

                foreach (var source in doc?.Sources ?? [])
                {
                    string name = !string.IsNullOrWhiteSpace(source.Name)
                        ? source.Name.Trim()
                        : source.Material?.Trim() ?? "";
                    if (name.Length == 0) continue;

                    string? chamber = string.IsNullOrWhiteSpace(source.Chamber) ? null : source.Chamber.Trim();
                    string? shutterId = string.IsNullOrWhiteSpace(source.ShutterId) ? null : source.ShutterId.Trim();
                    if (!map.TryGetValue(name, out var list))
                        map[name] = list = [];
                    list.Add(new SourceEntry(name, source.FlangeNumber, chamber, shutterId));
                }
            }
            catch (Exception ex)
            {
                map.Clear();
                System.Diagnostics.Trace.TraceError($"Sources.xml: failed to load '{path}': {ex.Message}");
            }
        }
        _sources = map;
    }

    private static bool TryResolveSource(string token, bool inCurrentChamber,
        out SourceEntry entry, out string error)
    {
        entry = null!;
        var snapshot = _sources;
        string key = token.Trim();
        string? chamber = inCurrentChamber ? _currentChamber : null;

        if (!snapshot.TryGetValue(key, out var candidates))
        {
            string known = snapshot.Count > 0 ? string.Join(", ", snapshot.Keys) : "none (Sources.xml not loaded)";
            error = $"'{token}' is not an installed source. Known sources: {known}";
            return false;
        }

        if (chamber != null)
        {
            candidates = candidates
                .Where(c => string.Equals(c.Chamber, chamber, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (candidates.Count == 0)
            {
                error = $"no source '{token}' in chamber '{chamber}'";
                return false;
            }
        }

        if (candidates.Count > 1)
        {
            error = inCurrentChamber && chamber == null
                ? $"source '{token}' exists in several chambers — transfer into one first"
                : $"source '{token}' is ambiguous: several sources share this name";
            return false;
        }

        entry = candidates[0];
        error = "";
        return true;
    }

    /// <summary>
    /// Drive position token → flange: <c>Home</c> and <c>0</c> → <see cref="HomeFlange"/>,
    /// a literal flange <c>1..8</c>, or an installed source name. Anything else is refused
    /// before any motion is commanded.
    /// </summary>
    private static bool TryResolveDrivePosition(string token, out int flange, out string error)
    {
        flange = HomeFlange;
        error = "";
        string key = token.Trim();

        if (key.Equals("Home", StringComparison.OrdinalIgnoreCase) || key == "0")
            return true;

        if (_sources.ContainsKey(key))
        {
            if (!TryResolveSource(key, inCurrentChamber: false, out var entry, out error))
                return false;
            if (entry.Flange < 1)
            {
                error = $"source '{token}' has no flange in Sources.xml";
                return false;
            }
            flange = entry.Flange;
            return true;
        }

        if (int.TryParse(key, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out int literal)
            && literal is >= 1 and <= MaxFlange)
        {
            flange = literal;
            return true;
        }

        var snapshot = _sources;
        string known = snapshot.Count > 0 ? string.Join(", ", snapshot.Keys) : "none";
        error = $"'{token}' is not Home, 0, a flange 1..{MaxFlange} or an installed source (known sources: {known})";
        return false;
    }

    public static async Task<object?> Holder(BaseStepperDrive drive, string position, CancellationToken ct)
    {
        if (!drive.Connected)
            return Fail("Holder", $"drive {drive.Params.ID} is not connected");
        if (!TryResolveDrivePosition(position, out int flange, out string error))
            return Fail("Holder", error);

        if (!await MoveDriveAsync(drive, flange, "Holder", position, ct).ConfigureAwait(false))
            return null;
        return drive.CurrentPosition;
    }

    public static Task<object?> Shutter(BaseStepperDrive drive, string position, CancellationToken ct)
        => Holder(drive, position, ct);

    /// <summary>
    /// Holder-and-shutter deposit: the holder moves over <paramref name="position"/>, the
    /// shutter drive (id <c>Shutter</c> through <see cref="DriveResolver"/>) opens over the
    /// same flange for <paramref name="durationSeconds"/> and then parks at
    /// <paramref name="retractPosition"/>. Without a connected shutter drive the dwell is timed
    /// on the holder alone. The holder is never moved after the dwell.
    /// </summary>
    public static async Task<object?> Deposit(BaseStepperDrive holder, string position,
        float durationSeconds, string retractPosition, CancellationToken ct)
    {
        const string op = "Deposit";
        if (!holder.Connected)
            return Fail(op, $"drive {holder.Params.ID} is not connected");
        if (!float.IsFinite(durationSeconds) || durationSeconds < 0f)
            return Fail(op, $"invalid duration '{Inv(durationSeconds, "g")}'");
        if (!TryResolveDrivePosition(position, out int sourceFlange, out string error))
            return Fail(op, error);
        if (sourceFlange == HomeFlange)
            return Fail(op, $"'{position}' is not a deposition position");
        if (!TryResolveDrivePosition(retractPosition, out int retractFlange, out error))
            return Fail(op, error);

        if (!await MoveDriveAsync(holder, sourceFlange, op, position, ct).ConfigureAwait(false))
            return null;

        string? veto = DepositGuard?.Invoke(holder, position, durationSeconds, retractPosition);
        if (veto != null)
            return Fail(op, veto);

        var shutter = DriveResolver?.Invoke(ShutterDriveId);
        if (shutter == null || !shutter.Connected)
        {
            DepositReporter?.Invoke(sourceFlange, durationSeconds);
            StepProgressReporter?.Invoke($"Deposit {position}", durationSeconds);
            await DwellAsync(durationSeconds, ct).ConfigureAwait(false);
            return holder.CurrentPosition;
        }

        if (!await MoveDriveAsync(shutter, sourceFlange, op, position, ct).ConfigureAwait(false))
            return null;

        DepositReporter?.Invoke(sourceFlange, durationSeconds);
        StepProgressReporter?.Invoke($"Deposit {position}", durationSeconds);
        bool dwellCompleted = false;
        try
        {
            await DwellAsync(durationSeconds, ct).ConfigureAwait(false);
            dwellCompleted = true;
        }
        finally
        {
            if (!dwellCompleted)
            {
                try { CommandDrive(shutter, retractFlange); }
                catch (Exception ex)
                {
                    Fail(op, $"shutter {shutter.Params.ID} retract after an interrupted dwell failed: {ex.Message}");
                }
            }
        }

        if (!await MoveDriveAsync(shutter, retractFlange, op, retractPosition, ct).ConfigureAwait(false))
            return null;
        return holder.CurrentPosition;
    }

    /// <summary>
    /// Source-addressed deposit: opens the shutter valve of the installed source
    /// <paramref name="position"/> (Sources.xml <c>shutterId</c>) for
    /// <paramref name="durationSeconds"/>, then closes it. After a <see cref="Transfer"/> the
    /// source is looked up in the chamber the wafer is in.
    /// </summary>
    public static async Task<object?> Deposit(string position, float durationSeconds, CancellationToken ct)
    {
        const string op = "Deposit";
        if (!float.IsFinite(durationSeconds) || durationSeconds < 0f)
            return Fail(op, $"invalid duration '{Inv(durationSeconds, "g")}'");
        if (!TryResolveSource(position, inCurrentChamber: true, out var source, out string error))
            return Fail(op, error);
        if (source.ShutterId == null)
            return Fail(op, $"source '{position}' has no shutterId in Sources.xml");

        var shutter = ShutterResolver?.Invoke(source.ShutterId);
        if (shutter == null || !shutter.Connected)
            return Fail(op, $"shutter '{source.ShutterId}' of source '{position}' is not available");

        shutter.Open();
        if (!shutter.LastWriteVerified)
        {
            shutter.Close();
            return Fail(op, $"shutter {shutter.Params.ID} open command was not verified");
        }

        DepositReporter?.Invoke(source.Flange, durationSeconds);
        StepProgressReporter?.Invoke($"Deposit {position}", durationSeconds);
        try
        {
            await DwellAsync(durationSeconds, ct).ConfigureAwait(false);
        }
        finally
        {
            shutter.Close();
        }

        if (!shutter.LastWriteVerified)
            return Fail(op, $"shutter {shutter.Params.ID} close command was not verified — it may still be open");
        return shutter.IsOpen;
    }

    private static async Task DwellAsync(float seconds, CancellationToken ct)
    {
        int totalMs = (int)(seconds * 1000);
        int elapsedMs = 0;
        while (elapsedMs < totalMs)
        {
            int step = Math.Min(1000, totalMs - elapsedMs);
            await Task.Delay(step, ct).ConfigureAwait(false);
            elapsedMs += step;
        }
    }

    private static void CommandDrive(BaseStepperDrive drive, int flange)
    {
        if (flange == HomeFlange) drive.Home();
        else drive.MoveToFlangeQueued(flange);
    }

    private static async Task<bool> MoveDriveAsync(BaseStepperDrive drive, int flange,
        string operation, string position, CancellationToken ct)
    {
        try
        {
            CommandDrive(drive, flange);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Fail(operation, $"drive {drive.Params.ID} rejected the move to '{position}': {ex.Message}");
            return false;
        }

        if (await WaitForDriveIdle(drive, DriveIdleTimeoutMs, ct).ConfigureAwait(false))
            return true;
        Fail(operation,
            $"drive {drive.Params.ID} did not finish moving to '{position}' within {DriveIdleTimeoutMs / 1000}s");
        return false;
    }

    private static async Task<bool> WaitForDriveIdle(BaseStepperDrive drive, int timeoutMs, CancellationToken ct)
    {
        await Task.Delay(DriveSettleMs, ct).ConfigureAwait(false);
        int elapsed = 0;
        while (drive.IsMoving && elapsed < timeoutMs)
        {
            await Task.Delay(DrivePollMs, ct).ConfigureAwait(false);
            elapsed += DrivePollMs;
        }
        return !drive.IsMoving;
    }
}
