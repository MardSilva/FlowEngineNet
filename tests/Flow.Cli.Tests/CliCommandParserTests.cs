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
        var inventory = Assert.IsType<InventoryEpubCommand>(_parser.Parse(
        [
            "epub-inventory", "books", "--output", "inventory.json", "--repository-root", "repository", "--force",
        ]).Command);
        Assert.Equal("books", inventory.SourceDirectory);
        Assert.Equal("inventory.json", inventory.OutputPath);
        Assert.Equal("repository", inventory.RepositoryRoot);
        Assert.True(inventory.Force);
        var inventoryQualification = Assert.IsType<QualifyEpubInventoryCommand>(_parser.Parse(
        [
            "epub-inventory-qualify", "books", "--report", "qualification.json",
            "--repository-root", "repository", "--legal-use", "--drm-free", "--force", "--resume",
        ]).Command);
        Assert.Equal("books", inventoryQualification.SourceDirectory);
        Assert.Equal("qualification.json", inventoryQualification.ReportPath);
        Assert.True(inventoryQualification.Force);
        Assert.True(inventoryQualification.Resume);
        var matrix = Assert.IsType<ClassifyEpubInventoryCommand>(_parser.Parse(
        [
            "epub-inventory-matrix", "qualification.json", "--qualification-sha256", new string('A', 64),
            "--output", "matrix.json", "--repository-root", "repository", "--force", "--resume",
        ]).Command);
        Assert.Equal("qualification.json", matrix.QualificationReportPath);
        Assert.Equal("matrix.json", matrix.OutputPath);
        Assert.True(matrix.Force);
        Assert.True(matrix.Resume);
        var inventoryReview = Assert.IsType<ReviewEpubInventoryCommand>(_parser.Parse(
        [
            "epub-inventory-review", "books", "--qualification", "qualification.json",
            "--qualification-sha256", new string('B', 64), "--output", "review",
            "--repository-root", "repository", "--legal-use", "--drm-free",
            "--ui-language", "pt-BR", "--force", "--resume",
        ]).Command);
        Assert.Equal("books", inventoryReview.SourceDirectory);
        Assert.Equal("qualification.json", inventoryReview.QualificationReportPath);
        Assert.Equal("review", inventoryReview.OutputDirectory);
        Assert.Equal(HtmlBookUiLanguage.PortugueseBrazil, inventoryReview.UiLanguage);
        Assert.True(inventoryReview.Force);
        Assert.True(inventoryReview.Resume);
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

        var corpus = Assert.IsType<CorpusCommand>(_parser.Parse(
        [
            "corpus", "epub-corpus.json", "--repository-root", "repository", "--report", "corpus-report.json",
            "--external-root", "external", "--baseline", "baseline.json", "--force", "--resume",
        ]).Command);
        Assert.Equal("epub-corpus.json", corpus.ManifestPath);
        Assert.Equal("external", corpus.ExternalCorpusRoot);
        Assert.Equal("baseline.json", corpus.AcceptedBaselinePath);
        Assert.True(corpus.Force);
        Assert.True(corpus.Resume);

        var hash = new string('A', 64);
        var qualify = Assert.IsType<QualifyEpubCommand>(_parser.Parse(
        [
            "epub-qualify", "book.epub", "--candidate-id", "candidate-001", "--sha256", hash,
            "--report", "gate.json", "--repository-root", "repository", "--legal-use", "--drm-free", "--repetitions", "3",
            "--include-environment", "--force", "--resume",
        ]).Command);
        Assert.Equal("candidate-001", qualify.CandidateId.Value);
        Assert.Equal(3, qualify.RepetitionCount);
        Assert.True(qualify.IncludeEnvironment);
        Assert.True(qualify.Force);
        Assert.True(qualify.Resume);

        var review = Assert.IsType<ReviewEpubCommand>(_parser.Parse(
        [
            "epub-review", "book.epub", "--candidate-id", "candidate-001", "--sha256", hash,
            "--output", "review", "--repository-root", "repository", "--legal-use", "--drm-free",
            "--ui-language", "pt-BR",
            "--force", "--resume",
        ]).Command);
        Assert.Equal(HtmlBookUiLanguage.PortugueseBrazil, review.UiLanguage);
        Assert.True(review.Force);
        Assert.True(review.Resume);

        var status = Assert.IsType<ExecutionStatusCommand>(_parser.Parse(
            ["execution-status", "gate.json", "--json", "status.json", "--force"]).Command);
        Assert.Equal("gate.json", status.DestinationPath);
        Assert.Equal("status.json", status.JsonOutputPath);
        Assert.True(status.Force);

        var executionId = Guid.NewGuid();
        var clean = Assert.IsType<ExecutionCleanCommand>(_parser.Parse(
            ["execution-clean", "gate.json", "--execution-id", executionId.ToString("N")]).Command);
        Assert.Equal(executionId, clean.ExecutionId);
    }

    [Fact]
    public void InvocationOptions_RecognizeLanguageBannerAndPlainOutput()
    {
        var result = CliInvocationOptionsParser.Parse(
            ["--language", "pt-BR", "--banner", "--no-color", "inspect", "book.flow.json"]);

        Assert.True(result.IsSuccess);
        Assert.Equal("pt-BR", result.Options!.CultureName);
        Assert.True(result.Options.ShowBanner);
        Assert.True(result.Options.NoColor);
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
            "FLOWCLI_USAGE:",
            _parser.Parse(["epub-inventory", "books", "--output", "inventory.json"]).Error,
            StringComparison.Ordinal);
        Assert.StartsWith(
            "FLOWCLI_DECLARATION_REQUIRED:",
            _parser.Parse(
            [
                "epub-inventory-qualify", "books", "--report", "qualification.json",
                "--repository-root", "repository",
            ]).Error,
            StringComparison.Ordinal);
        Assert.StartsWith(
            "FLOWCLI_INVALID_VALUE:",
            _parser.Parse(
            [
                "epub-inventory-matrix", "qualification.json", "--qualification-sha256", "invalid",
                "--output", "matrix.json", "--repository-root", "repository",
            ]).Error,
            StringComparison.Ordinal);
        Assert.StartsWith(
            "FLOWCLI_DUPLICATE_OPTION:",
            _parser.Parse(
                ["import", "book.epub", "--output", "one.json", "--output", "two.json"])
                .Error,
            StringComparison.Ordinal);
        Assert.StartsWith(
            "FLOWCLI_INVALID_VALUE:",
            _parser.Parse(["execution-clean", "gate.json", "--execution-id", "not-an-id"]).Error,
            StringComparison.Ordinal);
        Assert.StartsWith(
            "FLOWCLI_DECLARATION_REQUIRED:",
            _parser.Parse(
                [
                    "epub-qualify", "book.epub", "--candidate-id", "candidate", "--sha256", new string('A', 64),
                    "--report", "gate.json",
                ])
                .Error,
            StringComparison.Ordinal);
        Assert.StartsWith(
            "FLOWCLI_INVALID_VALUE:",
            _parser.Parse(
                [
                    "epub-qualify", "book.epub", "--candidate-id", "INVALID", "--sha256", new string('A', 64),
                    "--report", "gate.json", "--repository-root", "repository", "--legal-use", "--drm-free",
                ])
                .Error,
            StringComparison.Ordinal);
        Assert.StartsWith(
            "FLOWCLI_INVALID_VALUE:",
            _parser.Parse(
                [
                    "epub-qualify", "book.epub", "--candidate-id", "candidate", "--sha256", new string('A', 64),
                    "--report", "gate.json", "--repository-root", "repository", "--legal-use", "--drm-free", "--repetitions", "1",
                ])
                .Error,
            StringComparison.Ordinal);
        Assert.StartsWith(
            "FLOWCLI_DUPLICATE_OPTION:",
            _parser.Parse(
                ["import", "book.epub", "--source-map-json", "one.json", "--source-map-json", "two.json"])
                .Error,
            StringComparison.Ordinal);
        Assert.StartsWith(
            "FLOWCLI_DUPLICATE_OPTION:",
            _parser.Parse(
                [
                    "epub-review", "book.epub", "--candidate-id", "candidate", "--sha256", new string('A', 64),
                    "--output", "review", "--repository-root", "repository", "--legal-use", "--drm-free",
                    "--force", "--force",
                ])
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
