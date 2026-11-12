namespace APS.RecipeModel.Ast;

public enum TokenKind
{
    Identifier,
    Number,
    StringLiteral,  // content of a "..." pair (without the quotes)
    ParamRef,       // $name (without the $)
    Operator,       // <, >, =, ==, !=, <=, >=, +, -, (, ), ,
}

public readonly record struct Token(TokenKind Kind, string Text, SourceSpan Span);
