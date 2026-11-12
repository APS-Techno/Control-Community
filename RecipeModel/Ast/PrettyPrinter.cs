using System.Collections.Immutable;
using System.Text;
using APS.RecipeModel.Blocks;

namespace APS.RecipeModel.Ast;

public static class PrettyPrinter
{
    public static string Print(ScriptContainer script)
    {
        var sb = new StringBuilder();
        if (script.HasStartMarker) sb.Append("start\n");
        foreach (var node in script.Body)
            AppendNode(sb, node, indentLevel: 0);
        if (script.HasFinishMarker) sb.Append("finish\n");
        return sb.ToString();
    }

    private static void AppendNode(StringBuilder sb, AstNode node, int indentLevel)
    {
        AppendTrivia(sb, node.LeadingTrivia);

        switch (node)
        {
            case LoopContainer lp:
                AppendIndent(sb, indentLevel);
                sb.Append("loop ").Append(lp.Count.ToDslToken());
                AppendTrailing(sb, node.TrailingTrivia);
                foreach (var child in lp.Body) AppendNode(sb, child, indentLevel + 1);
                AppendTrivia(sb, lp.EndTrivia);
                AppendIndent(sb, indentLevel);
                sb.Append("endloop\n");
                return;
            case IfElseContainer ie:
                AppendIndent(sb, indentLevel);
                sb.Append("if ").Append(ie.Condition.ToDsl());
                AppendTrailing(sb, node.TrailingTrivia);
                foreach (var child in ie.Body) AppendNode(sb, child, indentLevel + 1);
                if (ie.ElseBody.HasValue)
                {
                    AppendTrivia(sb, ie.ElseTrivia);
                    AppendIndent(sb, indentLevel);
                    sb.Append("else\n");
                    foreach (var child in ie.ElseBody.Value)
                        AppendNode(sb, child, indentLevel + 1);
                }
                AppendTrivia(sb, ie.EndTrivia);
                AppendIndent(sb, indentLevel);
                sb.Append("endif\n");
                return;
            case FunctionContainer fn:
                AppendIndent(sb, indentLevel);
                sb.Append("function ").Append(fn.Name)
                  .Append('(').Append(string.Join(", ", fn.Parameters)).Append(')');
                AppendTrailing(sb, node.TrailingTrivia);
                foreach (var child in fn.Body) AppendNode(sb, child, indentLevel + 1);
                AppendTrivia(sb, fn.EndTrivia);
                AppendIndent(sb, indentLevel);
                sb.Append("endfunction\n");
                return;
            case BeginEndContainer be:
                AppendIndent(sb, indentLevel);
                sb.Append("begin");
                AppendTrailing(sb, node.TrailingTrivia);
                foreach (var child in be.Body) AppendNode(sb, child, indentLevel + 1);
                AppendTrivia(sb, be.EndTrivia);
                AppendIndent(sb, indentLevel);
                sb.Append("end\n");
                return;
        }

        // Trivia-only placeholder (dangling comments/blank lines at the end of a
        // block) carries no statement text — emitting the usual empty line here
        // would grow the script by one blank line on every round-trip.
        if (node is InvalidStatement { OriginalText: "" } && node.TrailingTrivia.Tokens.Length == 0)
            return;

        AppendIndent(sb, indentLevel);
        sb.Append(LeafToDsl((Statement)node));
        AppendTrailing(sb, node.TrailingTrivia);
    }

    private static void AppendIndent(StringBuilder sb, int level)
    {
        for (int i = 0; i < level; i++) sb.Append("  ");
    }

    private static void AppendTrailing(StringBuilder sb, Trivia trivia)
    {
        foreach (var tok in trivia.Tokens)
        {
            if (tok is not LineCommentTrivia c) continue;
            // Region markers are full-line comments by contract (see RegionTrivia).
            // A close parked in trailing trivia (region ending at the last block of
            // the script) must not be glued onto the statement line.
            if (RegionMarker.IsClose(c.Text) || RegionMarker.TryParseOpen(c.Text) is not null)
                sb.Append('\n').Append(c.Text);
            else
                sb.Append(' ').Append(c.Text);
        }
        sb.Append('\n');
    }

    private static void AppendTrivia(StringBuilder sb, Trivia trivia)
    {
        foreach (var tok in trivia.Tokens)
        {
            switch (tok)
            {
                case BlankLineTrivia b:
                    for (int i = 0; i < b.Count; i++) sb.Append('\n');
                    break;
                case LineCommentTrivia c:
                    sb.Append(c.Text).Append('\n');
                    break;
                case WhitespaceTrivia _:
                    // whitespace before a statement is reconstructed from indent level, not trivia
                    break;
            }
        }
    }

