using Flow.Cli;

namespace Flow.Cli.Tests;

public sealed class CliCommandParserTests
{
    private readonly CliCommandParser _parser = new();

    [Fact]
    public void Parse_RecognizesAllCommands()
    {
        Assert.IsType<SampleCommand>(_parser.Parse(["sample"]).Command);
        var import = Assert.IsType<ImportEpubCommand>(_parser.Parse(["import", "book.epub"]).Command);
        Assert.Equal("book.flow.json", import.OutputPath);

        var importWithOutput = Assert.IsType<ImportEpubCommand>(
            _parser.Parse(["import", "book.epub", "--output", "library/book.flow.json"]).Command);
        Assert.Equal("library/book.flow.json", importWithOutput.OutputPath);
        var epubInspect = Assert.IsType<InspectEpubCommand>(
            _parser.Parse(["epub-inspect", "book.epub", "--json", "report.json"]).Command);
        Assert.Equal("report.json", epubInspect.JsonOutputPath);
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
        Assert.StartsWith("FLOWCLI_UNKNOWN_COMMAND:", _parser.Parse(["unknown"]).Error, StringComparison.Ordinal);
        Assert.StartsWith("FLOWCLI_USAGE:", _parser.Parse(["inspect"]).Error, StringComparison.Ordinal);
        Assert.StartsWith(
            "FLOWCLI_USAGE:",
            _parser.Parse(["import", "book.epub", "--unknown", "book.flow.json"]).Error,
            StringComparison.Ordinal);
        Assert.StartsWith(
            "FLOWCLI_USAGE:",
            _parser.Parse(["epub-inspect", "book.epub", "--output", "report.json"]).Error,
            StringComparison.Ordinal);
        Assert.StartsWith(
            "FLOWCLI_USAGE:",
            _parser.Parse(["render", "book.flow.json", "--html", "book.html"]).Error,
            StringComparison.Ordinal);
        Assert.False(
            _parser.Parse(
                ["render", "book.flow.json", "--html", "one.html", "--html", "two.html", "--width", "800", "--height", "600"])
                .IsSuccess);
    }
}
