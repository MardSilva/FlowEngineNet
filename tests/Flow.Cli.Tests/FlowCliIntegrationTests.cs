using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Flow.Cli;
using Flow.Core;
using Flow.Documents;
using Flow.Epub;
using Flow.Layout;
using Flow.Rendering.Html;

namespace Flow.Cli.Tests;

public sealed class FlowCliIntegrationTests
{
    [Fact]
    public async Task CommittedSample_IsValidAndContainsTheRequiredBookStructure()
    {
        var samplePath = Path.Combine(AppContext.BaseDirectory, "SampleBook", "sample.flow.json");
        await using var stream = File.OpenRead(samplePath);
        var document = await new FlowJsonDocumentSerializer().DeserializeAsync(stream);

        Assert.Equal("The Flow Experiment", document.Metadata.Title);
        Assert.Equal(5, document.Content.Children.Count(static node => node is Chapter));
        Assert.Contains(document.Index.Locations, static location => location.Node is TableOfContents);
        Assert.Contains(document.Index.Locations, static location => location.Node is Figure { Caption: not null });
        Assert.Contains(document.Index.Locations, static location => location.Node is Footnote);
        Assert.Contains(document.Index.Locations, static location => location.Node is CodeBlock);
        Assert.True(new DocumentValidator().Validate(document).IsValid);
    }

    [Fact]
    public async Task CommittedArtifacts_AreDeterministicOutputsOfTheCurrentPipeline()
    {
        var sampleDirectory = Path.Combine(AppContext.BaseDirectory, "SampleBook");
        var samplePath = Path.Combine(sampleDirectory, "sample.flow.json");
        var serializer = new FlowJsonDocumentSerializer();
        await using var source = File.OpenRead(samplePath);
        var document = await serializer.DeserializeAsync(source);
        await using var regeneratedSource = new MemoryStream();
        await serializer.SerializeAsync(SampleBookFactory.Create(), regeneratedSource);

        Assert.True(File.ReadAllBytes(samplePath).AsSpan().SequenceEqual(regeneratedSource.ToArray()));

        var renderer = new HtmlDocumentRenderer();
        var preferences = new UserReadingPreferences();
        var mobileLayout = new AdaptiveLayoutEngine().Layout(
            document,
            new LayoutContext(390, 844, DeviceClass.Phone, userPreferences: preferences));
        var desktopLayout = new AdaptiveLayoutEngine().Layout(
            document,
            new LayoutContext(1600, 1000, DeviceClass.Desktop, userPreferences: preferences));

        Assert.True(
            File.ReadAllBytes(Path.Combine(sampleDirectory, "mobile.html"))
                .AsSpan()
                .SequenceEqual(renderer.Render(document, mobileLayout, preferences).Content.AsSpan()));
        Assert.True(
            File.ReadAllBytes(Path.Combine(sampleDirectory, "desktop.html"))
                .AsSpan()
                .SequenceEqual(renderer.Render(document, desktopLayout, preferences).Content.AsSpan()));
    }

