using System.Collections.Immutable;

namespace APS.RecipeModel.Ast;

/// <summary>
/// A <c>bias</c> command. Holds the full, ordered argument list verbatim so the
/// editor round-trips every runtime form without dropping tokens:
///   <c>bias VALUE</c> (on/off), <c>bias set CH PARAM VALUE</c>, <c>bias out CH Arb VALUE</c>.
/// The runtime interprets the subcommand; the model only preserves the arguments.
/// </summary>
public sealed record BiasStatement(ImmutableArray<NodeField> Args) : Statement
{
    public BiasStatement(NodeField value) : this(ImmutableArray.Create(value)) { }
}
public sealed record BiasArbStatement(
    string Channel, NodeField Voltage, NodeField Frequency, NodeField DutyCycle, NodeField Phase) : Statement;
/// <summary>
/// <c>power ID on|off</c> — energize or de-energize a supply. <paramref name="Value"/>
/// is a legacy trailing argument the runtime <b>silently discards</b> (the worker op is
/// 2-arg): <c>power RF301 on 350</c> turns the output on at whatever the last <c>set</c>
/// left, often 0 W. It is retained only so an existing recipe round-trips byte-for-byte
/// instead of being rewritten on open/save — nothing may author it. <see cref="PowerBlock"/>
/// offers no value field and the validator warns on any line that carries one.
/// </summary>
public sealed record PowerStatement(string Device, OnOff Action, NodeField Value) : Statement;
public sealed record RampStatement(string Device, NodeField Value) : Statement;
public sealed record SetStatement(string Device, string Property, NodeField Value) : Statement;
