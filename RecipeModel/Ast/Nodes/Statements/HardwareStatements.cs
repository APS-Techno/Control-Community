namespace APS.RecipeModel.Ast;

public enum ValveAction { Open, Close }
public enum OnOff { On, Off }

public sealed record ValveStatement(string Device, ValveAction Action) : Statement;
public sealed record MfcStatement(string Device, NodeField Flow) : Statement;
public sealed record PumpStatement(string Device, OnOff Action, NodeField? SpinupSeconds = null) : Statement;
public sealed record StageStatement(string Device, NodeField Position) : Statement;
public sealed record ShutterStatement(string Position) : Statement;
public sealed record HolderStatement(string Position) : Statement;
/// <summary>
/// <c>scan ID START END SPEED</c> — one sweep of a stepper drive. Mirrors the
/// worker op <c>Operations.Scan(drive, start, end, speed)</c>.
/// </summary>
public sealed record ScanStatement(string Device, NodeField Start, NodeField End, NodeField Speed) : Statement;
public sealed record MessageStatement(string Text) : Statement;
