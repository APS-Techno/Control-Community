using APS.Controls.Theme;
using Xunit;

namespace APS.Controls.Tests.Theme;

public class HintTextTests
{
    [Fact] public void Short_only_has_null_long()
        => Assert.Equal(("Open the gate valve", (string?)null), HintText.Parse("Open the gate valve"));

    [Fact] public void Splits_short_and_long_on_first_pipe()
        => Assert.Equal(("Start", (string?)"Rough then turbo"), HintText.Parse("Start|Rough then turbo"));

    [Fact] public void Trims_both_sides()
        => Assert.Equal(("Start", (string?)"Long text"), HintText.Parse("  Start  |  Long text  "));

    [Fact] public void Long_may_contain_pipes()
        => Assert.Equal(("S", (string?)"a|b|c"), HintText.Parse("S|a|b|c"));

    [Fact] public void Empty_long_becomes_null()
        => Assert.Equal(("S", (string?)null), HintText.Parse("S|"));

    [Fact] public void Null_or_empty_input_is_empty_short()
    {
        Assert.Equal(("", (string?)null), HintText.Parse(""));
        Assert.Equal(("", (string?)null), HintText.Parse(null));
    }
}
