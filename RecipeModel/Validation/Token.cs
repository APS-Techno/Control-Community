namespace APS.RecipeModel.Validation;

public enum TokenType
{
    Command,
    Identifier,
    Number,
    String,
    Equals,
    Comma,
    OpenParen,
    CloseParen,
    Comment,
    EOL,
    Dollar,
    Colon
}

public record Token(TokenType Type, string Value, int Line, int Column);
