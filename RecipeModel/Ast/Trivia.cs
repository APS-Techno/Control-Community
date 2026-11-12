using System.Collections.Immutable;

namespace APS.RecipeModel.Ast;

public abstract record TriviaToken;

/// <summary>Raw whitespace (spaces/tabs) that is not a line break.</summary>
public sealed record WhitespaceTrivia(string Text) : TriviaToken;

/// <summary>One or more consecutive blank lines (no tokens on them).</summary>
public sealed record BlankLineTrivia(int Count) : TriviaToken;

/// <summary>A full-line comment, including the leading "#" or "//".</summary>
public sealed record LineCommentTrivia(string Text) : TriviaToken;

public sealed record Trivia(ImmutableArray<TriviaToken> Tokens)
{
    public static Trivia Empty { get; } = new(ImmutableArray<TriviaToken>.Empty);

    public bool Equals(Trivia? other)
    {
        if (other is null) return false;
        return Tokens.SequenceEqual(other.Tokens);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var token in Tokens)
        {
            hash.Add(token);
        }
        return hash.ToHashCode();
    }
}
