using System.Collections.Immutable;

namespace APS.RecipeModel.Ast;

public static class Parser
{
    public static ScriptContainer Parse(string text)
    {
        // Early return for empty input (prevents spurious trailing-trivia InvalidStatement)
        if (string.IsNullOrEmpty(text))
        {
            return new ScriptContainer
            {
                Id = NodeId.FromPosition("script", parentPath: "", indexInParent: 0),
                Body = ImmutableArray<AstNode>.Empty,
            };
        }

        var rawLines = text.Replace("\r\n", "\n").Split('\n');
        // A source that ends with '\n' produces a spurious empty last element — drop it so it
        // doesn't generate phantom trailing blank-line trivia.
        if (rawLines.Length > 0 && rawLines[^1] == "")
            rawLines = rawLines[..^1];
        var lexed = new List<LexedLine>();
        for (int i = 0; i < rawLines.Length; i++)
        {
            var segments = Lexer.SplitStatements(rawLines[i]);
            if (segments.Count == 1)
            {
                // No top-level ';' — tokenize verbatim so blank and comment-only
                // lines keep producing their trivia.
                lexed.Add(Lexer.TokenizeLine(segments[0], i + 1));
            }
            else
            {
                // Multiple statements on one source line: emit each as its own
                // LexedLine and drop empty segments (e.g. a trailing ';').
                foreach (var seg in segments)
                    if (seg.Trim().Length > 0)
                        lexed.Add(Lexer.TokenizeLine(seg, i + 1));
            }
        }

        bool hasStart = false, hasFinish = false;

        // Peel off a `start` at the beginning and a `finish` at the end, if present.
        int first = 0, last = lexed.Count - 1;
        while (first <= last && IsBlankOrCommentOnly(lexed[first])) first++;
        if (first <= last && FirstKeyword(lexed[first]) == "start") { hasStart = true; first++; }
        while (first <= last && IsBlankOrCommentOnly(lexed[last])) last--;
        if (first <= last && FirstKeyword(lexed[last]) == "finish") { hasFinish = true; last--; }

        var inner = lexed.GetRange(first, Math.Max(0, last - first + 1));
        var ctx = new ParseCtx(inner);
        var body = ParseBlock(ctx, parentPath: "script", stopAt: null);

        return new ScriptContainer
        {
            Id = NodeId.FromPosition("script", parentPath: "", indexInParent: 0),
            Body = body,
            HasStartMarker = hasStart,
            HasFinishMarker = hasFinish,
        };

        static bool IsBlankOrCommentOnly(LexedLine l) =>
            l.Tokens.Length == 0 && l.TrailingComment.Tokens.Length == 0;
        static string FirstKeyword(LexedLine l) =>
            l.Tokens.Length == 0 ? "" : l.Tokens[0].Text.ToLowerInvariant();
    }

    internal sealed class ParseCtx
    {
        public List<LexedLine> Lines { get; }
        public int Index;
        public ParseCtx(List<LexedLine> lines) { Lines = lines; }
        public bool AtEnd => Index >= Lines.Count;
        public LexedLine Peek() => Lines[Index];
    }

