using System.Collections.Immutable;
using APS.RecipeModel.Ast;

namespace APS.RecipeModel.Blocks;

public enum ParseStatus { Ok, Error }

public sealed class AstSyncEngine : IDisposable
{
    private readonly RichTextBox _text;
    private readonly BlockStackControl _stack;
    private readonly DebounceTimer _debouncer;
    private readonly HashSet<object> _hooked = new();
    private bool _suppress;
    private ScriptContainer? _lastAst;

    public ParseStatus Status { get; private set; } = ParseStatus.Ok;
    public string? LastError { get; private set; }
    public event Action? StatusChanged;

    public AstSyncEngine(RichTextBox text, BlockStackControl stack, int debounceMs = 300)
    {
        _text = text;
        _stack = stack;
        _debouncer = new DebounceTimer(debounceMs);

        _text.TextChanged += OnTextChanged;
        _debouncer.Elapsed += TextToBlocks;
        WireSubscriptions();
    }

    /// <summary>Run text→blocks immediately, bypassing the debouncer.</summary>
    public void SynchronizeNow()
    {
        _debouncer.Cancel();
        TextToBlocks();
    }

    private void OnTextChanged(object? sender, EventArgs e)
    {
        if (_suppress) return;
        _debouncer.Ping();
    }

    private void TextToBlocks()
    {
        _suppress = true;
        try
        {
            ScriptContainer ast;
            try { ast = Parser.Parse(_text.Text); }
            catch (Exception ex) { SetStatus(ParseStatus.Error, ex.Message); return; }

            SetStatus(ParseStatus.Ok, null);

            var oldBody = _lastAst?.Body ?? ImmutableArray<AstNode>.Empty;
            AstPatcher.Patch(_stack, oldBody, ast.Body);
            _lastAst = ast;

            WireSubscriptions();
        }
        finally { _suppress = false; }
    }

    private void OnBlocksChanged()
    {
        if (_suppress) return;
        _suppress = true;
        try
        {
            var script = new ScriptContainer
            {
                Id = _lastAst?.Id ?? NodeId.FromPosition("script", "", 0),
                Body = _stack.Blocks.Select(b => b.ToAst()).ToImmutableArray(),
                HasStartMarker = _lastAst?.HasStartMarker ?? false,
                HasFinishMarker = _lastAst?.HasFinishMarker ?? false,
            };
            string printed = PrettyPrinter.Print(script);
            // Assigning identical text still resets the RichTextBox undo stack, caret
            // and scroll position — only touch it when the script actually changed.
            if (!string.Equals(printed, _text.Text, StringComparison.Ordinal))
                _text.Text = printed;
            _lastAst = script;
        }
        finally { _suppress = false; }
    }

    /// <summary>Run <paramref name="action"/> with both sync directions suppressed.
    /// Use around operations that raise <see cref="BlockBase.FieldChanged"/> without
    /// changing the AST — e.g. assigning <see cref="BlockStackControl.Context"/>, which
    /// repopulates every block's combo boxes — so the text is not re-serialised
    /// (canonicalising the user's formatting and wiping undo history).</summary>
    public void RunSuppressed(Action action)
    {
        bool prev = _suppress;
        _suppress = true;
        try { action(); }
        finally { _suppress = prev; }
    }

    private void WireSubscriptions() => WireStack(_stack);

    private void WireStack(BlockStackControl stack)
    {
        if (_hooked.Add(stack))
            stack.StackChanged += StackChangedHandler;
        foreach (var b in stack.Blocks) WireBlock(b);
    }

    private void WireBlock(BlockBase block)
    {
        if (_hooked.Add(block))
            block.FieldChanged += OnBlocksChanged;
        if (block is IContainerBlock c)
            foreach (var m in c.Mouths) WireStack(m);
    }

    private void StackChangedHandler()
    {
        WireSubscriptions();
        OnBlocksChanged();
    }

    private void SetStatus(ParseStatus s, string? error)
    {
        if (s == Status && error == LastError) return;
        Status = s;
        LastError = error;
        StatusChanged?.Invoke();
    }

    public void Dispose()
    {
        _text.TextChanged -= OnTextChanged;
        _debouncer.Dispose();
    }
}
