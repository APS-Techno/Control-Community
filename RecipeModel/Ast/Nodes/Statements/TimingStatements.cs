namespace APS.RecipeModel.Ast;

public enum CompareOp { Eq, Ne, Lt, Le, Gt, Ge }

public sealed record DelayStatement(NodeField Seconds, string? Caption) : Statement;
public sealed record WaitStatement(NodeField Seconds, string? Caption) : Statement;
/// <summary>pressure/pwait — sensor, target in mTorr, timeout seconds. Active PID wait (throttle valve, pm only).</summary>
public sealed record PressureStatement(string Sensor, NodeField TargetMTorr, NodeField TimeoutSec) : Statement;
/// <summary>swait — passive sensor wait. sensor, target pressure (Torr or suffixed), timeout seconds.</summary>
public sealed record SwaitStatement(string Sensor, NodeField Target, NodeField TimeoutSec) : Statement;
public sealed record CheckStatement(string Sensor, CompareOp Op, NodeField Value) : Statement;