    /// <summary>
    /// Parses a sequence of statements until it hits a line whose first-identifier
    /// text is in <paramref name="stopAt"/>, or until end of input. The stop line is NOT consumed.
    /// </summary>
    internal static ImmutableArray<AstNode> ParseBlock(ParseCtx ctx, string parentPath, HashSet<string>? stopAt)
    {
        var result = ImmutableArray.CreateBuilder<AstNode>();
        int childIndex = 0;
        Trivia pendingTrivia = Trivia.Empty;
        int blankRun = 0;

        while (!ctx.AtEnd)
        {
            var line = ctx.Peek();

            // Blank line → aggregate into BlankLineTrivia
            if (line.Tokens.Length == 0 && line.TrailingComment.Tokens.Length == 0)
            {
                blankRun++;
                ctx.Index++;
                continue;
            }

            // Pure comment line → attach as leading trivia to next statement
            if (line.Tokens.Length == 0 && line.TrailingComment.Tokens.Length > 0)
            {
                pendingTrivia = AppendTrivia(pendingTrivia, blankRun, line.TrailingComment);
                blankRun = 0;
                ctx.Index++;
                continue;
            }

            var kw = line.Tokens[0].Text.ToLowerInvariant();

            if (stopAt != null && stopAt.Contains(kw))
                break;

            var stmt = ParseOne(ctx, parentPath, childIndex, FlushPending());
            result.Add(stmt);
            childIndex++;
        }

        // Any dangling comment-only trivia is dropped onto an empty InvalidStatement placeholder
        if (pendingTrivia.Tokens.Length > 0 || blankRun > 0)
        {
            result.Add(new InvalidStatement("", "trailing trivia")
            {
                Id = NodeId.FromPosition("trailing", parentPath, childIndex),
                LeadingTrivia = FlushPending(),
            });
        }

        return result.ToImmutable();

        Trivia FlushPending()
        {
            var flushed = AppendTrivia(pendingTrivia, blankRun, Trivia.Empty);
            pendingTrivia = Trivia.Empty;
            blankRun = 0;
            return flushed;
        }
    }

    private static Trivia AppendTrivia(Trivia acc, int blankCount, Trivia extra)
    {
        var b = ImmutableArray.CreateBuilder<TriviaToken>();
        b.AddRange(acc.Tokens);
        if (blankCount > 0) b.Add(new BlankLineTrivia(blankCount));
        b.AddRange(extra.Tokens);
        return new Trivia(b.ToImmutable());
    }

    // Placeholder NodeId used when constructing statement records in the switch expression;
    // immediately replaced by the real Id via the trailing `with` expression.
    private static readonly NodeId _placeholder = new NodeId("00000000");

