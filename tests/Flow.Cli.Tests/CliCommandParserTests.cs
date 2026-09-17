using Flow.Cli;
using Flow.Rendering.Html;

namespace Flow.Cli.Tests;

public sealed class CliCommandParserTests
{
    private readonly CliCommandParser _parser = new();

    [Fact]
    public void Parse_RecognizesAllCommands()
    {
        Assert.IsType<SampleCommand>(_parser.Parse(["sample"]).Command);
        var import = Assert.IsType<ImportEpubCommand>(_parser.Parse(["import", "book.epub"]).Command);
        Assert.Null(import.OutputPath);
        Assert.Null(import.DiagnosticsJsonOutputPath);

        var importWithOutput = Assert.IsType<ImportEpubCommand>(
            _parser.Parse(
            [
                "import",
                "book.epub",
                "--diagnostics-json",
                "import-report.json",
                "--output",
                "library/book.flow.json",
                "--fidelity-report",
                "fidelity.json",
                "--metadata-json",
                "metadata.json",
                "--processing-json",
                "processing.json",
                "--source-map-json",
                "source-map.json",
            ]).Command);
        Assert.Equal("library/book.flow.json", importWithOutput.OutputPath);
        Assert.Equal("import-report.json", importWithOutput.DiagnosticsJsonOutputPath);
        Assert.Equal("fidelity.json", importWithOutput.FidelityReportOutputPath);
        Assert.Equal("metadata.json", importWithOutput.MetadataJsonOutputPath);
        Assert.Equal("processing.json", importWithOutput.ProcessingJsonOutputPath);
        Assert.Equal("source-map.json", importWithOutput.SourceMapJsonOutputPath);
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
        var htmlBook = Assert.IsType<RenderHtmlBookCommand>(
            _parser.Parse(["render", "book.flow.json", "--html-book", "book-directory"]).Command);
        Assert.Equal("book.flow.json", htmlBook.DocumentPath);
        Assert.Equal("book-directory", htmlBook.OutputDirectory);
        Assert.Equal(HtmlBookUiLanguage.Automatic, htmlBook.UiLanguage);

        var portugueseHtmlBook = Assert.IsType<RenderHtmlBookCommand>(
            _parser.Parse(
            [
                "render",
                "book.flow.json",
                "--ui-language",
                "pt-PT",
                "--html-book",
                "livro",
            ]).Command);
        Assert.Equal(HtmlBookUiLanguage.PortuguesePortugal, portugueseHtmlBook.UiLanguage);
    }

    [Fact]
    public void InvocationOptions_RecognizeLanguageBannerAndPlainOutput()
    {
        var result = CliInvocationOptionsParser.Parse(
            ["--language", "pt-BR", "--banner", "--no-color", "inspect", "book.flow.json"]);

        Assert.True(result.IsSuccess);
        Assert.Equal("pt-BR", result.Options!.CultureName);
        Assert.True(result.Options.ShowBanner);
        Assert.False(result.Options.UseColor);
        Assert.Equal(["inspect", "book.flow.json"], result.Options.CommandArguments);
    }

    [Theory]
    [InlineData("fr-FR", "FLOWCLI_INVALID_VALUE")]
    [InlineData("", "FLOWCLI_USAGE")]
    public void InvocationOptions_RejectUnknownLanguagesWithStableCode(string language, string code)
    {
        var result = CliInvocationOptionsParser.Parse(["--language", language, "help"]);

        Assert.False(result.IsSuccess);
        Assert.Equal(code, result.DiagnosticCode);
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
            "FLOWCLI_UNKNOWN_OPTION:",
            _parser.Parse(["import", "book.epub", "--unknown", "book.flow.json"]).Error,
            StringComparison.Ordinal);
        Assert.StartsWith(
            "FLOWCLI_DUPLICATE_OPTION:",
            _parser.Parse(
                ["import", "book.epub", "--output", "one.json", "--output", "two.json"])
                .Error,
            StringComparison.Ordinal);
        Assert.StartsWith(
            "FLOWCLI_DUPLICATE_OPTION:",
            _parser.Parse(
                ["import", "book.epub", "--source-map-json", "one.json", "--source-map-json", "two.json"])
                .Error,
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
        Assert.StartsWith(
            "FLOWCLI_INVALID_VALUE:",
            _parser.Parse(
                ["render", "book.flow.json", "--html-book", "book", "--ui-language", "portuguese"])
                .Error,
            StringComparison.Ordinal);
        Assert.StartsWith(
            "FLOWCLI_DUPLICATE_OPTION:",
            _parser.Parse(
                ["render", "book.flow.json", "--html-book", "book", "--ui-language", "en", "--ui-language", "pt-BR"])
                .Error,
            StringComparison.Ordinal);
    }
}
