using APS.RecipeModel.Validation;
using APS.SystemControl.Core.Recipe;

namespace RecipeTests;

public class RecipeBranchingTests
{
    [Fact]
    public void BuildBlockStructure_IfEndif_CreatesBlock()
    {
        var lexer = new Lexer();
        var interpreter = new Interpreter();

        var lines = lexer.Tokenize("if pm < 0.05\nvalve M1 open\nendif");
        interpreter.BuildBlockStructure(lines);

        var block = interpreter.GetBlock(0);
        Assert.NotNull(block);
        Assert.Equal(BlockKind.IfElse, block.Kind);
        Assert.Equal(0, block.StartLine);
        Assert.Equal(2, block.EndLine);
        Assert.Equal(-1, block.ElseLine);
    }

    [Fact]
    public void BuildBlockStructure_IfElseEndif_TracksElseLine()
    {
        var lexer = new Lexer();
        var interpreter = new Interpreter();

        var lines = lexer.Tokenize("if pm < 0.05\nvalve M1 open\nelse\nvalve M1 closed\nendif");
        interpreter.BuildBlockStructure(lines);

        var block = interpreter.GetBlock(0);
        Assert.NotNull(block);
        Assert.Equal(BlockKind.IfElse, block.Kind);
        Assert.Equal(0, block.StartLine);
        Assert.Equal(4, block.EndLine);
        Assert.Equal(2, block.ElseLine);

        var elseBlock = interpreter.GetBlock(2);
        Assert.NotNull(elseBlock);
        Assert.Equal(block, elseBlock);

        var endifBlock = interpreter.GetBlock(4);
        Assert.NotNull(endifBlock);
        Assert.Equal(block, endifBlock);
    }

    [Fact]
    public void BuildBlockStructure_NestedIfInLoop_Works()
    {
        var lexer = new Lexer();
        var interpreter = new Interpreter();

        string recipe = """
            loop 3
            if pm < 0.05
            valve M1 open
            endif
            endloop
            """;
        var lines = lexer.Tokenize(recipe);
        interpreter.BuildBlockStructure(lines);

        var loopBlock = interpreter.GetBlock(0);
        Assert.NotNull(loopBlock);
        Assert.Equal(BlockKind.Loop, loopBlock.Kind);

        var ifBlock = interpreter.GetBlock(1);
        Assert.NotNull(ifBlock);
        Assert.Equal(BlockKind.IfElse, ifBlock.Kind);
    }

    [Fact]
    public void BuildBlockStructure_UnmatchedIf_Throws()
    {
        var lexer = new Lexer();
        var interpreter = new Interpreter();

        var lines = lexer.Tokenize("if pm < 0.05\nvalve M1 open");
        var ex = Assert.Throws<InvalidOperationException>(() => interpreter.BuildBlockStructure(lines));
        Assert.Contains("'if' without matching 'endif'", ex.Message);
    }

    [Fact]
    public void BuildBlockStructure_UnmatchedEndif_Throws()
    {
        var lexer = new Lexer();
        var interpreter = new Interpreter();

        var lines = lexer.Tokenize("valve M1 open\nendif");
        var ex = Assert.Throws<InvalidOperationException>(() => interpreter.BuildBlockStructure(lines));
        Assert.Contains("'endif' without matching 'if'", ex.Message);
    }

    [Fact]
    public void BuildBlockStructure_UnmatchedElse_Throws()
    {
        var lexer = new Lexer();
        var interpreter = new Interpreter();

        var lines = lexer.Tokenize("else\nvalve M1 open\nendif");
        var ex = Assert.Throws<InvalidOperationException>(() => interpreter.BuildBlockStructure(lines));
        Assert.Contains("'else' without matching 'if'", ex.Message);
    }

    [Fact]
    public void BuildBlockStructure_DuplicateElse_Throws()
    {
        var lexer = new Lexer();
        var interpreter = new Interpreter();

        var lines = lexer.Tokenize("if pm < 0.05\nvalve M1 open\nelse\nvalve M1 closed\nelse\ndelay 1\nendif");
        var ex = Assert.Throws<InvalidOperationException>(() => interpreter.BuildBlockStructure(lines));
        Assert.Contains("duplicate 'else'", ex.Message);
    }

    [Fact]
    public void BuildBlockStructure_Label_RegistersInMap()
    {
        var lexer = new Lexer();
        var interpreter = new Interpreter();

        var lines = lexer.Tokenize("label start\nvalve M1 open\ngoto start");
        interpreter.BuildBlockStructure(lines);

        Assert.Equal(0, interpreter.GetLabelLine("start"));
    }

