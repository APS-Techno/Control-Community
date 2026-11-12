using System.Reflection;
using APS.SystemControl;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.Classes;
using APS.SystemControl.Core.CoreClass;
using APS.SystemControl.Core.HWClasses.Base;
using APS.SystemControl.XML;
using APS.SystemControl.XML.Core;

namespace APS.SystemControl.Core;

public partial class Core
{
    private readonly Worker? _worker = [];
    private Assembly? workerDll;
    /// <summary>
    /// Cached Operations type resolved during LoadWorker; used by ReloadSources
    /// to re-invoke Init without a hard compile-time reference to the worker DLL.
    /// </summary>
    internal Type? WorkerOpsType { get; set; }
    /// <summary>
    /// Invoked at recipe start to clear per-recipe worker state (e.g. the
    /// tracked current chamber). Wired by LoadWorker via reflection so Core
    /// keeps no compile-time dependency on the worker DLL.
    /// </summary>
    internal Action? WorkerResetRecipeState { get; set; }

    /// <summary>
    /// Filename (without extension) of the loaded worker DLL.
    /// Set in <see cref="LoadWorker"/> so the deposition snapshot can record which
    /// worker drove the run. Null until a worker is loaded.
    /// </summary>
    public string? WorkerName { get; internal set; }
    private readonly Dictionary<string, Type> typeMappings = new()
    {
        { "o", typeof(object) },
        { "i", typeof(int)    },
        { "f", typeof(float)  },
        { "b", typeof(bool)   },
        { "s", typeof(string) },
    };


    /// <summary>
    /// Registers an embedded operation. <paramref name="sArgs"/> lists ONLY the
    /// value slots the recipe writes after the device name — the device slot is
    /// prepended here as <see cref="IBaseComponent"/>, because every embedded op
    /// takes its device as arg 0. The value list must NOT repeat that device slot:
    /// the pre-run validator derives arity from this table, so an extra slot makes
    /// it demand a phantom argument.
    ///
    /// <paramref name="target"/> must be the DeviceType the op resolves against:
    /// it is what the validator checks a device name against.
    /// </summary>
    private void AddOperation(string name, OperationDelegate operation, Type returnType,
                              string[] sArgs, DeviceType target, int minArgs = -1)
    {
        Type[] mArgs = new Type[sArgs.Length + 1];
        mArgs[0] = typeof(IBaseComponent);
        for (int i = 0; i < sArgs.Length; i++)
        {
            mArgs[i + 1] = typeMappings[sArgs[i]];
        }
        // minArgs counts the device slot too, so callers state the recipe-visible
        // minimum directly (pump = device + on/off, spin-up seconds optional).
        _worker!.Add(name.ToLower(), operation, returnType, mArgs, target, null, minArgs);
    }

    public void LoadEmbeddedOperations()
    {
        AddOperation("MFC", OpMFC, typeof(float), ["f"], DeviceType.MFC);
        AddOperation("Valve", OpValve, typeof(bool), ["b"], DeviceType.Valve);
        AddOperation("Stage", OpStage, typeof(float), ["f"], DeviceType.Stage);
        AddOperation("StageStop", OpStageStop, typeof(float), [], DeviceType.Stage);
        // pump <name> <on|off> [spin-up seconds]
        AddOperation("Pump", OpPump, typeof(bool), ["b", "f"], DeviceType.Pump, minArgs: 2);

        // "isolate" takes no recipe arguments — it enumerates all connected
        // valves and MFCs from hwElements internally.  Register directly on
        // the Worker so the dispatch path can find it without a device arg.
        _worker!.Add("isolate", OpIsolate, typeof(bool), [], DeviceType.System);
    }