    private static string LeafToDsl(Statement s) => s switch
    {
        ValveStatement v      => $"valve {v.Device} {(v.Action == ValveAction.Open ? "open" : "close")}",
        MfcStatement m        => $"mfc {m.Device} {m.Flow.ToDslToken()}",
        PumpStatement p       => p.SpinupSeconds is null
            ? $"pump {p.Device} {(p.Action == OnOff.On ? "on" : "off")}"
            : $"pump {p.Device} {(p.Action == OnOff.On ? "on" : "off")} {p.SpinupSeconds.ToDslToken()}",
        StageStatement st     => $"stage {st.Device} {st.Position.ToDslToken()}",
        ShutterStatement sh   => $"shutter {sh.Position}",
        HolderStatement h     => $"holder {h.Position}",
        ScanStatement sc      => $"scan {sc.Device} {sc.Start.ToDslToken()} {sc.End.ToDslToken()} {sc.Speed.ToDslToken()}".TrimEnd(),
        MessageStatement msg  => $"message \"{msg.Text}\"",
        BiasStatement b       => ("bias " + string.Join(' ', b.Args.Select(a => a.ToDslToken()))).TrimEnd(),
        BiasArbStatement ba   => $"biasArb {ba.Channel} {ba.Voltage.ToDslToken()} {ba.Frequency.ToDslToken()} {ba.DutyCycle.ToDslToken()} {ba.Phase.ToDslToken()}",
        // The legacy trailing value is preserved verbatim when present so existing
        // recipes round-trip, but never invented for the normal 2-token form.
        PowerStatement pw     => $"power {pw.Device} {(pw.Action == OnOff.On ? "on" : "off")} {pw.Value.ToDslToken()}".TrimEnd(),
        RampStatement r       => $"ramp {r.Device} {r.Value.ToDslToken()}",
        SetStatement sv       => $"set {sv.Device} {sv.Property} {sv.Value.ToDslToken()}",
        DelayStatement d      => d.Caption is null ? $"delay {d.Seconds.ToDslToken()}" : $"delay {d.Seconds.ToDslToken()} \"{d.Caption}\"",
        WaitStatement w       => w.Caption is null ? $"wait {w.Seconds.ToDslToken()}" : $"wait {w.Seconds.ToDslToken()} \"{w.Caption}\"",
        PressureStatement pr  => $"pwait {pr.Sensor} {pr.TargetMTorr.ToDslToken()} {pr.TimeoutSec.ToDslToken()}",
        SwaitStatement sw     => $"swait {sw.Sensor} {sw.Target.ToDslToken()} {sw.TimeoutSec.ToDslToken()}",
        CheckStatement ch     => $"check {ch.Sensor} {OpToStr(ch.Op)} {ch.Value.ToDslToken()}",
        StateStatement stt    => $"state {stt.State.ToDslToken()}",
        LabelStatement l      => $"label {l.Name}",
        GotoStatement g       => $"goto {g.Target}",
        CallStatement cl      => $"call {cl.RecipeName}",
        VarStatement va       => $"var {va.Name} {va.Value.ToDslToken()}",
        RotateStatement rt    => $"rotate {rt.Speed.ToDslToken()}",
        RotationStatement rn  => rn.Speed is null
            ? $"rotation {(rn.Action == OnOff.On ? "on" : "off")}"
            : $"rotation {(rn.Action == OnOff.On ? "on" : "off")} {rn.Speed.ToDslToken()}",
        WarmupStatement wm    => $"warmup {wm.Time.ToDslToken()} {string.Join(' ', wm.Sources)}",
        DepositStatement dp   => $"deposit {dp.Position.ToDslToken()} {dp.Time.ToDslToken()} {dp.Retract.ToDslToken()}",
        TransferStatement tr  => $"transfer {tr.Src.ToDslToken()} {tr.Dst.ToDslToken()}",
        HomeStatement _       => "home",
        AbortStatement _      => "abort",
        FunctionCallStatement fc => $"{fc.Name} {string.Join(' ', fc.Args.Select(a => a.ToDslToken()))}".TrimEnd(),
        InvalidStatement iv   => iv.OriginalText,
        _ => $"# unhandled: {s.GetType().Name}"
    };

    private static string OpToStr(CompareOp op) => op switch
    {
        CompareOp.Eq => "=",
        CompareOp.Ne => "!=",
        CompareOp.Lt => "<",
        CompareOp.Le => "<=",
        CompareOp.Gt => ">",
        CompareOp.Ge => ">=",
        _ => "="
    };
}