    [Fact]
    public async Task SampleInspectValidateAndHash_OperateOnTheSameGeneratedDocument()
    {
        using var workspace = new TemporaryWorkspace();
        var documentPath = workspace.PathOf("generated.flow.json");
        var application = FlowCliApplication.CreateDefault();

        var sample = await RunAsync(application, ["sample", documentPath]);
        var inspect = await RunAsync(application, ["inspect", documentPath]);
        var validate = await RunAsync(application, ["validate", documentPath]);
        var hash = await RunAsync(application, ["hash", documentPath]);

        Assert.Equal(0, sample.ExitCode);
        Assert.True(File.Exists(documentPath));
        Assert.Equal(0, inspect.ExitCode);
        Assert.Contains($"ID: {SampleBookFactory.DocumentUrn}", inspect.Output, StringComparison.Ordinal);
        Assert.Contains("Chapters: 5", inspect.Output, StringComparison.Ordinal);
        Assert.Contains("Sections: 1", inspect.Output, StringComparison.Ordinal);
        Assert.Contains("Paragraphs: 18", inspect.Output, StringComparison.Ordinal);
        Assert.Contains("Figures: 1", inspect.Output, StringComparison.Ordinal);
        Assert.Contains("Footnotes: 1", inspect.Output, StringComparison.Ordinal);
        Assert.Contains("Anchors: 44", inspect.Output, StringComparison.Ordinal);
        Assert.Equal((0, "Valid: no semantic validation errors."), (validate.ExitCode, validate.Output.Trim()));
        Assert.Equal(0, hash.ExitCode);
        Assert.Contains("Hash: SHA-256:", hash.Output, StringComparison.Ordinal);
        Assert.Contains("Canonicalization: flow-c14n-0.1", hash.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Import_EpubProducesAValidDeterministicFlowDocument()
    {
        using var workspace = new TemporaryWorkspace();
        var epubPath = workspace.PathOf("real-book.epub");
        var documentPath = workspace.PathOf("imported.flow.json");
        CreateMinimalEpub(epubPath);
        var application = FlowCliApplication.CreateDefault();

        var import = await RunAsync(
            application,
            ["import", epubPath, "--output", documentPath]);
        var validate = await RunAsync(application, ["validate", documentPath]);
        var inspect = await RunAsync(application, ["inspect", documentPath]);

        Assert.Equal(0, import.ExitCode);
        Assert.True(File.Exists(documentPath));
        Assert.Contains("Imported EPUB:", import.Output, StringComparison.Ordinal);
        Assert.Contains("Title: Livro real mínimo", import.Output, StringComparison.Ordinal);
        Assert.Contains("Hash: SHA-256:", import.Output, StringComparison.Ordinal);
        Assert.Equal(string.Empty, import.Error);
        Assert.Equal((0, "Valid: no semantic validation errors."), (validate.ExitCode, validate.Output.Trim()));
        Assert.Equal(0, inspect.ExitCode);
        Assert.Contains("Chapters: 1", inspect.Output, StringComparison.Ordinal);

        await using var stream = File.OpenRead(documentPath);
        var document = await new FlowJsonDocumentSerializer().DeserializeAsync(stream);
        Assert.Equal("Livro real mínimo", document.Metadata.Title);
        Assert.Contains(document.Index.Locations, static location => location.Node is Heading);
        Assert.Contains(document.Index.Locations, static location => location.Node is Paragraph);
    }

    [Fact]
    public async Task Import_WithoutOutputUsesPortableTitleAndWritesCleanUnicodeDiagnosticsJson()
    {
        using var workspace = new TemporaryWorkspace();
        var epubPath = workspace.PathOf("source-name.epub");
        var diagnosticsPath = workspace.PathOf("diagnósticos.json");
        const string title = "Isto é filtro solar: Eclesiastes e a vida debaixo do sol";
        CreateMinimalEpub(epubPath, title);
        var application = FlowCliApplication.CreateDefault();

        var first = await RunAsync(
            application,
            ["import", epubPath, "--diagnostics-json", diagnosticsPath]);
        var expectedDocumentPath = workspace.PathOf(
            "isto_e_filtro_solar_eclesiastes_e_a_vida_debaixo_do_sol.flow.json");

        Assert.Equal(0, first.ExitCode);
        Assert.True(File.Exists(expectedDocumentPath));
        Assert.True(File.Exists(diagnosticsPath));
        Assert.Contains($"Flow document: {expectedDocumentPath}", first.Output, StringComparison.Ordinal);
        Assert.Contains($"Title: {title}", first.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("CategoryInfo", first.Output + first.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("FullyQualifiedErrorId", first.Output + first.Error, StringComparison.Ordinal);

        var documentBytes = await File.ReadAllBytesAsync(expectedDocumentPath);
        var diagnosticsBytes = await File.ReadAllBytesAsync(diagnosticsPath);
        var documentJson = Encoding.UTF8.GetString(documentBytes);
        var diagnosticsJson = Encoding.UTF8.GetString(diagnosticsBytes);
        Assert.False(documentBytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        Assert.False(diagnosticsBytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        Assert.Contains(title, documentJson, StringComparison.Ordinal);
        Assert.Contains(title, diagnosticsJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u00E9", documentJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\\u00E9", diagnosticsJson, StringComparison.OrdinalIgnoreCase);

        var escapedPath = workspace.PathOf("escaped.flow.json");
        await File.WriteAllTextAsync(
            escapedPath,
            documentJson.Replace("é", "\\u00e9", StringComparison.Ordinal),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        var readableHash = await RunAsync(application, ["hash", expectedDocumentPath]);
        var escapedHash = await RunAsync(application, ["hash", escapedPath]);
        Assert.Equal(OutputValue(readableHash.Output, "Hash: "), OutputValue(escapedHash.Output, "Hash: "));

        var firstDiagnostics = diagnosticsBytes;
        var second = await RunAsync(
            application,
            ["import", epubPath, "--diagnostics-json", diagnosticsPath]);
        Assert.Equal(0, second.ExitCode);
        Assert.Equal(firstDiagnostics, await File.ReadAllBytesAsync(diagnosticsPath));
    }

    [Fact]
    public async Task Import_InvalidEpubReportsDiagnosticsAndDoesNotWritePartialOutput()
    {
        using var workspace = new TemporaryWorkspace();
        var epubPath = workspace.PathOf("invalid.epub");
        var documentPath = workspace.PathOf("invalid.flow.json");
        var diagnosticsPath = workspace.PathOf("invalid-diagnostics.json");
        await File.WriteAllTextAsync(epubPath, "not a ZIP archive");

        var result = await RunAsync(
            FlowCliApplication.CreateDefault(),
            [
                "import",
                epubPath,
                "--output",
                documentPath,
                "--diagnostics-json",
                diagnosticsPath,
            ]);

        Assert.Equal(1, result.ExitCode);
        Assert.False(File.Exists(documentPath));
        Assert.True(File.Exists(diagnosticsPath));
        Assert.Contains("EPUB001", result.Error, StringComparison.Ordinal);
        Assert.Contains("FLOWCLI_EPUB_IMPORT_FAILED", result.Error, StringComparison.Ordinal);
        using var report = JsonDocument.Parse(await File.ReadAllBytesAsync(diagnosticsPath));
        Assert.False(report.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, report.RootElement.GetProperty("document").ValueKind);
    }

    [Fact]
    public async Task Import_WritesDeterministicFidelityReportAlongsideDiagnostics()
    {
        using var workspace = new TemporaryWorkspace();
        var epubPath = workspace.PathOf("fidelity.epub");
        var documentPath = workspace.PathOf("fidelity.flow.json");
        var diagnosticsPath = workspace.PathOf("diagnostics.json");
        var firstReportPath = workspace.PathOf("fidelity-one.json");
        var secondReportPath = workspace.PathOf("fidelity-two.json");
        CreateMinimalEpub(epubPath);
        var application = FlowCliApplication.CreateDefault();

        var first = await RunAsync(
            application,
            [
                "import",
                epubPath,
                "--output",
                documentPath,
                "--diagnostics-json",
                diagnosticsPath,
                "--fidelity-report",
                firstReportPath,
            ]);
        var second = await RunAsync(
            application,
            [
                "import",
                epubPath,
                "--output",
                documentPath,
                "--fidelity-report",
                secondReportPath,
            ]);

        Assert.Equal(0, first.ExitCode);
        Assert.Equal(0, second.ExitCode);
        Assert.True(File.Exists(diagnosticsPath));
        Assert.Equal(await File.ReadAllBytesAsync(firstReportPath), await File.ReadAllBytesAsync(secondReportPath));
        var bytes = await File.ReadAllBytesAsync(firstReportPath);
        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        Assert.DoesNotContain((byte)'\r', bytes);
        using var report = JsonDocument.Parse(bytes);
        Assert.Equal("flow-epub-fidelity-0.1", report.RootElement.GetProperty("format").GetString());
        Assert.True(report.RootElement.GetProperty("importSucceeded").GetBoolean());
        Assert.False(report.RootElement.GetProperty("summary").GetProperty("partial").GetBoolean());
        Assert.Contains("not a claim", report.RootElement.GetProperty("scope").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Import_FailedInputStillWritesPartialFidelityReportAtomically()
    {
        using var workspace = new TemporaryWorkspace();
        var epubPath = workspace.PathOf("invalid.epub");
        var reportPath = workspace.PathOf("partial-fidelity.json");
        await File.WriteAllTextAsync(epubPath, "not a ZIP archive");

        var result = await RunAsync(
            FlowCliApplication.CreateDefault(),
            ["import", epubPath, "--fidelity-report", reportPath]);

        Assert.Equal(1, result.ExitCode);
        Assert.True(File.Exists(reportPath));
        Assert.Empty(Directory.GetFiles(workspace.Root, "*.tmp"));
        using var report = JsonDocument.Parse(await File.ReadAllBytesAsync(reportPath));
        Assert.False(report.RootElement.GetProperty("importSucceeded").GetBoolean());
        Assert.True(report.RootElement.GetProperty("summary").GetProperty("partial").GetBoolean());
        Assert.Equal(
            JsonValueKind.Null,
            report.RootElement.GetProperty("summary").GetProperty("preservationPercentage").ValueKind);
        Assert.Contains(
            report.RootElement.GetProperty("findings").EnumerateArray(),
            static finding => finding.GetProperty("relatedDiagnosticCode").GetString() == "EPUB001");
    }

    [Fact]
    public async Task EpubInspect_ReportsStructureAndWritesDeterministicJsonWithoutImporting()
    {
        using var workspace = new TemporaryWorkspace();
        var epubPath = workspace.PathOf("inspection.epub");
        var firstReportPath = workspace.PathOf("inspection-one.json");
        var secondReportPath = workspace.PathOf("inspection-two.json");
        CreateMinimalEpub(epubPath, "Inspeção é 日本語");
        var application = FlowCliApplication.CreateDefault();

        var first = await RunAsync(
            application,
            ["epub-inspect", epubPath, "--json", firstReportPath]);
        var second = await RunAsync(
            application,
            ["epub-inspect", epubPath, "--json", secondReportPath]);

        Assert.Equal(0, first.ExitCode);
        Assert.Equal(0, second.ExitCode);
        Assert.Equal(string.Empty, first.Error);
        Assert.Contains("Status: valid", first.Output, StringComparison.Ordinal);
        Assert.Contains("EPUB version: Epub3 (3.0)", first.Output, StringComparison.Ordinal);
        Assert.Contains("Manifest items: 1", first.Output, StringComparison.Ordinal);
        Assert.Contains("Spine items: 1 (linear 1, non-linear 0)", first.Output, StringComparison.Ordinal);
        Assert.True(File.ReadAllBytes(firstReportPath).AsSpan().SequenceEqual(File.ReadAllBytes(secondReportPath)));

        var reportBytes = await File.ReadAllBytesAsync(firstReportPath);
        Assert.DoesNotContain((byte)'\r', reportBytes);
        Assert.False(reportBytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        Assert.Contains("Inspeção é 日本語", Encoding.UTF8.GetString(reportBytes), StringComparison.Ordinal);
        Assert.DoesNotContain("\\u00E9", Encoding.UTF8.GetString(reportBytes), StringComparison.OrdinalIgnoreCase);
        using var report = JsonDocument.Parse(reportBytes);
        Assert.Equal("flow-epub-inspection-0.1", report.RootElement.GetProperty("format").GetString());
        Assert.Equal("epub3", report.RootElement.GetProperty("package").GetProperty("versionFamily").GetString());
        Assert.Equal(1, report.RootElement.GetProperty("manifest").GetArrayLength());
        Assert.Equal(1, report.RootElement.GetProperty("spine").GetArrayLength());
    }

    [Fact]
    public async Task EpubInspect_InvalidZipStillWritesDiagnosticJsonReport()
    {
        using var workspace = new TemporaryWorkspace();
        var epubPath = workspace.PathOf("invalid-inspection.epub");
        var reportPath = workspace.PathOf("invalid-inspection.json");
        await File.WriteAllTextAsync(epubPath, "not a ZIP archive");

        var result = await RunAsync(
            FlowCliApplication.CreateDefault(),
            ["epub-inspect", epubPath, "--json", reportPath]);

        Assert.Equal(1, result.ExitCode);
        Assert.True(File.Exists(reportPath));
        Assert.Contains("EPUB001", result.Error, StringComparison.Ordinal);
        using var report = JsonDocument.Parse(await File.ReadAllBytesAsync(reportPath));
        Assert.False(report.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(
            report.RootElement.GetProperty("diagnostics").EnumerateArray(),
            static diagnostic => diagnostic.GetProperty("code").GetString() == EpubDiagnosticCodes.InvalidArchive);
    }

    [Fact]
    public async Task Validate_ReturnsTwoAndDiagnosticsForInvalidDocument()
    {
        using var workspace = new TemporaryWorkspace();
        var path = workspace.PathOf("invalid.flow.json");
        var duplicateId = new NodeId("duplicate");
        var invalid = new FlowDocument(
            new DocumentIdentity(new DocumentId("urn:flow:cli:invalid")),
            new DocumentMetadata("Invalid"),
            new DocumentContent(
            [
                new Paragraph(duplicateId, [new Text("One")]),
                new Paragraph(duplicateId, [new Text("Two")]),
            ]));
        await using (var stream = File.Create(path))
        {
            await new FlowJsonDocumentSerializer().SerializeAsync(invalid, stream);
        }

        var result = await RunAsync(FlowCliApplication.CreateDefault(), ["validate", path]);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains(ValidationDiagnosticCodes.DuplicateNodeId, result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Render_MobileAndDesktopPreserveIdentityHashAndAnchors()
    {
        using var workspace = new TemporaryWorkspace();
        var documentPath = workspace.PathOf("sample.flow.json");
        var mobilePath = workspace.PathOf("mobile.html");
        var desktopPath = workspace.PathOf("desktop.html");
        var application = FlowCliApplication.CreateDefault();
        Assert.Equal(0, (await RunAsync(application, ["sample", documentPath])).ExitCode);

        var mobile = await RunAsync(
            application,
            ["render", documentPath, "--html", mobilePath, "--width", "390", "--height", "844"]);
        var desktop = await RunAsync(
            application,
            ["render", documentPath, "--html", desktopPath, "--width", "1600", "--height", "1000"]);

        Assert.Equal(0, mobile.ExitCode);
        Assert.Equal(0, desktop.ExitCode);
        Assert.True(File.Exists(mobilePath));
        Assert.True(File.Exists(desktopPath));

        var mobileHtml = XDocument.Load(mobilePath);
        var desktopHtml = XDocument.Load(desktopPath);
        Assert.Equal(SampleBookFactory.DocumentUrn, DocumentId(mobileHtml));
        Assert.Equal(DocumentId(mobileHtml), DocumentId(desktopHtml));
        Assert.Equal(OutputValue(mobile.Output, "Hash: "), OutputValue(desktop.Output, "Hash: "));
        Assert.Equal(OutputValue(mobile.Output, "Anchors: "), OutputValue(desktop.Output, "Anchors: "));
        Assert.Equal(ElementIds(mobileHtml), ElementIds(desktopHtml));
        Assert.Equal(AnchorTargets(mobileHtml), AnchorTargets(desktopHtml));
        Assert.Contains("Viewport: 390x844 (Small)", mobile.Output, StringComparison.Ordinal);
        Assert.Contains("Viewport: 1600x1000 (Large)", desktop.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Application_ReportsParsingAndFileErrorsWithoutThrowing()
    {
        using var workspace = new TemporaryWorkspace();
        var application = FlowCliApplication.CreateDefault();

        var parseError = await RunAsync(application, ["unknown"]);
        var fileError = await RunAsync(application, ["inspect", workspace.PathOf("missing.flow.json")]);

        Assert.Equal(1, parseError.ExitCode);
        Assert.Contains("Unknown command", parseError.Error, StringComparison.Ordinal);
        Assert.Equal(1, fileError.ExitCode);
        Assert.Contains("FLOWCLI_OPERATION_FAILED:", fileError.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Help_StatesExperimentalStatusCommandsAndExitCodes()
    {
        var result = await RunAsync(FlowCliApplication.CreateDefault(), ["help"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("0.2.0-alpha.1 (experimental)", result.Output, StringComparison.Ordinal);
        Assert.Contains("flow import <book.epub>", result.Output, StringComparison.Ordinal);
        Assert.Contains("flow epub-inspect <book.epub>", result.Output, StringComparison.Ordinal);
        Assert.Contains("flow validate <document>", result.Output, StringComparison.Ordinal);
        Assert.Contains("Exit codes: 0 success, 1 command/input failure, 2 semantic validation failure.", result.Output, StringComparison.Ordinal);
    }

    private static async Task<CliResult> RunAsync(FlowCliApplication application, string[] arguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await application.RunAsync(arguments, output, error);
        return new CliResult(exitCode, output.ToString(), error.ToString());
    }

    private static string? DocumentId(XDocument document) =>
        (string?)document.Descendants("article").Single().Attribute("data-document-id");

    private static string[] ElementIds(XDocument document) =>
        document.Descendants()
            .Attributes("id")
            .Select(static attribute => attribute.Value)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string[] AnchorTargets(XDocument document) =>
        document.Descendants("a")
            .Attributes("href")
            .Select(static attribute => attribute.Value)
            .Where(static value => value.StartsWith('#'))
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string OutputValue(string output, string prefix) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Single(line => line.StartsWith(prefix, StringComparison.Ordinal))[prefix.Length..].Trim();

    private static void CreateMinimalEpub(string path, string title = "Livro real mínimo")
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        AddText(archive, "mimetype", "application/epub+zip", CompressionLevel.NoCompression);
        AddText(
            archive,
            "META-INF/container.xml",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
              <rootfiles>
                <rootfile full-path="EPUB/package.opf" media-type="application/oebps-package+xml" />
              </rootfiles>
            </container>
            """);
        AddText(
            archive,
            "EPUB/package.opf",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="book-id">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="book-id">urn:flow:test:real-epub</dc:identifier>
                <dc:title>{{TITLE}}</dc:title>
                <dc:language>pt-PT</dc:language>
                <dc:creator>Flow contributors</dc:creator>
              </metadata>
              <manifest>
                <item id="chapter" href="text/chapter.xhtml" media-type="application/xhtml+xml" />
              </manifest>
              <spine><itemref idref="chapter" /></spine>
            </package>
            """.Replace("{{TITLE}}", title, StringComparison.Ordinal));
        AddText(
            archive,
            "EPUB/text/chapter.xhtml",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <html xmlns="http://www.w3.org/1999/xhtml" lang="pt-PT">
              <head><title>Capítulo um</title></head>
              <body>
                <h1 id="chapter-one">Capítulo um</h1>
                <p id="opening">O primeiro parágrafo importado pelo Flow.</p>
              </body>
            </html>
            """);
    }

    private static void AddText(
        ZipArchive archive,
        string path,
        string content,
        CompressionLevel compressionLevel = CompressionLevel.Optimal)
    {
        var entry = archive.CreateEntry(path, compressionLevel);
        using var stream = entry.Open();
        stream.Write(Encoding.UTF8.GetBytes(content));
    }

    private sealed record CliResult(int ExitCode, string Output, string Error);

    private sealed class TemporaryWorkspace : IDisposable
    {
        public TemporaryWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), $"flow-cli-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string PathOf(string fileName) => Path.Combine(Root, fileName);

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