    public bool LoadWorker(string ConfigPath, string dllBasePath)
    {
        bool result = false;

        try
        {
            XML_Worker xml_worker = Serializer<XML_Worker>.Load(ConfigPath);
            if (xml_worker != null)
            {
                ApplyFaultPauseTimeout(xml_worker.FaultPauseTimeoutSeconds);
                ApplyHeldWarningTimeout(xml_worker.HeldWarningSeconds);

                string workerDllPath = Path.Combine(dllBasePath, xml_worker.Library!.Path!);

                // Feature gate: loading a non-stock worker DLL requires the custom-workers flag.
                // The generic worker and any library the edition bundles are always allowed.
                var workerDllFilename = Path.GetFileName(workerDllPath);
                bool isStockWorker =
                    string.Equals(workerDllFilename, "APS.Workers.Generic.dll", StringComparison.OrdinalIgnoreCase)
                    || APS.SystemControl.Hosting.ControlApp.Edition.Features.IsBundledLibrary(workerDllFilename);
                if (!isStockWorker
                    && !APS.SystemControl.Hosting.ControlApp.Edition.Features.Has(APS.SystemControl.Hosting.EditionFeatureNames.CustomWorkers))
                {
                    LastErrorMessage = $"License does not include 'custom-workers'; refusing to load {workerDllFilename}.";
                    SendErrorMesssage(LastErrorMessage);
                    return false;
                }

                // Same DLL-integrity policy as hardware libraries: if a SHA-256
                // hash is pinned in Worker.xml it MUST match; otherwise we log
                // the current hash and allow the load to proceed.
                DllHashVerifier.Verify(workerDllPath, xml_worker.Library.Sha256,
                    msg => SendLogMesssage("Core", msg));

                workerDll = Assembly.LoadFrom(workerDllPath);
                WorkerName = Path.GetFileNameWithoutExtension(workerDllPath);

                if (xml_worker.Operations != null) { LoadOperations(xml_worker.Operations); }

                if (xml_worker.Vars != null) { LoadVars(xml_worker.Vars); }

                if (xml_worker.Operations?.Typename != null)
                {
                    string configDir = Path.GetDirectoryName(ConfigPath)!;
                    ConfigDir = configDir;
                    var opsType = workerDll.GetType(xml_worker.Operations.Typename);
                    if (opsType == null)
                        // LoadOperations reports the same failure in detail (audit C20);
                        // this note covers the Init/resolver wiring skipped below.
                        SendLogMesssage("Worker",
                            $"Operations class '{xml_worker.Operations.Typename}' not found — Init and resolvers skipped.");
                    WorkerOpsType = opsType;
                    var initMethod = opsType?.GetMethod("Init", [typeof(string)]);
                    initMethod?.Invoke(null, [configDir]);

                    // Wire up the drive resolver so composite operations like
                    // Deposit can reach auxiliary drives (e.g. the shutter)
                    // without the worker DLL taking a hard dependency on Core.
                    var resolverField = opsType?.GetField("DriveResolver");
                    if (resolverField != null)
                    {
                        Func<string, BaseStepperDrive?> resolver =
                            id => FindElement(DeviceType.Drive, id) as BaseStepperDrive;
                        resolverField.SetValue(null, resolver);
                    }

                    // Wire up the step progress reporter so composite operations
                    // (Deposit's dwell, Warmup, Pwait) can push a bounded
                    // progress window to the UI. Sends a Progress ParamMsg
                    // tagged "StepProgress" with [description, totalSeconds].
                    var progressField = opsType?.GetField("StepProgressReporter");
                    if (progressField != null)
                    {
                        Action<string, double> reporter = (desc, totalSeconds) =>
                        {
                            // Fault pause: remember when this dwell began so a Retry of an
                            // interrupted deposit runs only the remaining seconds.
                            NoteDwellStarted(desc, totalSeconds);
                            WriteGUI(new ParamMsg(MsgType.Progress, DeviceType.Recipe,
                                "StepProgress", [desc, totalSeconds]));
                        };
                        progressField.SetValue(null, reporter);
                    }

                    var depositField = opsType?.GetField("DepositReporter");
                    if (depositField != null)
                    {
                        Action<int, double> depositReporter = (flange, seconds) =>
                            WriteGUI(new ParamMsg(MsgType.Progress, DeviceType.Recipe,
                                "DepositStart", [flange, seconds]));
                        depositField.SetValue(null, depositReporter);
                    }

                    // Resolve a shutter valve id → BaseValve for the cluster
                    // material-based Deposit (mirrors DriveResolver).
                    var shutterResolverField = opsType?.GetField("ShutterResolver");
                    if (shutterResolverField != null)
                    {
                        Func<string, BaseValve?> sResolver =
                            id => FindElement(DeviceType.Valve, id) as BaseValve;
                        shutterResolverField.SetValue(null, sResolver);
                    }

                    var depositGuardField = opsType?.GetField("DepositGuard");
                    if (depositGuardField != null)
                    {
                        Func<BaseStepperDrive, string, float, string, string?> guard = DepositGuardReason;
                        depositGuardField.SetValue(null, guard);
                    }

                    // Operator-log sink (source, message) so a worker op can report a
                    // long wait — e.g. Valve's "waiting for position sensor" while a
                    // panel gate valve travels.
                    var logReporterField = opsType?.GetField("LogReporter");
                    if (logReporterField != null)
                    {
                        Action<string, string> logReporter = (src, msg) => SendLogMesssage(src, msg);
                        logReporterField.SetValue(null, logReporter);
                    }

                    // Resolve a pressure-sensor id → the throttle valve that
                    // regulates its chamber, so a chamber-aware Pwait (multi-chamber
                    // worker) can drive the right valve. Maps via
                    // the sensor's Chamber + a controller whose id is prefixed
                    // by that chamber (e.g. C3_pm → chamber C3 → C3_TV).
                    var throttleResolverField = opsType?.GetField("ThrottleResolver");
                    if (throttleResolverField != null)
                    {
                        Func<string, BaseController?> tResolver = sensorId =>
                        {
                            var sensor = FindElement(DeviceType.Sensor, sensorId) as BaseSensor;
                            string chamber = sensor?.Chamber ?? CoreConstants.DefaultChamberId;
                            if (hwElements.Item.TryGetValue(DeviceType.Controller, out var ctrls) && ctrls != null)
                                foreach (var kv in ctrls)
                                    if (kv.Value is BaseController bc
                                        && kv.Key.StartsWith(chamber + "_", StringComparison.OrdinalIgnoreCase))
                                        return bc;
                            return null;
                        };
                        throttleResolverField.SetValue(null, tResolver);
                    }

                    // Resolve a pressure-sensor id → its current reading (Torr),
                    // for the chamber-aware Pwait's convergence poll.
                    var pressureReaderField = opsType?.GetField("PressureReader");
                    if (pressureReaderField != null)
                    {
                        Func<string, float?> pReader = sensorId =>
                            (FindElement(DeviceType.Sensor, sensorId) as BaseSensor)?.Value();
                        pressureReaderField.SetValue(null, pReader);
                    }

                    // Recipe-start reset hook (clears tracked current chamber).
                    var resetMethod = opsType?.GetMethod("ResetRecipeState");
                    WorkerResetRecipeState = resetMethod != null
                        ? () => { resetMethod.Invoke(null, null); }
                        : null;
                }

                SendLogMesssage("Core", $"Core worker loaded from {xml_worker.Library!.Path}");
                result = true;
            }
        }
        catch (DllHashVerifier.HashMismatchException hashEx)
        {
            LastErrorMessage = "Worker DLL integrity check failed: " + hashEx.Message;
            SendErrorMesssage(hashEx.Message);
        }
        catch (Exception ex) { LastErrorMessage = "Failed to load Worker config from file: " + ex.Message; }
        return result;
    }

