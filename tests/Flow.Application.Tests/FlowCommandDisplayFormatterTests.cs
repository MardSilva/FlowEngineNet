using Flow.Application;

namespace Flow.Application.Tests;

public sealed class FlowCommandDisplayFormatterTests
{
    [Fact]
    public void PowerShellDisplayEscapesApostrophesAndKeepsTypedArguments()
    {
        var result = FlowCommandDisplayFormatter.Format(
            FlowCommandShell.PowerShell,
            "flow",
            ["import", "C:\\Books\\Autor's book.epub", "--output", "C:\\Output\\book.flow.json"]);

        Assert.Equal(FlowCommandShell.PowerShell, result.Shell);
        Assert.Equal("Autor's book.epub", Path.GetFileName(result.Arguments[1]));
        Assert.Equal(
            "& 'flow' 'import' 'C:\\Books\\Autor''s book.epub' '--output' 'C:\\Output\\book.flow.json'",
            result.Text);
    }

    [Fact]
    public void CommandPromptDisplayQuotesMetacharactersForCopying()
    {
        var result = FlowCommandDisplayFormatter.Format(
            FlowCommandShell.CommandPrompt,
            "flow",
            ["import", "C:\\Books\\a&b%20!.epub"]);

        Assert.Equal("\"flow\" \"import\" \"C:\\Books\\a&b^%20^!.epub\"", result.Text);
    }

    [Theory]
    [InlineData("line\nbreak")]
    [InlineData("carriage\rreturn")]
    [InlineData("null\0character")]
    public void FormatterRejectsMultilineAndNullTokens(string argument)
    {
        Assert.Throws<ArgumentException>(() => FlowCommandDisplayFormatter.Format(
            FlowCommandShell.PowerShell,
            "flow",
            [argument]));
    }
}