    [Fact]
    public void BuildBlockStructure_DuplicateLabel_Throws()
    {
        var lexer = new Lexer();
        var interpreter = new Interpreter();

        var lines = lexer.Tokenize("label start\nvalve M1 open\nlabel start");
        var ex = Assert.Throws<InvalidOperationException>(() => interpreter.BuildBlockStructure(lines));
        Assert.Contains("duplicate label 'start'", ex.Message);
    }

    [Fact]
    public void BuildBlockStructure_GotoUndefinedLabel_Throws()
    {
        var lexer = new Lexer();
        var interpreter = new Interpreter();

        var lines = lexer.Tokenize("goto nowhere");
        var ex = Assert.Throws<InvalidOperationException>(() => interpreter.BuildBlockStructure(lines));
        Assert.Contains("'goto' target label 'nowhere' not found", ex.Message);
    }

    [Fact]
    public void BuildBlockStructure_GotoDefinedLabel_Succeeds()
    {
        var lexer = new Lexer();
        var interpreter = new Interpreter();

        var lines = lexer.Tokenize("label retry\ndelay 1\ngoto retry");
        interpreter.BuildBlockStructure(lines);
        Assert.Equal(0, interpreter.GetLabelLine("retry"));
    }

    [Fact]
    public void BuildBlockStructure_MaxNestingDepthExceeded_Throws()
    {
        var lexer = new Lexer();
        var interpreter = new Interpreter();

        // Build 9 nested loops (exceeds max of 8)
        string recipe = "";
        for (int i = 0; i < 9; i++)
            recipe += "loop 1\n";
        recipe += "delay 1\n";
        for (int i = 0; i < 9; i++)
            recipe += "endloop\n";

        var lines = lexer.Tokenize(recipe);
        var ex = Assert.Throws<InvalidOperationException>(() => interpreter.BuildBlockStructure(lines));
        Assert.Contains("exceeds maximum nesting depth", ex.Message);
    }

    [Fact]
    public void BuildBlockStructure_ExactlyMaxDepth_Succeeds()
    {
        var lexer = new Lexer();
        var interpreter = new Interpreter();

        // Build exactly 8 nested loops (at the limit)
        string recipe = "";
        for (int i = 0; i < 8; i++)
            recipe += "loop 1\n";
        recipe += "delay 1\n";
        for (int i = 0; i < 8; i++)
            recipe += "endloop\n";

        var lines = lexer.Tokenize(recipe);
        interpreter.BuildBlockStructure(lines);
    }

    [Fact]
    public void BuildBlockStructure_MixedNestingIfLoop_CountsCorrectly()
    {
        var lexer = new Lexer();
        var interpreter = new Interpreter();

        // 4 loops + 4 ifs = 8 total = at the limit
        string recipe = "";
        for (int i = 0; i < 4; i++)
            recipe += "loop 1\n";
        for (int i = 0; i < 4; i++)
            recipe += "if pm < 0.05\n";
        recipe += "delay 1\n";
        for (int i = 0; i < 4; i++)
            recipe += "endif\n";
        for (int i = 0; i < 4; i++)
            recipe += "endloop\n";

        var lines = lexer.Tokenize(recipe);
        interpreter.BuildBlockStructure(lines);
    }