    private static void LoadVars(List<XML_Variable> vars)
    {
        foreach (XML_Variable var in vars)
        {
        }
    }

    /// <summary>
    /// Convert command name to DeviceType. First tries the DeviceType enum directly
    /// (e.g. "Valve" → DeviceType.Valve), then looks up the target stored in the
    /// loaded Worker operations (from Worker.xml), falling back to DeviceType.System.
    /// </summary>
    public DeviceType FindTarget(string name)
    {
        if (Enum.TryParse(name, ignoreCase: true, out DeviceType target))
            return target;

        var op = _worker![name.ToLower()];
        if (op != null)
            return op.Target;

        return DeviceType.System;
    }

    public static string[] PrepareParts(string commandLine)
    {
        char[] separator = [' '];
        commandLine = Substitute(commandLine);

        return commandLine.Split(separator, StringSplitOptions.RemoveEmptyEntries);
    }

    public static string Substitute(string commandLine)
    {
        commandLine = commandLine.TrimStart('\t');
        char[] separator = [' '];
        string[] parts = commandLine.Split(separator, StringSplitOptions.RemoveEmptyEntries);

        // Never rewrite operator-facing message text (audit C16): the whole
        // line is token-split before the message handler runs.
        if (parts.Length > 0 && parts[0].Equals("message", StringComparison.OrdinalIgnoreCase))
            return string.Join(' ', parts);

        for (int i = 0; i < parts.Length; i++)
        {
            parts[i] = parts[i] switch
            {
                "open" => "true",
                "closed" => "false",
                "close" => "false",
                "on" => "true",
                "off" => "false",
                _ => parts[i]
            };
        }
        return string.Join(' ', parts);
    }

