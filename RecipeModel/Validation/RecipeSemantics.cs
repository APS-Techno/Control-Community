using System.Xml.Linq;

namespace APS.RecipeModel.Validation;

public enum ArgKind
{
    /// <summary>A device name that must resolve within <see cref="ArgSpec.DeviceKind"/>.</summary>
    Device,
    /// <summary>Float literal, invariant culture (Worker.xml 'f').</summary>
    Number,
    /// <summary>Integer literal, invariant culture (Worker.xml 'i').</summary>
    Integer,
    /// <summary>open/close/closed/on/off/true/false (Worker.xml 'b').</summary>
    Bool,
    /// <summary>Free text (Worker.xml 's' / 'o'). Material and face slots are
    /// checked separately by the material/robot phases.</summary>
    Text,
    /// <summary>Time literal with optional s/m/h suffix, via TimeParser.</summary>
    Time,
    /// <summary>Pressure literal with optional unit suffix, via PressureParser.</summary>
    Pressure,
}

/// <param name="DeviceKind">For <see cref="ArgKind.Device"/>: the DeviceType
/// name the device must belong to (e.g. "Power", "Valve"). Null otherwise.</param>
public readonly record struct ArgSpec(ArgKind Kind, string? DeviceKind = null);

/// <summary>
/// The recipe-visible signature of one command: the argument slots an author
/// actually writes, in order. A bound device (Worker.xml <c>device="..."</c>)
/// is auto-injected by the executor and is therefore NOT part of this list.
/// </summary>
/// <param name="Name">Command name, lower-case.</param>
/// <param name="Variadic">When true, extra trailing arguments are allowed
/// (e.g. <c>warmup TIME [SRC...]</c>).</param>
/// <param name="MinArgs">How many of <paramref name="Args"/> the author must
/// write. Null means all of them. A lower value marks the trailing slots
/// optional — <c>pump NAME on [seconds]</c> is complete with two.</param>
public sealed record CommandSignature(string Name, IReadOnlyList<ArgSpec> Args, bool Variadic = false, int? MinArgs = null)
{
    public int RequiredArgs => MinArgs ?? Args.Count;
}

/// <summary>
/// Over-pressure ceilings a chamber's alarm rules enforce. Null ceiling = no such rule.
/// </summary>
/// <param name="IdleDelaySeconds">Settling delay of the idle rule; 0 trips on the first sample.</param>
/// <param name="IdleUnless">The idle rule's guard text (e.g. "LL_A1 open"), or null.</param>
public sealed record ChamberAlarmLimits(
    float? IdleCeilingTorr, double IdleDelaySeconds, string? IdleUnless, float? ProcessingCeilingTorr);

/// <summary>
/// Everything the validator needs to decide whether a recipe line is
/// executable: the signature of every command, and the device names available
/// under each DeviceType.
///
/// Validation is driven from the same declarations the executor binds against
/// (Worker.xml -> <c>Operation.Args</c>), so the two cannot drift: a command
/// added to Worker.xml is fully checked without anyone updating a list here.
/// </summary>
public sealed class RecipeSemantics
{
    private static readonly (string DeviceKind, string HwConfCategory)[] DeviceKindCategories =
    [
        ("Valve",      "Valves"),
        ("MFC",        "MFCs"),
        ("Pump",       "Pumps"),
        ("Stage",      "Stages"),
        ("Sensor",     "Sensors"),
        ("Power",      "Power Supplies"),
        ("Drive",      "Drives"),
        ("Controller", "Controllers"),
    ];

