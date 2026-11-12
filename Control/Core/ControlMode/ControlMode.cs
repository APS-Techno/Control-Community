namespace APS.SystemControl.Core;

/// <summary>SEMI E30 control state. <see cref="Offline"/> and <see cref="Local"/> enforce identically in
/// v1 (operator owns the machine, host reads only); <see cref="Remote"/> hands process control to the
/// host (Agent wire) and locks operator commands except safety actions.</summary>
public enum ControlMode { Offline, Local, Remote }

/// <summary>Who is asking. <see cref="Host"/> = Agent wire (APS.Mcp, GEM later);
/// <see cref="Operator"/> = Control UI, XML menus, Stream Deck.</summary>
public enum CommandOrigin { Host, Operator }

/// <summary><see cref="Read"/> never gated; <see cref="Mutate"/> gated by mode; <see cref="Safety"/>
/// (abort, fault-pause decisions, stop, isolate, alarm reset, the mode switch itself) never gated for
/// the operator. A host has no Safety class — its abort/resolve are Mutate (spec §3).</summary>
public enum CommandClass { Read, Mutate, Safety }

public sealed record ControlModeChange(ControlMode From, ControlMode To, string By, DateTime Utc);

/// <param name="PersistPath">bin\ControlMode.state; null = no persistence (tests).</param>
/// <param name="RestoreRemote">AgentServer.xml &lt;ControlMode restoreRemote/&gt;.</param>
/// <param name="DevOverride">APS_DEV_CONTROL_MODE value; wins over everything when parseable.</param>
/// <param name="Log">Operator-level event sink.</param>
public sealed record ControlModeStartup(string? PersistPath, bool RestoreRemote, string? DevOverride, Action<string>? Log);