    private void LoadOperations(XML_Operations Operations)
    {
        // Assembly.GetType returns null (not throw) on a typo'd or renamed class
        // name; without this check ZERO operations register, boot completes
        // green, and every recipe line fails only at execution with no diagnostic
        // pointing at Worker.xml (audit C20).
        var opsClass = workerDll!.GetType(Operations.Typename);
        if (opsClass == null)
        {
            LastErrorMessage = $"Worker operations class '{Operations.Typename}' not found in the worker DLL — " +
                               "check the Operations Typename in Worker.xml. NO worker operations were registered.";
            SendErrorMesssage(LastErrorMessage);
            SendLogMesssage("Worker", LastErrorMessage);
            return;
        }

        foreach (XML_Operation op in Operations.Ops!)
        {
            var type = opsClass;
            if (type != null)
            {
                int varCount = op.Vars?.Count ?? 0;
                MethodInfo? methodInfo = WorkerMethodResolver.Resolve(type, op.Method, varCount);
                if (methodInfo != null)
                {
                    string opName = op.Method.ToLower();
                    var item = _worker![opName];
                    if (item != null)
                        _worker.Remove(opName);

                    try
                    {
                        // Detect whether the method wants a CancellationToken
                        // and whether it returns Task / Task<T>. Both sync and
                        // async methods are supported; async ones can be
                        // interrupted by a recipe abort via the token.
                        var methodParams = methodInfo.GetParameters();
                        bool wantsCt = methodParams.Length > 0
                            && methodParams[^1].ParameterType == typeof(CancellationToken);
                        Type returnType = methodInfo.ReturnType;
                        bool returnsTaskOfT = returnType.IsGenericType
                            && returnType.GetGenericTypeDefinition() == typeof(Task<>);
                        bool returnsTask = returnType == typeof(Task) || returnsTaskOfT;

                        async Task<object?> function(object[] parameters, CancellationToken ct)
                        {
                            object[] invokeArgs = wantsCt
                                ? [.. parameters, ct]
                                : parameters;
                            object? raw = methodInfo.Invoke(null, invokeArgs);
                            if (raw is Task taskResult)
                            {
                                await taskResult.ConfigureAwait(false);
                                if (returnsTaskOfT)
                                    return returnType.GetProperty("Result")?.GetValue(taskResult);
                                return null;
                            }
                            if (raw == null && returnType.IsValueType)
                                return Activator.CreateInstance(returnType);
                            return raw;
                        }
                        List<Type> argsList = [];
                        DeviceType opTarget = DeviceType.System;

                        foreach (string var in op!.Vars!)
                        {
                            bool success = Enum.TryParse(var, ignoreCase: true, out DeviceType target);
                            if (success)
                            {
                                argsList.Add(target.GetType());
                                if (opTarget == DeviceType.System)
                                    opTarget = target; // first DeviceType var is the target
                            }
                            else
                            {
                                argsList.Add(typeMappings[var]);
                            }
                        }

                        Type[] Args = [.. argsList];

                        _worker!.Add(opName, function!, typeMappings[op.Type], Args, opTarget, op.Device);
                    }
                    catch (Exception e)
                    {
                        SendErrorMesssage(e.Message);
                    }
                }
                else
                {
                    SendErrorMesssage(
                        $"Worker op '{op.Method}' (varCount={varCount}): no matching method overload found — op not registered.");
                }
            }
        }
    }

    public Operation GetOperation(string key) => _worker![key.ToLower()]!;

