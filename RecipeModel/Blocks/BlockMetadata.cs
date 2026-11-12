namespace APS.RecipeModel.Blocks;

public readonly record struct BlockInfo(string Syntax, string Description);

/// <summary>Single source of truth for palette hints, validation messages,
/// and Language Reference rendering.</summary>
public static class BlockMetadata
{
    public static IReadOnlyDictionary<RecipeBlockKind, BlockInfo> Map { get; } =
        new Dictionary<RecipeBlockKind, BlockInfo>
        {
            [RecipeBlockKind.Valve]    = new("valve ID open|close",         "Valve control"),
            [RecipeBlockKind.Mfc]      = new("mfc ID SCCM",                 "MFC flow setpoint"),
            [RecipeBlockKind.Pump]     = new("pump ID on|off [SEC]",        "Pump on/off; [SEC] waits for spin-up (skipped if already running)"),
            [RecipeBlockKind.Stage]    = new("stage ID POSITION",           "Move stage to an absolute position"),
            [RecipeBlockKind.Shutter]  = new("shutter SOURCE",              "Move the shutter drive to a source (Sources.xml name, or Home)"),
            [RecipeBlockKind.Holder]   = new("holder SOURCE",               "Move the holder drive to a source (Sources.xml name, or Home)"),
            [RecipeBlockKind.Scan]     = new("scan ID START END SPEED",     "Scan a drive once from START to END"),
            [RecipeBlockKind.Message]  = new("message \"TEXT\"",            "Show message dialog (no ';' in the text — it splits the line)"),
            [RecipeBlockKind.Transfer] = new("transfer SRC DST",            "Move wafer between robot faces"),
            [RecipeBlockKind.Home]     = new("home",                        "Home the transfer robot"),
            [RecipeBlockKind.Abort]    = new("abort",                       "Abort current robot motion"),

            [RecipeBlockKind.Bias]     = new("bias VALUE | set CH PARAM V | out CH Arb V", "Bias on/off, set parameter, or ARB output"),
            [RecipeBlockKind.BiasArb]  = new("biasArb CH V FREQ DUTY DELAY", "Arbitrary bias waveform"),
            [RecipeBlockKind.Power]    = new("power ID on|off",             "Energize/de-energize ONLY — a trailing value is dropped; set the level with 'set' first"),
            [RecipeBlockKind.Ramp]     = new("ramp ID VALUE",               "Sets the level immediately — does NOT ramp gradually"),
            [RecipeBlockKind.Set]      = new("set ID PARAM VALUE",          "Setpoint: Power|Voltage|Current|Frequency|DutyCycle (case-sensitive long names)"),

            [RecipeBlockKind.Warmup]   = new("warmup TIME [SRC...]",        "Timed dwell in SECONDS — does NOT energize; the source list is informational"),
            [RecipeBlockKind.Deposit]  = new("deposit SOURCE TIME [RETRACT]", "Dwell over SOURCE's flange — energize the supply with 'power ... on' first"),
            [RecipeBlockKind.Rotation] = new("rotation on|off [SPEED]",     "Substrate rotation"),
            [RecipeBlockKind.Rotate]   = new("rotate SPEED",                "Set rotation speed"),

            [RecipeBlockKind.Delay]    = new("delay TIME [\"caption\"]",    "Wait — bare = seconds, or 30s / 5m / 1h"),
            [RecipeBlockKind.Wait]     = new("wait TIME [\"caption\"]",     "Alias for delay"),
            [RecipeBlockKind.Pressure] = new("pwait SENSOR TARGET TIMEOUT", "Active wait — drives the throttle-valve PID; only the throttle sensor (pm / *_pm)"),
            [RecipeBlockKind.Swait]    = new("swait SENSOR TARGET TIMEOUT", "Passive wait — polls any pressure sensor (pl, ...)"),
            [RecipeBlockKind.Check]    = new("check SENSOR COND VALUE",     "Assert sensor value"),

            [RecipeBlockKind.Loop]     = new("loop COUNT ... endloop",      "Loop N times"),
            [RecipeBlockKind.IfElse]   = new("if COND ... else ... endif",  "Conditional branch"),
            [RecipeBlockKind.BeginEnd] = new("begin ... end",               "No-op block markers"),
            [RecipeBlockKind.Label]    = new("label NAME",                  "Jump target"),
            [RecipeBlockKind.Goto]     = new("goto NAME",                   "Jump to label"),
            [RecipeBlockKind.Function] = new("function name(p1,p2) ... endfunction", "Define function"),
            [RecipeBlockKind.State]    = new("state startup|idle|processing", "Set the system/alarm state"),

            [RecipeBlockKind.Call]     = new("call NAME",                   "Run an embedded recipe inline (max nesting 8)"),
            [RecipeBlockKind.Var]      = new("var name value",              "Declare variable"),
        };

    public static BlockInfo Get(RecipeBlockKind kind) =>
        Map.TryGetValue(kind, out var info)
            ? info
            : throw new KeyNotFoundException($"No BlockMetadata entry for {kind}. Add it to BlockMetadata.cs.");
}
