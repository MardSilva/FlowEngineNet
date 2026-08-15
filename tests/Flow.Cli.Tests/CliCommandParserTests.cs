using Flow.Cli;

namespace Flow.Cli.Tests;

public sealed class CliCommandParserTests
{
    private readonly CliCommandParser _parser = new();

    [Fact]
    public void Parse_RecognizesAllCommands()
    {
        Assert.IsType<SampleCommand>(_parser.Parse(["sample"]).Command);
        Assert.IsType<InspectCommand>(_parser.Parse(["inspect", "book.flow.json"]).Command);
        Assert.IsType<ValidateCommand>(_parser.Parse(["validate", "book.flow.json"]).Command);
        Assert.IsType<HashCommand>(_parser.Parse(["hash", "book.flow.json"]).Command);

        var render = Assert.IsType<RenderHtmlCommand>(
            _parser.Parse(
            [
                "render",
                "book.flow.json",
                "--height",
                "844",
                "--html",
                "mobile.html",
                "--width",
                "390",
            ]).Command);

        Assert.Equal("book.flow.json", render.DocumentPath);
        Assert.Equal("mobile.html", render.OutputPath);
        Assert.Equal(390, render.ViewportWidth);
        Assert.Equal(844, render.ViewportHeight);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("wide")]
    public void Parse_RejectsInvalidViewport(string width)
    {
        var result = _parser.Parse(
            ["render", "book.flow.json", "--html", "book.html", "--width", width, "--height", "800"]);

        Assert.False(result.IsSuccess);
        Assert.Contains("--width", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RejectsUnknownOrIncompleteCommands()
    {
        Assert.False(_parser.Parse(["unknown"]).IsSuccess);
        Assert.False(_parser.Parse(["inspect"]).IsSuccess);
        Assert.False(_parser.Parse(["render", "book.flow.json", "--html", "book.html"]).IsSuccess);
        Assert.False(
            _parser.Parse(
                ["render", "book.flow.json", "--html", "one.html", "--html", "two.html", "--width", "800", "--height", "600"])
                .IsSuccess);
    }
}