    /// <summary>
    /// Projects the LIVE operation table into the declaration form the recipe
    /// validator consumes, so pre-run validation is driven by exactly the
    /// signatures the executor will bind against.
    ///
    /// Reading Worker.xml a second time would not be equivalent: registration
    /// order decides which definition wins when both the embedded ops and
    /// Worker.xml declare the same name, and a registration that failed is
    /// simply absent here. Whatever is in this table is what will run.
    ///
    /// An operation whose argument shape isn't recognised is skipped rather
    /// than guessed at — a missing signature costs a check, a wrong signature
    /// would block a good recipe.
    /// </summary>
    public List<(string Name, string? BoundDevice, IReadOnlyList<string> Vars)> GetOperationDeclarations()
    {
        var result = new List<(string, string?, IReadOnlyList<string>)>();
        if (_worker == null) return result;

        foreach (var kvp in _worker)
        {
            var op = kvp.Value;
            if (op.Args == null) continue;

            var vars = new List<string>(op.Args.Length);
            bool recognised = true;

            for (int i = 0; i < op.Args.Length; i++)
            {
                var t = op.Args[i];
                // A DeviceType marker (Worker.xml <var>Valve</var>) or the
                // embedded-op IBaseComponent slot both mean "a device of this
                // operation's target type, named in the recipe".
                if (t == typeof(DeviceType) || t == typeof(IBaseComponent))
                    vars.Add(op.Target.ToString());
                else if (t == typeof(int)) vars.Add("i");
                else if (t == typeof(float)) vars.Add("f");
                else if (t == typeof(bool)) vars.Add("b");
                else if (t == typeof(string)) vars.Add("s");
                else if (t == typeof(object)) vars.Add("o");
                else { recognised = false; break; }

                // Trailing slots the op can do without are marked "?" so the
                // validator requires the mandatory prefix and tolerates the
                // rest (pump's optional spin-up seconds).
                if (i >= op.MinArgs) vars[^1] += "?";
            }

            if (recognised)
                result.Add((kvp.Key, op.Device, vars));
        }

        return result;
    }

    public Type[] GetArgs(string key) => _worker![key.ToLower()]!.Args;

    internal bool LoadEmbeddedRecipes(string scriptConfig)
    {
        bool result = false;
        try
        {
            embRecipes = new EmbeddedRecipes(scriptConfig);
            SendLogMesssage("Core", $"Embedded recipes loaded from {scriptConfig}");
            // Non-fatal parse problems (e.g. an unrecognised onStepFailure value).
            // Reported rather than thrown so one bad attribute cannot take the
            // machine's safety recipes down with the load.
            foreach (string configError in embRecipes.ConfigErrors)
                SendLogMesssage("Core", configError);
            ValidateEmbeddedRecipes();
            result = true;
        }
        catch (Exception ex)
            { LastErrorMessage = $"Failed to load embeded recipe from file {scriptConfig}:\n" + ex.Message; }
        return result;
    }

    /// <summary>
    /// Validates every embedded recipe against the live operation table at load.
    /// Embedded recipes (EmbedRecipes.xml — SYSTEM_*, LOAD_*, FAILURE_PROTOCOL)
    /// RUN, so an error in one is exactly as damaging as in an operator recipe —
    /// e.g. FAILURE_PROTOCOL addressing MFCs as "MFC 1".."MFC 4" on a machine
    /// whose are named Ar1/Ar2.
    ///
    /// Findings are logged rather than made fatal: refusing to boot would leave
    /// the operator with no control over a machine that may be under vacuum
    /// with a hot target. The recipe is still rejected when it is invoked.
    /// </summary>
    private void ValidateEmbeddedRecipes()
    {
        if (embRecipes == null) return;

        var semantics = BuildRecipeSemantics();
        if (semantics.IsEmpty) return;

        var validator = BuildRecipeValidator(semantics);

        int totalBad = 0;

        foreach (string name in embRecipes.GetKnownNames())
        {
            string[] lines = embRecipes.Recipe(name);
            if (lines.Length == 0) continue;   // intentionally empty placeholder

            var errors = validator
                .Validate(string.Join('\n', lines))
                .Where(e => e.Severity == APS.RecipeModel.Validation.RecipeErrorSeverity.Error)
                .ToList();

            foreach (var e in errors)
                SendLogMesssage("Core",
                    $"Embedded recipe '{name}' line {e.Line}: {e.Message}");

            totalBad += errors.Count;
        }

        if (totalBad > 0)
            SendErrorMesssage(
                $"{totalBad} validation error(s) in embedded recipes — see the log. " +
                "These recipes will fail if invoked.");
    }
}