    /// <summary>
    /// Worker.xml type keys, mirroring Core.LoadWorker.typeMappings. Anything
    /// that is not one of these is a DeviceType name.
    /// </summary>
    private static readonly Dictionary<string, ArgKind> ScalarKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["i"] = ArgKind.Integer,
        ["f"] = ArgKind.Number,
        ["b"] = ArgKind.Bool,
        ["s"] = ArgKind.Text,
        ["o"] = ArgKind.Text,
    };

    /// <summary>
    /// Commands the interpreter handles itself, BEFORE any worker dispatch
    /// (CoreThread.Execute.cs ExecuteTokenLines). Their signatures are fixed in
    /// code rather than declared in Worker.xml, and they take precedence over a
    /// worker op of the same name — e.g. Worker.xml declares 'warmup' and
    /// 'pwait', but the interpreter intercepts both and pre-parses their
    /// literals with TimeParser / PressureParser.
    /// </summary>
    private static readonly Dictionary<string, CommandSignature> InterpreterSignatures =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // delay/wait accept an optional trailing note — recipes across this
            // config use `wait 0.5 "Pipe cleaning..."`. The executor reads only
            // parts[1], so the note is documentation, not an argument.
            ["delay"]    = new("delay",    [new(ArgKind.Time)], Variadic: true),
            ["wait"]     = new("wait",     [new(ArgKind.Time)], Variadic: true),
            // 'pwait' is deliberately absent — its arity depends on the loaded
            // worker and it is checked by RecipeValidator.ValidatePwait:
            //   BOUND worker    pwait is bound to one controller, which is both the
            //                   throttle actuator and the pressure sensor, so the
            //                   canonical form is `pwait <target> <timeout>`.
            //   UNBOUND worker  the sensor id selects which chamber's throttle to
            //                   drive (ThrottleResolver), so
            //                   `pwait <sensor> <target> <timeout>` is required.
            // 'swait' always needs its sensor — it polls that sensor directly.
            ["swait"]    = new("swait",    [new(ArgKind.Text), new(ArgKind.Pressure), new(ArgKind.Time)]),
            ["warmup"]   = new("warmup",   [new(ArgKind.Time)], Variadic: true),
            ["loop"]     = new("loop",     [new(ArgKind.Integer)]),
            ["state"]    = new("state",    [new(ArgKind.Text)]),
            ["message"]  = new("message",  [new(ArgKind.Text)], Variadic: true),
            ["call"]     = new("call",     [new(ArgKind.Text)]),
            ["goto"]     = new("goto",     [new(ArgKind.Text)]),
            ["label"]    = new("label",    [new(ArgKind.Text)]),
            ["check"]    = new("check",    [new(ArgKind.Text), new(ArgKind.Text), new(ArgKind.Text)]),
            ["if"]       = new("if",       [new(ArgKind.Text), new(ArgKind.Text), new(ArgKind.Text)]),
            ["rotation"] = new("rotation", [new(ArgKind.Text)], Variadic: true),
            ["transfer"] = new("transfer", [new(ArgKind.Text), new(ArgKind.Text)]),
            ["isolate"]  = new("isolate",  []),
            ["home"]     = new("home",     []),
            ["abort"]    = new("abort",    []),
            ["begin"]    = new("begin",    []),
            ["end"]      = new("end",      []),
            ["start"]    = new("start",    []),
            ["finish"]   = new("finish",   []),
            ["endloop"]  = new("endloop",  []),
            ["else"]     = new("else",     []),
            ["endif"]    = new("endif",    []),
            ["endfunction"] = new("endfunction", []),
        };

    private readonly Dictionary<string, CommandSignature> _signatures;
    private readonly Dictionary<string, HashSet<string>> _devicesByKind;
    private readonly Dictionary<string, string> _boundOps;
    private readonly Dictionary<string, ChamberAlarmLimits> _chamberLimits;
    private readonly Dictionary<string, string> _deviceChamber;
    private readonly Dictionary<string, IReadOnlyCollection<string>> _setpointNames;

    private RecipeSemantics(
        Dictionary<string, CommandSignature> signatures,
        Dictionary<string, HashSet<string>> devicesByKind,
        Dictionary<string, string>? boundOps = null,
        Dictionary<string, IReadOnlyCollection<string>>? setpointNames = null,
        IReadOnlyDictionary<string, string>? sourceToPsu = null,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>>? mfcGateValves = null,
        bool rotationOperational = true,
        float gaugeFloorTorr = DefaultGaugeFloorTorr,
        IReadOnlyDictionary<string, ChamberAlarmLimits>? chamberLimits = null,
        IReadOnlyDictionary<string, string>? deviceChamber = null)
    {
        _signatures = signatures;
        _devicesByKind = devicesByKind;
        _boundOps = boundOps ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _chamberLimits = new Dictionary<string, ChamberAlarmLimits>(
            chamberLimits ?? new Dictionary<string, ChamberAlarmLimits>(), StringComparer.OrdinalIgnoreCase);
        _deviceChamber = new Dictionary<string, string>(
            deviceChamber ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        _setpointNames = setpointNames ?? new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);
        SourceToPsu = new Dictionary<string, string>(
            sourceToPsu ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        MfcGateValves = new Dictionary<string, IReadOnlyCollection<string>>(
            mfcGateValves ?? new Dictionary<string, IReadOnlyCollection<string>>(), StringComparer.OrdinalIgnoreCase);
        RotationOperational = rotationOperational;
        GaugeFloorTorr = gaugeFloorTorr;
        AllDeviceNames = devicesByKind.Values
            .SelectMany(v => v)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public const float DefaultGaugeFloorTorr = 1e-5f;

    /// <summary>Effective source name to the id of the power supply that feeds it.</summary>
    public IReadOnlyDictionary<string, string> SourceToPsu { get; }

    /// <summary>MFC id to the inlet valves, any one of which passes its gas. Absent = ungated.</summary>
    public IReadOnlyDictionary<string, IReadOnlyCollection<string>> MfcGateValves { get; }

    public bool RotationOperational { get; }

    public float GaugeFloorTorr { get; }

    public IReadOnlyCollection<string> AllDeviceNames { get; }

    /// <summary>
    /// True when no signatures were supplied. The validator treats this as
    /// "cannot vouch for anything" rather than "everything is fine" — see
    /// <see cref="RecipeValidator"/>'s default-deny handling.
    /// </summary>
    public bool IsEmpty => _signatures.Count == 0;

    /// <summary>An empty catalogue. A validator holding this cannot approve a recipe.</summary>
    public static RecipeSemantics Empty { get; } = new([], []);

    public CommandSignature? Signature(string command)
        => _signatures.TryGetValue(command, out var sig) ? sig : null;

    /// <summary>
    /// True when the loaded worker declares this command with a bound device
    /// (Worker.xml <c>device="..."</c>), i.e. the executor injects the device
    /// rather than reading it from the recipe.
    ///
    /// For <c>pwait</c> this is what distinguishes the two machine families:
    /// a bound pwait drives one fixed controller that is also the gauge, so a
    /// sensor argument is inert; an unbound pwait uses the sensor id to pick
    /// which chamber's throttle to drive.
    /// </summary>
    public bool HasBoundDevice(string command) => _boundOps.ContainsKey(command);

    /// <summary>The device Worker.xml binds to this command, or null when the command is unbound or unknown.</summary>
    public string? BoundDeviceOf(string op) => _boundOps.TryGetValue(op, out var device) ? device : null;

    public string? ChamberOf(string deviceId) => _deviceChamber.TryGetValue(deviceId, out var chamber) ? chamber : null;

    public ChamberAlarmLimits? LimitsOf(string chamber) => _chamberLimits.TryGetValue(chamber, out var limits) ? limits : null;

    /// <summary>
    /// The setpoint parameter names a power supply's driver accepts in
    /// <c>set &lt;device&gt; &lt;name&gt; &lt;value&gt;</c> — exactly the strings
    /// its <c>ApplySetpoint</c> switch matches (case-sensitive, aliases
    /// included). Null when the device declared no list (unknown device, or a
    /// catalogue built from config files with no live drivers): such a
    /// device's parameter names are not checked.
    /// </summary>
    public IReadOnlyCollection<string>? SetpointNames(string device)
        => _setpointNames.TryGetValue(device, out var names) ? names : null;

    public bool IsDeviceOfKind(string deviceKind, string name)
        => _devicesByKind.TryGetValue(deviceKind, out var names) && names.Contains(name);

    public bool HasDevicesOfKind(string deviceKind)
        => _devicesByKind.TryGetValue(deviceKind, out var names) && names.Count > 0;

    public IReadOnlyCollection<string> DevicesOfKind(string deviceKind)
        => _devicesByKind.TryGetValue(deviceKind, out var names) ? names : [];

    /// <summary>
    /// Builds the catalogue from raw operation declarations. This is the entry
    /// point Core uses at runtime, passing the SAME (method, device, vars)
    /// triples it feeds into its own operation table — so production validation
    /// is driven by the live table, not by a re-read of the file.
    /// </summary>
    /// <param name="operations">
    /// (command name, bound-device name or null, Worker.xml var strings).
    /// The var strings are the raw declarations: a DeviceType name
    /// ("Valve", "Power", ...) or a scalar key ("s", "f", "b", "i", "o").
    /// </param>
    /// <param name="setpointNamesByDevice">
    /// Power-supply device name to the setpoint parameter names its driver
    /// accepts (<c>BasePowerSupply.SetpointParameterNames</c>). Devices absent
    /// here are not checked. Null when no live drivers are available.
    /// </param>
    /// <param name="sourceToPsu">Effective source name to the power supply that feeds it.</param>
    /// <param name="mfcGateValves">MFC id to its inlet valves (any one open passes gas).</param>
    /// <param name="rotationOperational">False when the machine cannot turn the substrate.</param>
    /// <param name="gaugeFloorTorr">Lowest pressure the throttle gauge can read.</param>
    /// <param name="chamberLimits">Chamber id to the over-pressure ceilings its alarm rules enforce.</param>
    /// <param name="deviceChamber">Sensor, MFC and controller id to the chamber it belongs to.</param>
    public static RecipeSemantics Build(
        IEnumerable<(string Name, string? BoundDevice, IReadOnlyList<string> Vars)> operations,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> devicesByKind,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>>? setpointNamesByDevice = null,
        IReadOnlyDictionary<string, string>? sourceToPsu = null,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>>? mfcGateValves = null,
        bool rotationOperational = true,
        float gaugeFloorTorr = DefaultGaugeFloorTorr,
        IReadOnlyDictionary<string, ChamberAlarmLimits>? chamberLimits = null,
        IReadOnlyDictionary<string, string>? deviceChamber = null)
    {
        var signatures = new Dictionary<string, CommandSignature>(StringComparer.OrdinalIgnoreCase);
        var boundOps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, boundDevice, vars) in operations)
        {
            if (boundDevice != null) boundOps[name] = boundDevice;

            // A bound device is injected by the executor and never written in
            // the recipe, so its declaration is dropped from the visible args.
            var visible = boundDevice != null && vars.Count > 0 ? vars.Skip(1) : vars;

            var specs = new List<ArgSpec>();
            int? minArgs = null;
            foreach (string var in visible)
            {
                // A trailing '?' marks the slot optional (Core emits it for an
                // operation whose MinArgs is short of its argument count; a
                // Worker.xml <var>f?</var> declares the same thing). Everything
                // from the first optional slot on may be omitted.
                bool optional = var.EndsWith('?');
                string key = optional ? var[..^1] : var;
                if (optional) minArgs ??= specs.Count;

                specs.Add(ScalarKinds.TryGetValue(key, out var kind)
                    ? new ArgSpec(kind)
                    : new ArgSpec(ArgKind.Device, key));
            }

            signatures[name] = new CommandSignature(name.ToLowerInvariant(), specs, MinArgs: minArgs);
        }

        // Interpreter-handled commands win over a worker op of the same name —
        // the executor checks for them first, so their real contract is the
        // interpreter's, not Worker.xml's.
        foreach (var (name, sig) in InterpreterSignatures)
            signatures[name] = sig;

        var byKind = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (kind, names) in devicesByKind)
            byKind[kind] = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);

        Dictionary<string, IReadOnlyCollection<string>>? setpointNames = null;
        if (setpointNamesByDevice is not null)
        {
            setpointNames = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (device, names) in setpointNamesByDevice)
                if (names is { Count: > 0 })
                    setpointNames[device] = names;
        }

        return new RecipeSemantics(signatures, byKind, boundOps, setpointNames,
            sourceToPsu, mfcGateValves, rotationOperational, gaugeFloorTorr, chamberLimits, deviceChamber);
    }

    /// <summary>
    /// Builds the catalogue from config files, for callers with no live Core —
    /// the RecipeEditor's Validate button and the test suite.
    /// Returns <see cref="Empty"/> when the files are missing or unreadable.
    /// </summary>
    public static RecipeSemantics FromConfig(string workerXmlPath, string hwConfPath)
    {
        var ops = ReadWorkerOperations(workerXmlPath);
        var devices = ReadDevicesByKind(hwConfPath);
        if (ops.Count == 0 && devices.Count == 0)
            return Empty;
        return Build(ops, devices, rotationOperational: !HWConfDeviceReader.ReadNonOperationalStages(hwConfPath).Contains("rotation"));
    }

    /// <summary>
    /// Parses <c>&lt;operations&gt;/&lt;op method="..." device="..."&gt;&lt;var&gt;</c>
    /// declarations out of a Worker.xml. Mirrors Core.LoadWorker's reading of
    /// the same file.
    /// </summary>
    public static List<(string Name, string? BoundDevice, IReadOnlyList<string> Vars)>
        ReadWorkerOperations(string workerXmlPath)
    {
        var result = new List<(string, string?, IReadOnlyList<string>)>();
        if (!File.Exists(workerXmlPath))
            return result;

        try
        {
            var doc = XDocument.Load(workerXmlPath);
            foreach (var op in doc.Descendants("op"))
            {
                string? method = op.Attribute("method")?.Value;
                if (string.IsNullOrWhiteSpace(method)) continue;

                string? device = op.Attribute("device")?.Value;
                var vars = op.Elements("var")
                    .Select(v => v.Value.Trim())
                    .Where(v => v.Length > 0)
                    .ToList();

                result.Add((method.ToLowerInvariant(), device, vars));
            }
        }
        catch (Exception)
        {
            // A malformed Worker.xml yields no signatures, which makes the
            // validator refuse to vouch for recipes rather than wave them
            // through — see IsEmpty.
            return [];
        }

        return result;
    }

    public static Dictionary<string, IReadOnlyCollection<string>> ReadDevicesByKind(string hwConfPath)
    {
        var byCategory = HWConfDeviceReader.ReadDevicesByCategory(hwConfPath);
        var result = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (deviceKind, category) in DeviceKindCategories)
        {
            if (byCategory.TryGetValue(category, out var names) && names.Count > 0)
                result[deviceKind] = names;
        }

        return result;
    }
}