    [Theory]
    [InlineData("if pm < 1.0", 0.5f, true)]
    [InlineData("if pm < 1.0", 1.5f, false)]
    [InlineData("if pm > 1.0", 1.5f, true)]
    [InlineData("if pm > 1.0", 0.5f, false)]
    [InlineData("if pm = 1.0", 1.0f, true)]
    [InlineData("if pm = 1.0", 0.5f, false)]
    [InlineData("if pm <= 1.0", 1.0f, true)]
    [InlineData("if pm <= 1.0", 0.5f, true)]
    [InlineData("if pm <= 1.0", 1.5f, false)]
    [InlineData("if pm >= 1.0", 1.0f, true)]
    [InlineData("if pm >= 1.0", 1.5f, true)]
    [InlineData("if pm >= 1.0", 0.5f, false)]
    // Unit-suffixed pressure (Torr): SYSTEM_PUMP_OUT branches on `if pm > 10mT`.
    [InlineData("if pm > 10mT", 2.5e-3f, false)]
    [InlineData("if pm > 10mT", 0.011f, true)]
    [InlineData("if pm > 10mT", 760f, true)]
    public void EvaluateIfCondition_SensorComparison(string ifLine, float sensorValue, bool expected)
    {
        var lexer = new Lexer();
        var interpreter = new Interpreter();
        interpreter.SensorValueProvider = (_) => sensorValue;

        var tokens = lexer.Tokenize(ifLine);
        bool result = interpreter.EvaluateIfCondition(tokens[0]);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void EvaluateIfCondition_VariableComparison()
    {
        var lexer = new Lexer();
        var interpreter = new Interpreter();
        interpreter.SetVariable("threshold", 0.05f);

        var tokens = lexer.Tokenize("if $threshold < 1.0");
        bool result = interpreter.EvaluateIfCondition(tokens[0]);
        Assert.True(result);
    }

    [Fact]
    public void EvaluateIfCondition_VariableOnBothSides()
    {
        var lexer = new Lexer();
        var interpreter = new Interpreter();
        interpreter.SetVariable("current", 5.0f);
        interpreter.SetVariable("limit", 10.0f);

        var tokens = lexer.Tokenize("if $current < $limit");
        bool result = interpreter.EvaluateIfCondition(tokens[0]);
        Assert.True(result);
    }

    [Fact]
    public void EvaluateIfCondition_MissingArguments_Throws()
    {
        var lexer = new Lexer();
        var interpreter = new Interpreter();

        var tokens = lexer.Tokenize("if pm");
        var ex = Assert.Throws<InvalidOperationException>(() => interpreter.EvaluateIfCondition(tokens[0]));
        Assert.Contains("requires at least 3 arguments", ex.Message);
    }

    [Fact]
    public void EvaluateIfCondition_UndefinedVariable_Throws()
    {
        var lexer = new Lexer();
        var interpreter = new Interpreter();

        var tokens = lexer.Tokenize("if $unknown < 1.0");
        var ex = Assert.Throws<InvalidOperationException>(() => interpreter.EvaluateIfCondition(tokens[0]));
        Assert.Contains("undefined variable", ex.Message);
    }

    [Fact]
    public void EvaluateIfCondition_NoSensorProvider_Throws()
    {
        var lexer = new Lexer();
        var interpreter = new Interpreter();
        // Do NOT set SensorValueProvider

        var tokens = lexer.Tokenize("if pm < 0.05");
        var ex = Assert.Throws<InvalidOperationException>(() => interpreter.EvaluateIfCondition(tokens[0]));
        Assert.Contains("no sensor value provider", ex.Message);
    }

    [Fact]
    public void Validator_MatchedIfEndif_NoErrors()
    {
        var validator = new RecipeValidator(["valve"], ["M1"]);
        var errors = validator.Validate("if pm < 0.05\nvalve M1 open\nendif");
        // There should be no structural errors (device 'pm' is unknown but
        // that's not what we're testing here)
        Assert.DoesNotContain(errors, e => e.Message.Contains("'if'") || e.Message.Contains("'endif'"));
    }

    [Fact]
    public void Validator_UnmatchedIf_ReportsError()
    {
        var validator = new RecipeValidator(["valve"], ["M1"]);
        var errors = validator.Validate("if pm < 0.05\nvalve M1 open");
        Assert.Contains(errors, e => e.Message.Contains("'if' without matching 'endif'"));
    }

    [Fact]
    public void Validator_UnmatchedEndif_ReportsError()
    {
        var validator = new RecipeValidator(["valve"], ["M1"]);
        var errors = validator.Validate("valve M1 open\nendif");
        Assert.Contains(errors, e => e.Message.Contains("'endif' without matching 'if'"));
    }

    [Fact]
    public void Validator_ElseWithoutIf_ReportsError()
    {
        var validator = new RecipeValidator(["valve"], ["M1"]);
        var errors = validator.Validate("else\nvalve M1 open\nendif");
        Assert.Contains(errors, e => e.Message.Contains("'else' without matching 'if'"));
    }

    [Fact]
    public void Validator_DuplicateElse_ReportsError()
    {
        var validator = new RecipeValidator(["valve"], ["M1"]);
        var errors = validator.Validate("if pm < 0.05\nvalve M1 open\nelse\ndelay 1\nelse\ndelay 2\nendif");
        Assert.Contains(errors, e => e.Message.Contains("Duplicate 'else'"));
    }

    [Fact]
    public void Validator_GotoUndefinedLabel_ReportsError()
    {
        var validator = new RecipeValidator(["delay"], []);
        var errors = validator.Validate("goto nowhere\ndelay 1");
        Assert.Contains(errors, e => e.Message.Contains("'goto' target label 'nowhere' not found"));
    }

    [Fact]
    public void Validator_DuplicateLabel_ReportsError()
    {
        var validator = new RecipeValidator(["delay"], []);
        var errors = validator.Validate("label start\ndelay 1\nlabel start");
        Assert.Contains(errors, e => e.Message.Contains("Duplicate label 'start'"));
    }

    [Fact]
    public void Validator_NestingDepthExceeded_ReportsError()
    {
        var validator = new RecipeValidator(["delay"], []);
        string recipe = "";
        for (int i = 0; i < 9; i++) recipe += "loop 1\n";
        recipe += "delay 1\n";
        for (int i = 0; i < 9; i++) recipe += "endloop\n";

        var errors = validator.Validate(recipe);
        Assert.Contains(errors, e => e.Message.Contains("Nesting depth exceeds maximum"));
    }

    [Fact]
    public void Validator_IfMissingArguments_ReportsError()
    {
        var validator = new RecipeValidator(["valve"], ["M1"]);
        var errors = validator.Validate("if pm\nvalve M1 open\nendif");
        Assert.Contains(errors, e => e.Message.Contains("'if' requires at least 3 argument"));
    }

    [Fact]
    public void Validator_LabelMissingName_ReportsError()
    {
        var validator = new RecipeValidator(["delay"], []);
        var errors = validator.Validate("label\ndelay 1");
        Assert.Contains(errors, e => e.Message.Contains("'label' requires at least 1 argument"));
    }

    [Fact]
    public void Validator_GotoMissingName_ReportsError()
    {
        var validator = new RecipeValidator(["delay"], []);
        var errors = validator.Validate("goto\ndelay 1");
        Assert.Contains(errors, e => e.Message.Contains("'goto' requires at least 1 argument"));
    }

    [Theory]
    [InlineData("if", true)]
    [InlineData("else", true)]
    [InlineData("endif", true)]
    [InlineData("label", true)]
    [InlineData("goto", true)]
    [InlineData("loop", true)]
    [InlineData("endloop", true)]
    [InlineData("var", true)]
    [InlineData("function", true)]
    [InlineData("valve", false)]
    [InlineData("delay", false)]
    public void IsInterpreterKeyword_CorrectClassification(string keyword, bool expected)
    {
        Assert.Equal(expected, Interpreter.IsInterpreterKeyword(keyword));
    }

    [Fact]
    public void ExistingLoopBehavior_StillWorks()
    {
        var lexer = new Lexer();
        var interpreter = new Interpreter();

        string recipe = "loop 3\ndelay 1\nendloop";
        var lines = lexer.Tokenize(recipe);
        interpreter.BuildBlockStructure(lines);

        var block = interpreter.GetBlock(0);
        Assert.NotNull(block);
        Assert.Equal(BlockKind.Loop, block.Kind);
        Assert.Equal(0, block.StartLine);
        Assert.Equal(2, block.EndLine);
        Assert.Equal(-1, block.ElseLine);
    }

    [Fact]
    public void ExistingFunctionBehavior_StillWorks()
    {
        var lexer = new Lexer();
        var interpreter = new Interpreter();

        string recipe = "function myFunc(x)\ndelay 1\nendfunction";
        var lines = lexer.Tokenize(recipe);
        interpreter.BuildBlockStructure(lines);

        Assert.True(interpreter.HasFunction("myFunc"));
        var func = interpreter.GetFunction("myFunc");
        Assert.NotNull(func);
        Assert.Single(func.ParamNames);
        Assert.Equal("x", func.ParamNames[0]);
    }

    [Fact]
    public void ExistingVariableBehavior_StillWorks()
    {
        var lexer = new Lexer();
        var interpreter = new Interpreter();

        var lines = lexer.Tokenize("var speed = 100");
        interpreter.ProcessVarDeclaration(lines[0]);

        Assert.Equal(100, interpreter.GetVariable("speed"));
    }

    [Fact]
    public void Reset_ClearsLabels()
    {
        var lexer = new Lexer();
        var interpreter = new Interpreter();

        var lines = lexer.Tokenize("label start\ndelay 1");
        interpreter.BuildBlockStructure(lines);
        Assert.Equal(0, interpreter.GetLabelLine("start"));

        interpreter.Reset();
        Assert.Equal(-1, interpreter.GetLabelLine("start"));
    }
}