    private static AstNode ParseOne(ParseCtx ctx, string parentPath, int childIndex, Trivia leading)
    {
        var line = ctx.Peek();
        var kw = line.Tokens[0].Text.ToLowerInvariant();

        switch (kw)
        {
            case "loop":     return ParseLoop(ctx, parentPath, childIndex, leading);
            case "if":       return ParseIfElse(ctx, parentPath, childIndex, leading);
            case "function": return ParseFunction(ctx, parentPath, childIndex, leading);
            case "begin":    return ParseBeginEnd(ctx, parentPath, childIndex, leading);
        }

        ctx.Index++; // leaf consumes its own line
        var id = (string kind) => NodeId.FromPosition(kind, parentPath, childIndex);
        string Text(int idx) => idx < line.Tokens.Length ? line.Tokens[idx].Text : "";
        NodeField Field(int idx)
        {
            if (idx >= line.Tokens.Length) return NodeField.Literal("");
            var t = line.Tokens[idx];
            return t.Kind == TokenKind.ParamRef ? NodeField.Param(t.Text) : NodeField.Literal(t.Text);
        }
        // Collects every token from startIdx to end as ordered NodeFields, preserving
        // param-ref typing. Used by commands with a variable argument count (bias).
        ImmutableArray<NodeField> Fields(int startIdx)
        {
            if (startIdx >= line.Tokens.Length) return ImmutableArray<NodeField>.Empty;
            var b = ImmutableArray.CreateBuilder<NodeField>(line.Tokens.Length - startIdx);
            for (int i = startIdx; i < line.Tokens.Length; i++)
                b.Add(Field(i));
            return b.ToImmutable();
        }
        var ph = _placeholder;

        // Grammar: valve [device] <open|close>. The action is the TRAILING keyword and the
        // device precedes it. When the device is cleared the block serializes to "valve  open",
        // which tokenizes to [valve, open]; that lone action keyword must parse as an EMPTY
        // device, not resurrect "open" as the device (the round-trip must not heal an empty field).
        Statement ParseValve()
        {
            static bool IsAct(string t) =>
                t.Equals("open", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("close", StringComparison.OrdinalIgnoreCase);
            static ValveAction Act(string t) =>
                t.Equals("close", StringComparison.OrdinalIgnoreCase) ? ValveAction.Close : ValveAction.Open;

            if (IsAct(Text(2)))
                return new ValveStatement(Text(1), Act(Text(2))) { Id = ph };
            if (IsAct(Text(1)) && line.Tokens.Length <= 2)
                return new ValveStatement("", Act(Text(1))) { Id = ph };
            return new ValveStatement(Text(1), ValveAction.Open) { Id = ph };
        }

        Statement leaf = kw switch
        {
            "valve"  => ParseValve(),
            "mfc"    => new MfcStatement(Text(1), Field(2)) { Id = ph },
            "pump"   => new PumpStatement(Text(1),
                Text(2).Equals("off", StringComparison.OrdinalIgnoreCase) ? OnOff.Off : OnOff.On,
                !Text(2).Equals("off", StringComparison.OrdinalIgnoreCase) && line.Tokens.Length > 3
                    ? Field(3) : null) { Id = ph },
            "stage"  => new StageStatement(Text(1), Field(2)) { Id = ph },
            "shutter"=> new ShutterStatement(Text(1)) { Id = ph },
            "holder" => new HolderStatement(Text(1)) { Id = ph },
            "scan"   => new ScanStatement(Text(1), Field(2), Field(3), Field(4)) { Id = ph },
            "message"=> new MessageStatement(line.Tokens.Length > 1 ? line.Tokens[1].Text : "") { Id = ph },
            "bias"   => new BiasStatement(Fields(1)) { Id = ph },
            "biasarb"=> new BiasArbStatement(Text(1), Field(2), Field(3), Field(4), Field(5)) { Id = ph },
            "power"  => new PowerStatement(Text(1),
                Text(2).Equals("off", StringComparison.OrdinalIgnoreCase) ? OnOff.Off : OnOff.On, Field(3)) { Id = ph },
            "ramp"   => new RampStatement(Text(1), Field(2)) { Id = ph },
            "set"    => new SetStatement(Text(1), Text(2), Field(3)) { Id = ph },
            "delay"  => new DelayStatement(Field(1),
                line.Tokens.Length > 2 && line.Tokens[2].Kind == TokenKind.StringLiteral
                    ? line.Tokens[2].Text : null) { Id = ph },
            "wait"   => new WaitStatement(Field(1),
                line.Tokens.Length > 2 && line.Tokens[2].Kind == TokenKind.StringLiteral
                    ? line.Tokens[2].Text : null) { Id = ph },
            "pwait"  => new PressureStatement(Text(1), Field(2), Field(3)) { Id = ph },
            "swait"  => new SwaitStatement(Text(1), Field(2), Field(3)) { Id = ph },
            "check"  => new CheckStatement(Text(1), ParseCompareOp(Text(2)), Field(3)) { Id = ph },
            "state"  => new StateStatement(Field(1)) { Id = ph },
            "label"  => new LabelStatement(Text(1)) { Id = ph },
            "goto"   => new GotoStatement(Text(1)) { Id = ph },
            "call"   => new CallStatement(Text(1)) { Id = ph },
            "var"    => new VarStatement(Text(1), Field(2)) { Id = ph },
            "rotate" => new RotateStatement(Field(1)) { Id = ph },
            "rotation" => new RotationStatement(
                Text(1).Equals("off", StringComparison.OrdinalIgnoreCase) ? OnOff.Off : OnOff.On,
                line.Tokens.Length > 2 ? Field(2) : null) { Id = ph },
            "deposit" => new DepositStatement(Field(1), Field(2), Field(3)) { Id = ph },
            "warmup"  => new WarmupStatement(Field(1), CollectTail(line, 2)) { Id = ph },
            "transfer" => new TransferStatement(Field(1), Field(2)) { Id = ph },
            "home"     => new HomeStatement() { Id = ph },
            "abort"    => new AbortStatement() { Id = ph },
            "start" or "finish" => new InvalidStatement(kw, "script marker handled at container level") { Id = ph },
            _ => new InvalidStatement(line.RawText.Trim(), $"unknown keyword '{kw}'") { Id = ph }
        };

        return leaf with
        {
            Id = id(kw),
            LeadingTrivia = PrependTrivia(line.LeadingWhitespace, leading),
            TrailingTrivia = line.TrailingComment,
        };
    }

    private static LoopContainer ParseLoop(ParseCtx ctx, string parentPath, int childIndex, Trivia leading)
    {
        var head = ctx.Peek();
        ctx.Index++;
        var id = NodeId.FromPosition("loop", parentPath, childIndex);
        var body = ParseBlock(ctx, $"{parentPath}/{id}", stopAt: new() { "endloop" });
        Trivia endTrivia = Trivia.Empty;
        if (!ctx.AtEnd && ctx.Peek().Tokens.Length > 0 &&
            ctx.Peek().Tokens[0].Text.Equals("endloop", StringComparison.OrdinalIgnoreCase))
        {
            endTrivia = ctx.Peek().TrailingComment;
            ctx.Index++;
        }
        return new LoopContainer
        {
            Id = id,
            Body = body,
            Count = head.Tokens.Length > 1
                ? (head.Tokens[1].Kind == TokenKind.ParamRef
                    ? NodeField.Param(head.Tokens[1].Text)
                    : NodeField.Literal(head.Tokens[1].Text))
                : NodeField.Literal("1"),
            LeadingTrivia = PrependTrivia(head.LeadingWhitespace, leading),
            TrailingTrivia = head.TrailingComment,
            EndTrivia = endTrivia,
        };
    }

    private static IfElseContainer ParseIfElse(ParseCtx ctx, string parentPath, int childIndex, Trivia leading)
    {
        var head = ctx.Peek();
        ctx.Index++;
        var id = NodeId.FromPosition("if", parentPath, childIndex);

        var cond = ParseCondition(head.Tokens, startIndex: 1);

        var thenBody = ParseBlock(ctx, $"{parentPath}/{id}/then", stopAt: new() { "else", "endif" });

        ImmutableArray<AstNode>? elseBody = null;
        Trivia elseTrivia = Trivia.Empty;
        if (!ctx.AtEnd && ctx.Peek().Tokens.Length > 0 &&
            ctx.Peek().Tokens[0].Text.Equals("else", StringComparison.OrdinalIgnoreCase))
        {
            elseTrivia = ctx.Peek().TrailingComment;
            ctx.Index++;
            elseBody = ParseBlock(ctx, $"{parentPath}/{id}/else", stopAt: new() { "endif" });
        }

        Trivia endTrivia = Trivia.Empty;
        if (!ctx.AtEnd && ctx.Peek().Tokens.Length > 0 &&
            ctx.Peek().Tokens[0].Text.Equals("endif", StringComparison.OrdinalIgnoreCase))
        {
            endTrivia = ctx.Peek().TrailingComment;
            ctx.Index++;
        }

        return new IfElseContainer
        {
            Id = id,
            Body = thenBody,
            ElseBody = elseBody,
            Condition = cond,
            LeadingTrivia = PrependTrivia(head.LeadingWhitespace, leading),
            TrailingTrivia = head.TrailingComment,
            ElseTrivia = elseTrivia,
            EndTrivia = endTrivia,
        };
    }

    private static FunctionContainer ParseFunction(ParseCtx ctx, string parentPath, int childIndex, Trivia leading)
    {
        var head = ctx.Peek();
        ctx.Index++;
        var id = NodeId.FromPosition("function", parentPath, childIndex);

        string name = head.Tokens.Length > 1 ? head.Tokens[1].Text : "";
        var paramsB = ImmutableArray.CreateBuilder<string>();
        for (int i = 2; i < head.Tokens.Length; i++)
        {
            if (head.Tokens[i].Kind == TokenKind.Identifier)
                paramsB.Add(head.Tokens[i].Text);
        }

        var body = ParseBlock(ctx, $"{parentPath}/{id}", stopAt: new() { "endfunction" });

        Trivia endTrivia = Trivia.Empty;
        if (!ctx.AtEnd && ctx.Peek().Tokens.Length > 0 &&
            ctx.Peek().Tokens[0].Text.Equals("endfunction", StringComparison.OrdinalIgnoreCase))
        {
            endTrivia = ctx.Peek().TrailingComment;
            ctx.Index++;
        }

        return new FunctionContainer
        {
            Id = id,
            Body = body,
            Name = name,
            Parameters = paramsB.ToImmutable(),
            LeadingTrivia = PrependTrivia(head.LeadingWhitespace, leading),
            TrailingTrivia = head.TrailingComment,
            EndTrivia = endTrivia,
        };
    }

    private static BeginEndContainer ParseBeginEnd(ParseCtx ctx, string parentPath, int childIndex, Trivia leading)
    {
        var head = ctx.Peek();
        ctx.Index++;
        var id = NodeId.FromPosition("begin", parentPath, childIndex);
        var body = ParseBlock(ctx, $"{parentPath}/{id}", stopAt: new() { "end" });
        Trivia endTrivia = Trivia.Empty;
        if (!ctx.AtEnd && ctx.Peek().Tokens.Length > 0 &&
            ctx.Peek().Tokens[0].Text.Equals("end", StringComparison.OrdinalIgnoreCase))
        {
            endTrivia = ctx.Peek().TrailingComment;
            ctx.Index++;
        }
        return new BeginEndContainer
        {
            Id = id,
            Body = body,
            LeadingTrivia = PrependTrivia(head.LeadingWhitespace, leading),
            TrailingTrivia = head.TrailingComment,
            EndTrivia = endTrivia,
        };
    }

    private static ConditionExpr ParseCondition(ImmutableArray<Token> tokens, int startIndex)
    {
        if (tokens.Length <= startIndex)
            return new ConditionExpr("", "=", NodeField.Literal(""));

        string lhs = tokens[startIndex].Kind == TokenKind.ParamRef
            ? $"${tokens[startIndex].Text}"
            : tokens[startIndex].Text;

        string op = "=";
        NodeField rhs = NodeField.Literal("");

        if (tokens.Length > startIndex + 1 && tokens[startIndex + 1].Kind == TokenKind.Operator)
            op = tokens[startIndex + 1].Text;

        if (tokens.Length > startIndex + 2)
        {
            var r = tokens[startIndex + 2];
            rhs = r.Kind == TokenKind.ParamRef ? NodeField.Param(r.Text) : NodeField.Literal(r.Text);
        }

        return new ConditionExpr(lhs, op, rhs);
    }

    private static CompareOp ParseCompareOp(string s) => s switch
    {
        "=" or "==" => CompareOp.Eq,
        "!=" => CompareOp.Ne,
        "<" => CompareOp.Lt,
        "<=" => CompareOp.Le,
        ">" => CompareOp.Gt,
        ">=" => CompareOp.Ge,
        _ => CompareOp.Eq,
    };

    private static ImmutableArray<string> CollectTail(LexedLine line, int startIndex)
    {
        if (startIndex >= line.Tokens.Length)
            return ImmutableArray<string>.Empty;
        var b = ImmutableArray.CreateBuilder<string>(line.Tokens.Length - startIndex);
        for (int i = startIndex; i < line.Tokens.Length; i++)
            b.Add(line.Tokens[i].Text);
        return b.ToImmutable();
    }

    private static Trivia PrependTrivia(Trivia lineLeading, Trivia preceding)
    {
        if (preceding.Tokens.Length == 0) return lineLeading;
        var b = ImmutableArray.CreateBuilder<TriviaToken>();
        b.AddRange(preceding.Tokens);
        b.AddRange(lineLeading.Tokens);
        return new Trivia(b.ToImmutable());
    }

}
