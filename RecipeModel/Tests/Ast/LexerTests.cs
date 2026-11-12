using APS.RecipeModel.Ast;

namespace APS.RecipeModel.Tests.Ast;

public class LexerTests
{
    [Fact]
    public void Tokenizes_Simple_Valve_Line()
    {
        var line = Lexer.TokenizeLine("valve M1 open", lineNumber: 1);
        Assert.Equal(3, line.Tokens.Length);
        Assert.Equal(TokenKind.Identifier, line.Tokens[0].Kind);
        Assert.Equal("valve", line.Tokens[0].Text);
        Assert.Equal("M1", line.Tokens[1].Text);
        Assert.Equal("open", line.Tokens[2].Text);
    }

    [Fact]
    public void Preserves_Leading_Whitespace_As_Trivia()
    {
        var line = Lexer.TokenizeLine("    mfc Ar1 5", lineNumber: 2);
        Assert.Single(line.LeadingWhitespace.Tokens);
        var ws = Assert.IsType<WhitespaceTrivia>(line.LeadingWhitespace.Tokens[0]);
        Assert.Equal("    ", ws.Text);
    }

    [Fact]
    public void Recognizes_Line_Comment_After_Statement()
    {
        var line = Lexer.TokenizeLine("delay 5 # wait for pumpdown", lineNumber: 3);
        Assert.Equal(2, line.Tokens.Length); // delay, 5
        Assert.Single(line.TrailingComment.Tokens);
        var comment = Assert.IsType<LineCommentTrivia>(line.TrailingComment.Tokens[0]);
        Assert.Equal("# wait for pumpdown", comment.Text);
    }

    [Fact]
    public void Recognizes_Full_Line_Comment()
    {
        var line = Lexer.TokenizeLine("// this is a section header", lineNumber: 4);
        Assert.Empty(line.Tokens);
        Assert.Single(line.TrailingComment.Tokens);
    }

    [Fact]
    public void Recognizes_Quoted_String_Token()
    {
        var line = Lexer.TokenizeLine("message \"hello world\"", lineNumber: 5);
        Assert.Equal(2, line.Tokens.Length);
        Assert.Equal(TokenKind.StringLiteral, line.Tokens[1].Kind);
        Assert.Equal("hello world", line.Tokens[1].Text);
    }

    [Fact]
    public void Recognizes_Dollar_Param()
    {
        var line = Lexer.TokenizeLine("mfc Ar1 $flow", lineNumber: 6);
        Assert.Equal(3, line.Tokens.Length);
        Assert.Equal(TokenKind.ParamRef, line.Tokens[2].Kind);
        Assert.Equal("flow", line.Tokens[2].Text);
    }
}
