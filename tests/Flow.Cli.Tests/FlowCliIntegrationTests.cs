using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Flow.Cli;
using Flow.Core;
using Flow.Documents;
using Flow.Epub;
using Flow.Epub.Corpus;
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
    public async Task Import_PortugueseDiagnosticAddsLocalizedSummaryWithoutChangingJsonEvidence()
    {
        using var workspace = new TemporaryWorkspace();
        var epubPath = workspace.PathOf("invalid-localized.epub");
        var diagnosticsPath = workspace.PathOf("invalid-localized-diagnostics.json");
        await File.WriteAllTextAsync(epubPath, "not a ZIP archive");
        var application = FlowCliApplication.CreateDefault();

        var english = await RunAsync(
            application,
            ["--language", "en-US", "import", epubPath, "--diagnostics-json", diagnosticsPath]);
        var englishJson = await File.ReadAllBytesAsync(diagnosticsPath);
        var portuguese = await RunAsync(
            application,
            ["--language", "pt-BR", "import", epubPath, "--diagnostics-json", diagnosticsPath]);
        var portugueseJson = await File.ReadAllBytesAsync(diagnosticsPath);

        Assert.Equal(1, english.ExitCode);
        Assert.Equal(1, portuguese.ExitCode);
        Assert.Contains("EPUB001", portuguese.Error, StringComparison.Ordinal);
        Assert.Contains("O arquivo EPUB é inválido ou não pode ser lido.", portuguese.Error, StringComparison.Ordinal);
        Assert.Contains("Detalhe técnico:", portuguese.Error, StringComparison.Ordinal);
        Assert.Equal(englishJson, portugueseJson);
    }

    [Fact]
    public async Task Import_WithManyDiagnosticsSummarizesConsoleAndPreservesEveryJsonDetail()
    {
        using var workspace = new TemporaryWorkspace();
        var epubPath = workspace.PathOf("diagnostic-heavy.epub");
        var documentPath = workspace.PathOf("diagnostic-heavy.flow.json");
        var diagnosticsPath = workspace.PathOf("diagnostics.json");
        CreateDiagnosticHeavyEpub(epubPath, chapterCount: 45);

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

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Diagnostics: 0 information, 45 warnings, 0 errors.", result.Output, StringComparison.Ordinal);
        Assert.Contains("Diagnostic codes: EPUB010=45", result.Output, StringComparison.Ordinal);
        Assert.Contains("Console detail limited to 40 of 45 diagnostics", result.Output, StringComparison.Ordinal);
        Assert.Equal(40, result.Error.Split("Warning EPUB010", StringSplitOptions.None).Length - 1);

        using var report = JsonDocument.Parse(await File.ReadAllBytesAsync(diagnosticsPath));
        var diagnostics = report.RootElement.GetProperty("diagnostics").EnumerateArray().ToArray();
        Assert.Equal(45, diagnostics.Length);
        Assert.All(
            diagnostics,
            static diagnostic => Assert.Equal(
                EpubDiagnosticCodes.UnsupportedElement,
                diagnostic.GetProperty("code").GetString()));
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
    public async Task Import_WritesDeterministicNoncanonicalEvidenceSidecarsWithoutChangingDocumentBytes()
    {
        using var workspace = new TemporaryWorkspace();
        var epubPath = workspace.PathOf("evidence.epub");
        var documentPath = workspace.PathOf("evidence.flow.json");
        var metadataOne = workspace.PathOf("metadata-one.json");
        var metadataTwo = workspace.PathOf("metadata-two.json");
        var processingOne = workspace.PathOf("processing-one.json");
        var processingTwo = workspace.PathOf("processing-two.json");
        var sourceMapOne = workspace.PathOf("source-map-one.json");
        var sourceMapTwo = workspace.PathOf("source-map-two.json");
        CreateMinimalEpub(epubPath, "Evidência é útil");
        var application = FlowCliApplication.CreateDefault();

        var baseline = await RunAsync(application, ["import", epubPath, "--output", documentPath]);
        Assert.Equal(0, baseline.ExitCode);
        var documentBytes = await File.ReadAllBytesAsync(documentPath);

        var first = await RunAsync(
            application,
            [
                "import",
                epubPath,
                "--output",
                documentPath,
                "--metadata-json",
                metadataOne,
                "--processing-json",
                processingOne,
                "--source-map-json",
                sourceMapOne,
            ]);
        var second = await RunAsync(
            application,
            [
                "import",
                epubPath,
                "--output",
                documentPath,
                "--metadata-json",
                metadataTwo,
                "--processing-json",
                processingTwo,
                "--source-map-json",
                sourceMapTwo,
            ]);

        Assert.Equal(0, first.ExitCode);
        Assert.Equal(0, second.ExitCode);
        Assert.Contains($"Metadata JSON: {metadataOne}", first.Output, StringComparison.Ordinal);
        Assert.Contains($"Processing JSON: {processingOne}", first.Output, StringComparison.Ordinal);
        Assert.Contains($"Source map JSON: {sourceMapOne}", first.Output, StringComparison.Ordinal);
        Assert.Equal(documentBytes, await File.ReadAllBytesAsync(documentPath));
        Assert.Equal(await File.ReadAllBytesAsync(metadataOne), await File.ReadAllBytesAsync(metadataTwo));
        Assert.Equal(await File.ReadAllBytesAsync(processingOne), await File.ReadAllBytesAsync(processingTwo));
        Assert.Equal(await File.ReadAllBytesAsync(sourceMapOne), await File.ReadAllBytesAsync(sourceMapTwo));

        AssertJsonEncoding(metadataOne);
        AssertJsonEncoding(processingOne);
        AssertJsonEncoding(sourceMapOne);
        Assert.DoesNotContain(workspace.Root, await File.ReadAllTextAsync(metadataOne), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(workspace.Root, await File.ReadAllTextAsync(processingOne), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(workspace.Root, await File.ReadAllTextAsync(sourceMapOne), StringComparison.OrdinalIgnoreCase);

        using var metadata = JsonDocument.Parse(await File.ReadAllBytesAsync(metadataOne));
        Assert.Equal("flow-epub-metadata-0.1", metadata.RootElement.GetProperty("format").GetString());
        Assert.True(metadata.RootElement.GetProperty("available").GetBoolean());
        Assert.Equal(
            "urn:flow:test:real-epub",
            metadata.RootElement.GetProperty("metadata").GetProperty("selectedIdentifier").GetString());
        Assert.Equal(
            "Evidência é útil",
            metadata.RootElement.GetProperty("metadata").GetProperty("titles")[0].GetProperty("value").GetString());

        using var processing = JsonDocument.Parse(await File.ReadAllBytesAsync(processingOne));
        Assert.Equal("flow-epub-package-processing-0.1", processing.RootElement.GetProperty("format").GetString());
        var processingPayload = processing.RootElement.GetProperty("processing");
        Assert.Equal(1, processingPayload.GetProperty("manifest").GetArrayLength());
        Assert.Equal(0, processingPayload.GetProperty("spine")[0].GetProperty("position").GetInt32());
        Assert.Equal(
            "EPUB/text/chapter.xhtml",
            processingPayload.GetProperty("spine")[0].GetProperty("selectedResourcePath").GetString());

        using var sourceMap = JsonDocument.Parse(await File.ReadAllBytesAsync(sourceMapOne));
        Assert.Equal("flow-epub-source-map-0.1", sourceMap.RootElement.GetProperty("format").GetString());
        var sourceMapPayload = sourceMap.RootElement.GetProperty("sourceMap");
        Assert.Equal(
            sourceMapPayload.GetProperty("locationCount").GetInt32(),
            sourceMapPayload.GetProperty("locations").GetArrayLength());
        Assert.Contains(
            sourceMapPayload.GetProperty("locations").EnumerateArray(),
            static location => location.GetProperty("fragment").GetString() == "opening"
                && location.GetProperty("nodeId").GetString() == "chapter-chapter-opening");
    }

    [Fact]
    public async Task Import_FailedInputWritesUnavailableEvidenceSidecarsWithoutPartialFiles()
    {
        using var workspace = new TemporaryWorkspace();
        var epubPath = workspace.PathOf("invalid-evidence.epub");
        var metadataPath = workspace.PathOf("metadata.json");
        var processingPath = workspace.PathOf("processing.json");
        var sourceMapPath = workspace.PathOf("source-map.json");
        await File.WriteAllTextAsync(epubPath, "not a ZIP archive");

        var result = await RunAsync(
            FlowCliApplication.CreateDefault(),
            [
                "import",
                epubPath,
                "--metadata-json",
                metadataPath,
                "--processing-json",
                processingPath,
                "--source-map-json",
                sourceMapPath,
            ]);

        Assert.Equal(1, result.ExitCode);
        foreach (var (path, payloadName) in new[]
        {
            (metadataPath, "metadata"),
            (processingPath, "processing"),
            (sourceMapPath, "sourceMap"),
        })
        {
            Assert.True(File.Exists(path));
            using var report = JsonDocument.Parse(await File.ReadAllBytesAsync(path));
            Assert.False(report.RootElement.GetProperty("importSucceeded").GetBoolean());
            Assert.False(report.RootElement.GetProperty("available").GetBoolean());
            Assert.Equal(JsonValueKind.Null, report.RootElement.GetProperty(payloadName).ValueKind);
        }

        Assert.Empty(Directory.GetFiles(workspace.Root, "*.tmp"));
    }

    [Fact]
    public async Task Import_RejectsEvidenceOutputPathCollisionsBeforeWriting()
    {
        using var workspace = new TemporaryWorkspace();
        var epubPath = workspace.PathOf("collision.epub");
        var sharedPath = workspace.PathOf("shared.json");
        CreateMinimalEpub(epubPath);

        var result = await RunAsync(
            FlowCliApplication.CreateDefault(),
            [
                "import",
                epubPath,
                "--metadata-json",
                sharedPath,
                "--source-map-json",
                sharedPath,
            ]);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("--metadata-json and --source-map-json must use different paths", result.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(sharedPath));
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
    public async Task RenderHtmlBook_WritesAndSafelyReplacesDeterministicNavigablePackage()
    {
        using var workspace = new TemporaryWorkspace();
        var documentPath = workspace.PathOf("sample.flow.json");
        var firstDirectory = workspace.PathOf("book-one");
        var secondDirectory = workspace.PathOf("book-two");
        var application = FlowCliApplication.CreateDefault();
        Assert.Equal(0, (await RunAsync(application, ["sample", documentPath])).ExitCode);

        var first = await RunAsync(application, ["render", documentPath, "--html-book", firstDirectory]);
        var second = await RunAsync(application, ["render", documentPath, "--html-book", secondDirectory]);

        Assert.Equal(0, first.ExitCode);
        Assert.Equal(0, second.ExitCode);
        Assert.Equal(string.Empty, first.Error);
        Assert.True(File.Exists(Path.Combine(firstDirectory, "index.html")));
        Assert.True(File.Exists(Path.Combine(firstDirectory, "toc.html")));
        Assert.True(File.Exists(Path.Combine(firstDirectory, "styles", "book.css")));
        Assert.Equal(5, Directory.GetFiles(Path.Combine(firstDirectory, "chapters"), "*.html").Length);
        Assert.Equal(DirectorySnapshot(firstDirectory), DirectorySnapshot(secondDirectory));

        using var manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(firstDirectory, "manifest.json")));
        Assert.Equal("flow-html-book-0.1", manifest.RootElement.GetProperty("format").GetString());
        Assert.Equal(
            OutputValue(first.Output, "Hash: ").Split(':', 2)[1],
            manifest.RootElement.GetProperty("canonicalIntegrity").GetProperty("hash").GetString());
        Assert.Equal(
            ["index.html", "toc.html", "chapters/chapter-001.html", "chapters/chapter-002.html", "chapters/chapter-003.html", "chapters/chapter-004.html", "chapters/chapter-005.html"],
            manifest.RootElement.GetProperty("readingOrder").EnumerateArray()
                .Select(static item => item.GetProperty("path").GetString()!)
                .ToArray());

        await File.WriteAllTextAsync(Path.Combine(firstDirectory, "stale.txt"), "old");
        var replacement = await RunAsync(application, ["render", documentPath, "--html-book", firstDirectory]);
        Assert.Equal(0, replacement.ExitCode);
        Assert.False(File.Exists(Path.Combine(firstDirectory, "stale.txt")));
        Assert.Equal(DirectorySnapshot(firstDirectory), DirectorySnapshot(secondDirectory));
    }

    [Fact]
    public async Task RenderHtmlBook_ExplicitUiLanguageLocalizesGeneratedTextAndPreservesBookLanguage()
    {
        using var workspace = new TemporaryWorkspace();
        var documentPath = workspace.PathOf("sample.flow.json");
        var outputDirectory = workspace.PathOf("livro-pt");
        var application = FlowCliApplication.CreateDefault();
        Assert.Equal(0, (await RunAsync(application, ["sample", documentPath])).ExitCode);

        var result = await RunAsync(
            application,
            ["render", documentPath, "--html-book", outputDirectory, "--ui-language", "pt-PT"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("UI language: pt-PT", result.Output, StringComparison.Ordinal);
        var chapter = XDocument.Load(Path.Combine(outputDirectory, "chapters", "chapter-001.html"));
        Assert.Equal("en", (string?)chapter.Root!.Attribute("lang"));
        Assert.Equal("pt-PT", (string?)chapter.Descendants("div")
            .Single(element => (string?)element.Attribute("class") == "book-shell")
            .Attribute("lang"));
        Assert.Equal("Aspeto da leitura", chapter.Descendants("summary").Single().Value);
        Assert.Contains(chapter.Descendants("a"), element => element.Value == "Índice");
        Assert.Contains(chapter.Descendants("article"), element => (string?)element.Attribute("lang") == "en");

        using var manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(outputDirectory, "manifest.json")));
        Assert.Equal("pt-PT", manifest.RootElement.GetProperty("uiLanguage").GetString());
    }

    [Fact]
    public async Task RenderHtmlBook_RejectsUnsafeExistingDirectoryAndSourceContainingOutput()
    {
        using var workspace = new TemporaryWorkspace();
        var documentPath = workspace.PathOf("sample.flow.json");
        var unrelatedDirectory = workspace.PathOf("unrelated");
        Directory.CreateDirectory(unrelatedDirectory);
        var marker = Path.Combine(unrelatedDirectory, "keep.txt");
        await File.WriteAllTextAsync(marker, "keep");
        var application = FlowCliApplication.CreateDefault();
        Assert.Equal(0, (await RunAsync(application, ["sample", documentPath])).ExitCode);

        var unrelated = await RunAsync(
            application,
            ["render", documentPath, "--html-book", unrelatedDirectory]);
        var containingSource = await RunAsync(
            application,
            ["render", documentPath, "--html-book", workspace.Root]);
        var localized = await RunAsync(
            application,
            ["--language", "pt-BR", "render", documentPath, "--html-book", unrelatedDirectory]);

        Assert.Equal(1, unrelated.ExitCode);
        Assert.Contains("not a replaceable Flow HTML book", unrelated.Error, StringComparison.Ordinal);
        Assert.Equal("keep", await File.ReadAllTextAsync(marker));
        Assert.Equal(1, containingSource.ExitCode);
        Assert.Contains("cannot contain its source document", containingSource.Error, StringComparison.Ordinal);
        Assert.Equal(1, localized.ExitCode);
        Assert.Contains(
            "O diretório de saída existente não é um livro HTML Flow que possa ser substituído.",
            localized.Error,
            StringComparison.Ordinal);
        Assert.True(File.Exists(documentPath));
        Assert.Empty(Directory.GetDirectories(workspace.Root, ".*.flow-html-book-*.tmp"));
    }

    [Fact]
    public async Task CancellationReturnsDistinctExitCodeAndPreservesExistingHtmlBook()
    {
        using var workspace = new TemporaryWorkspace();
        var documentPath = workspace.PathOf("sample.flow.json");
        var outputDirectory = workspace.PathOf("book");
        var application = FlowCliApplication.CreateDefault();
        Assert.Equal(0, (await RunAsync(application, ["sample", documentPath])).ExitCode);
        Assert.Equal(
            0,
            (await RunAsync(application, ["render", documentPath, "--html-book", outputDirectory])).ExitCode);
        var before = DirectorySnapshot(outputDirectory);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await application.RunAsync(
            ["render", documentPath, "--html-book", outputDirectory],
            output,
            error,
            cancellation.Token);

        Assert.Equal(130, exitCode);
        Assert.Contains("FLOWCLI_CANCELLED:", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(before, DirectorySnapshot(outputDirectory));
        Assert.Empty(Directory.GetDirectories(workspace.Root, ".*.flow-html-book-*.tmp"));
        Assert.Empty(Directory.GetDirectories(workspace.Root, ".*.flow-html-book-*.backup"));
    }

    [Fact]
    public async Task CancelledImportPreservesExistingFlowOutputAndLeavesNoTemporaryFile()
    {
        using var workspace = new TemporaryWorkspace();
        var epubPath = workspace.PathOf("book.epub");
        var outputPath = workspace.PathOf("book.flow.json");
        CreateMinimalEpub(epubPath);
        await File.WriteAllTextAsync(outputPath, "existing-output");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await FlowCliApplication.CreateDefault().RunAsync(
            ["import", epubPath, "--output", outputPath],
            output,
            error,
            cancellation.Token);

        Assert.Equal(130, exitCode);
        Assert.Contains("FLOWCLI_CANCELLED:", error.ToString(), StringComparison.Ordinal);
        Assert.Equal("existing-output", await File.ReadAllTextAsync(outputPath));
        Assert.Empty(Directory.GetFiles(workspace.Root, "*.tmp"));
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
        Assert.Contains("--metadata-json <metadata.json>", result.Output, StringComparison.Ordinal);
        Assert.Contains("--processing-json <processing.json>", result.Output, StringComparison.Ordinal);
        Assert.Contains("--source-map-json <source-map.json>", result.Output, StringComparison.Ordinal);
        Assert.Contains("flow epub-inspect <book.epub>", result.Output, StringComparison.Ordinal);
        Assert.Contains("flow corpus <manifest.json>", result.Output, StringComparison.Ordinal);
        Assert.Contains("flow epub-qualify <book.epub>", result.Output, StringComparison.Ordinal);
        Assert.Contains("flow epub-review <book.epub>", result.Output, StringComparison.Ordinal);
        Assert.Contains("flow execution-status <destination>", result.Output, StringComparison.Ordinal);
        Assert.Contains("flow execution-clean <destination>", result.Output, StringComparison.Ordinal);
        Assert.Contains("flow validate <document>", result.Output, StringComparison.Ordinal);
        Assert.Contains("Exit codes: 0 completed operation, 1 command/input/I/O failure, 2 semantic validation or automatic qualification failure, 130 cancellation.", result.Output, StringComparison.Ordinal);
        Assert.Contains("--language <en-US|pt-BR>", result.Output, StringComparison.Ordinal);
        Assert.Contains("--banner", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("\u001b[", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LocalizedHelpBannerAndErrors_PreserveCommandsAndDiagnosticCodes()
    {
        var application = FlowCliApplication.CreateDefault();

        var help = await RunAsync(application, ["--language", "pt-BR", "--banner", "--no-color", "help"]);
        var portugueseError = await RunAsync(application, ["--language", "pt-BR", "desconhecido"]);
        var englishError = await RunAsync(application, ["--language", "en-US", "desconhecido"]);

        Assert.Equal(0, help.ExitCode);
        Assert.Contains("Flow Engine .NET", help.Output, StringComparison.Ordinal);
        Assert.Contains("Opções globais:", help.Output, StringComparison.Ordinal);
        Assert.Contains("Comandos:", help.Output, StringComparison.Ordinal);
        Assert.Contains("flow import", help.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("\u001b[", help.Output, StringComparison.Ordinal);

        Assert.Equal(1, portugueseError.ExitCode);
        Assert.StartsWith("FLOWCLI_UNKNOWN_COMMAND:", portugueseError.Error, StringComparison.Ordinal);
        Assert.Contains("Comando desconhecido", portugueseError.Error, StringComparison.Ordinal);
        Assert.StartsWith("FLOWCLI_UNKNOWN_COMMAND:", englishError.Error, StringComparison.Ordinal);
        Assert.Contains("Unknown command", englishError.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EpubInventory_WritesNeutralCatalogAndRequiresForceForRecognizedReplacement()
    {
        using var workspace = new TemporaryWorkspace();
        var repositoryRoot = Directory.CreateDirectory(workspace.PathOf("repository")).FullName;
        var sourceDirectory = Directory.CreateDirectory(workspace.PathOf("private-books")).FullName;
        var outputDirectory = Directory.CreateDirectory(workspace.PathOf("private-reports")).FullName;
        var epubPath = Path.Combine(sourceDirectory, "identifying-file-name.epub");
        var outputPath = Path.Combine(outputDirectory, "inventory.json");
        CreateMinimalEpub(epubPath, "Identifying private title");
        var application = FlowCliApplication.CreateDefault();

        var first = await RunAsync(
            application,
            [
                "--language", "pt-BR", "--banner", "epub-inventory", sourceDirectory,
                "--output", outputPath, "--repository-root", repositoryRoot,
            ]);
        var refused = await RunAsync(
            application,
            [
                "epub-inventory", sourceDirectory,
                "--output", outputPath, "--repository-root", repositoryRoot,
            ]);
        var replaced = await RunAsync(
            application,
            [
                "epub-inventory", sourceDirectory,
                "--output", outputPath, "--repository-root", repositoryRoot, "--force",
            ]);

        Assert.Equal(0, first.ExitCode);
        Assert.Contains("Flow Engine .NET", first.Output, StringComparison.Ordinal);
        Assert.Contains("Arquivos EPUB encontrados: 1; conteúdos distintos: 1.", first.Output, StringComparison.Ordinal);
        Assert.Equal(1, refused.ExitCode);
        Assert.Contains("FLOWCLI_OUTPUT_EXISTS", refused.Error, StringComparison.Ordinal);
        Assert.Equal(0, replaced.ExitCode);
        var json = await File.ReadAllTextAsync(outputPath);
        Assert.Contains(EpubPrivateInventoryReport.CurrentFormat, json, StringComparison.Ordinal);
        Assert.DoesNotContain(sourceDirectory, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("identifying-file-name", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Identifying private title", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EpubInventoryQualification_RunsEligibleBooksTwiceAndWritesNeutralReport()
    {
        using var workspace = new TemporaryWorkspace();
        var repositoryRoot = Directory.CreateDirectory(workspace.PathOf("repository")).FullName;
        var sourceDirectory = Directory.CreateDirectory(workspace.PathOf("private-books")).FullName;
        var reportDirectory = Directory.CreateDirectory(workspace.PathOf("private-reports")).FullName;
        var epubPath = Path.Combine(sourceDirectory, "identifying-file-name.epub");
        var reportPath = Path.Combine(reportDirectory, "qualification.json");
        CreateMinimalEpub(epubPath, "Identifying private title");

        var result = await RunAsync(
            FlowCliApplication.CreateDefault(),
            [
                "--language", "pt-BR", "epub-inventory-qualify", sourceDirectory,
                "--report", reportPath, "--repository-root", repositoryRoot,
                "--legal-use", "--drm-free",
            ]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("1 elegíveis, 1 aprovados", result.Output, StringComparison.Ordinal);
        var json = await File.ReadAllTextAsync(reportPath);
        Assert.Contains(EpubPrivateQualificationReport.CurrentFormat, json, StringComparison.Ordinal);
        Assert.Contains("\"deterministicAcrossRepeatedRuns\": true", json, StringComparison.Ordinal);
        Assert.DoesNotContain(sourceDirectory, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("identifying-file-name", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Identifying private title", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PortugueseReports_AreLocalizedWithoutChangingGeneratedDocumentBytes()
    {
        using var workspace = new TemporaryWorkspace();
        var englishPath = workspace.PathOf("english.flow.json");
        var portuguesePath = workspace.PathOf("portuguese.flow.json");
        var application = FlowCliApplication.CreateDefault();

        var englishSample = await RunAsync(application, ["--language", "en-US", "sample", englishPath]);
        var portugueseSample = await RunAsync(application, ["--language", "pt-BR", "sample", portuguesePath]);
        var inspect = await RunAsync(application, ["--language", "pt-BR", "inspect", portuguesePath]);
        var validate = await RunAsync(application, ["--language", "pt-BR", "validate", portuguesePath]);
        var hash = await RunAsync(application, ["--language", "pt-BR", "hash", portuguesePath]);

        Assert.Equal(0, englishSample.ExitCode);
        Assert.Equal(0, portugueseSample.ExitCode);
        Assert.Equal(await File.ReadAllBytesAsync(englishPath), await File.ReadAllBytesAsync(portuguesePath));
        Assert.Contains("Título: The Flow Experiment", inspect.Output, StringComparison.Ordinal);
        Assert.Contains("Capítulos: 5", inspect.Output, StringComparison.Ordinal);
        Assert.Contains("Válido: nenhum erro de validação semântica.", validate.Output, StringComparison.Ordinal);
        Assert.Contains("Canonicalização: flow-c14n-0.1", hash.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnsupportedLanguage_UsesDocumentedEnglishFallback()
    {
        var result = await RunAsync(
            FlowCliApplication.CreateDefault(),
            ["--language", "fr-FR", "help"]);

        Assert.Equal(1, result.ExitCode);
        Assert.StartsWith("FLOWCLI_INVALID_VALUE:", result.Error, StringComparison.Ordinal);
        Assert.Contains("Unsupported language", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CorpusCommand_QualifiesAvailableManifestTwiceAndWritesDetailedReport()
    {
        using var workspace = new TemporaryWorkspace();
        var generatedDirectory = workspace.PathOf("generated");
        Directory.CreateDirectory(generatedDirectory);
        var epubPath = Path.Combine(generatedDirectory, "corpus-book.epub");
        CreateMinimalEpub(epubPath, "Corpus local");
        var manifestPath = workspace.PathOf("epub-corpus.json");
        var reportPath = workspace.PathOf("reports/corpus-qualification.json");
        WriteCorpusManifest(manifestPath, epubPath);

        var result = await RunAsync(
            FlowCliApplication.CreateDefault(),
            [
                "--language", "pt-BR", "corpus", manifestPath,
                "--repository-root", workspace.Root,
                "--report", reportPath,
            ]);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(reportPath));
        Assert.Contains("Publicações do corpus: 1", result.Output, StringComparison.Ordinal);
        Assert.Contains("Determinístico entre execuções repetidas: sim", result.Output, StringComparison.Ordinal);
        AssertJsonEncoding(reportPath);
        var report = await File.ReadAllTextAsync(reportPath);
        Assert.Contains("flow-epub-corpus-qualification-0.1", report, StringComparison.Ordinal);
        Assert.Contains("\"deterministicAcrossRepeatedRuns\": true", report, StringComparison.Ordinal);
        Assert.DoesNotContain(workspace.Root, report, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CorpusCommand_InvalidManifestIsLocalizedAndDoesNotWriteReport()
    {
        using var workspace = new TemporaryWorkspace();
        var manifestPath = workspace.PathOf("invalid-corpus.json");
        var reportPath = workspace.PathOf("report.json");
        await File.WriteAllTextAsync(manifestPath, "{}", new UTF8Encoding(false));

        var result = await RunAsync(
            FlowCliApplication.CreateDefault(),
            [
                "--language", "pt-BR", "corpus", manifestPath,
                "--repository-root", workspace.Root,
                "--report", reportPath,
            ]);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("EPC004", result.Error, StringComparison.Ordinal);
        Assert.Contains("Um valor obrigatório do corpus está ausente.", result.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(reportPath));
    }

    [Fact]
    public async Task CorpusCommand_RequiresForceAndResumesOnlyRecognizedInterruptedOutput()
    {
        using var workspace = new TemporaryWorkspace();
        var generatedDirectory = workspace.PathOf("generated");
        Directory.CreateDirectory(generatedDirectory);
        var epubPath = Path.Combine(generatedDirectory, "corpus-book.epub");
        CreateMinimalEpub(epubPath);
        var manifestPath = workspace.PathOf("epub-corpus.json");
        var reportPath = workspace.PathOf("corpus-report.json");
        var repositoryRoot = workspace.PathOf("repository");
        Directory.CreateDirectory(repositoryRoot);
        WriteCorpusManifest(manifestPath, epubPath);
        await File.WriteAllTextAsync(reportPath, "keep", new UTF8Encoding(false));
        var baseArguments = new[]
        {
            "--language", "pt-BR", "corpus", manifestPath,
            "--repository-root", repositoryRoot,
            "--external-root", workspace.Root,
            "--report", reportPath,
        };
        var application = FlowCliApplication.CreateDefault();

        var refused = await RunAsync(application, baseArguments);
        var forced = await RunAsync(application, [.. baseArguments, "--force"]);

        Assert.Equal(1, refused.ExitCode);
        Assert.Contains("FLOWCLI_OUTPUT_POLICY", refused.Error, StringComparison.Ordinal);
        Assert.Contains("Use --force", refused.Error, StringComparison.Ordinal);
        Assert.Equal(2, forced.ExitCode);
        Assert.Contains("flow-epub-corpus-qualification-0.1", await File.ReadAllTextAsync(reportPath));

        File.Delete(reportPath);
        var interrupted = workspace.PathOf($".corpus-report.json.{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(interrupted, "partial", new UTF8Encoding(false));
        var interruptedRefused = await RunAsync(application, baseArguments);
        var resumed = await RunAsync(application, [.. baseArguments, "--resume"]);

        Assert.Equal(1, interruptedRefused.ExitCode);
        Assert.Contains("Use --resume", interruptedRefused.Error, StringComparison.Ordinal);
        Assert.Equal(2, resumed.ExitCode);
        Assert.Contains("Resíduos da execução interrompida removidos", resumed.Output, StringComparison.Ordinal);
        Assert.Matches("ID da execução: [0-9a-f]{32}", resumed.Output);
        Assert.False(File.Exists(interrupted));
        Assert.True(File.Exists(reportPath));
        var lockPath = workspace.PathOf(".corpus-report.json.flow-execution.lock");
        using var executionLock = JsonDocument.Parse(await File.ReadAllBytesAsync(lockPath));
        Assert.Equal("completed", executionLock.RootElement.GetProperty("state").GetString());
        Assert.DoesNotContain(workspace.Root, await File.ReadAllTextAsync(lockPath), StringComparison.OrdinalIgnoreCase);
        var executionId = executionLock.RootElement.GetProperty("executionId").GetString()!;

        var statusPath = workspace.PathOf("execution-status.json");
        var status = await RunAsync(
            application,
            ["--language", "pt-BR", "execution-status", reportPath, "--json", statusPath]);
        Assert.Equal(0, status.ExitCode);
        Assert.Contains("Estado da execução: concluída", status.Output, StringComparison.Ordinal);
        Assert.Contains(executionId, status.Output, StringComparison.Ordinal);
        AssertJsonEncoding(statusPath);
        var statusJson = await File.ReadAllTextAsync(statusPath);
        Assert.Contains("flow-cli-execution-status-0.1", statusJson, StringComparison.Ordinal);
        Assert.DoesNotContain(workspace.Root, statusJson, StringComparison.OrdinalIgnoreCase);
        var statusAgain = await RunAsync(
            application,
            ["execution-status", reportPath, "--json", statusPath, "--force"]);
        Assert.Equal(0, statusAgain.ExitCode);
        Assert.Equal(statusJson, await File.ReadAllTextAsync(statusPath));

        var cleanupArtifact = workspace.PathOf($".corpus-report.json.{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(cleanupArtifact, "partial", new UTF8Encoding(false));
        var wrongClean = await RunAsync(
            application,
            ["execution-clean", reportPath, "--execution-id", Guid.NewGuid().ToString("N")]);

        Assert.Equal(1, wrongClean.ExitCode);
        Assert.True(File.Exists(cleanupArtifact));

        var clean = await RunAsync(
            application,
            ["--language", "pt-BR", "execution-clean", reportPath, "--execution-id", executionId]);

        Assert.Equal(0, clean.ExitCode);
        Assert.Contains("Resíduos removidos ou recuperados: 1", clean.Output, StringComparison.Ordinal);
        Assert.False(File.Exists(cleanupArtifact));
    }

    [Fact]
    public async Task EpubQualifyAndReview_RunAutomaticGateThenProduceLocalReviewMaterial()
    {
        using var workspace = new TemporaryWorkspace();
        var epubPath = workspace.PathOf("large-candidate.epub");
        CreateGateReadyEpub(epubPath, chapterCount: 20);
        var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(epubPath)));
        var gateReportPath = workspace.PathOf("large-candidate-gate.json");
        var repositoryRoot = workspace.PathOf("repository");
        var reviewDirectory = workspace.PathOf("large-candidate-review");
        Directory.CreateDirectory(repositoryRoot);
        var application = FlowCliApplication.CreateDefault();

        var gate = await RunAsync(
            application,
            [
                "--language", "pt-BR", "epub-qualify", epubPath,
                "--candidate-id", "candidate-001",
                "--sha256", hash,
                "--report", gateReportPath,
                "--repository-root", repositoryRoot,
                "--legal-use",
                "--drm-free",
            ]);
        var review = await RunAsync(
            application,
            [
                "--language", "pt-BR", "epub-review", epubPath,
                "--candidate-id", "candidate-001",
                "--sha256", hash,
                "--output", reviewDirectory,
                "--repository-root", repositoryRoot,
                "--legal-use",
                "--drm-free",
                "--ui-language", "pt-BR",
            ]);

        Assert.Equal(0, gate.ExitCode);
        Assert.Contains("Candidato do gate: candidate-001", gate.Output, StringComparison.Ordinal);
        Assert.Contains("0 não aprovadas", gate.Output, StringComparison.Ordinal);
        Assert.True(File.Exists(gateReportPath));
        AssertJsonEncoding(gateReportPath);
        var gateReport = await File.ReadAllTextAsync(gateReportPath);
        Assert.Contains("flow-epub-large-publication-gate-0.1", gateReport, StringComparison.Ordinal);
        Assert.Contains("\"status\": \"inconclusive\"", gateReport, StringComparison.Ordinal);
        Assert.Contains("\"included\": false", gateReport, StringComparison.Ordinal);
        Assert.DoesNotContain(epubPath, gateReport, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(0, review.ExitCode);
        Assert.Contains("Diretório da revisão:", review.Output, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(reviewDirectory, "review.html")));
        Assert.True(File.Exists(Path.Combine(reviewDirectory, "review-checklist.json")));
        Assert.True(File.Exists(Path.Combine(reviewDirectory, "mobile", "index.html")));
        Assert.True(File.Exists(Path.Combine(reviewDirectory, "desktop", "index.html")));
        Assert.Contains(
            "<html lang=\"pt-BR\">",
            await File.ReadAllTextAsync(Path.Combine(reviewDirectory, "review.html")),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            epubPath,
            await File.ReadAllTextAsync(Path.Combine(reviewDirectory, "review-manifest.json")),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EpubQualify_RejectsLicensedSourceInsideRepositoryBeforeWritingReport()
    {
        using var workspace = new TemporaryWorkspace();
        var repositoryRoot = workspace.PathOf("repository");
        Directory.CreateDirectory(repositoryRoot);
        var epubPath = Path.Combine(repositoryRoot, "licensed.epub");
        CreateMinimalEpub(epubPath);
        var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(epubPath)));
        var reportPath = workspace.PathOf("gate.json");

        var result = await RunAsync(
            FlowCliApplication.CreateDefault(),
            [
                "epub-qualify", epubPath,
                "--candidate-id", "candidate-unsafe",
                "--sha256", hash,
                "--report", reportPath,
                "--repository-root", repositoryRoot,
                "--legal-use",
                "--drm-free",
            ]);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("FLOWCLI_INVALID_OUTPUT", result.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(reportPath));
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

    private static void AssertJsonEncoding(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        Assert.DoesNotContain((byte)'\r', bytes);
    }

    private static string[] DirectorySnapshot(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        .Select(path =>
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            return $"{relative}:{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)))}";
        })
        .Order(StringComparer.Ordinal)
        .ToArray();

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

    private static void CreateDiagnosticHeavyEpub(string path, int chapterCount)
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

        var manifest = new StringBuilder();
        var spine = new StringBuilder();
        for (var index = 1; index <= chapterCount; index++)
        {
            manifest.AppendLine($"    <item id=\"chapter-{index}\" href=\"text/chapter-{index}.xhtml\" media-type=\"application/xhtml+xml\" />");
            spine.AppendLine($"    <itemref idref=\"chapter-{index}\" />");
            AddText(
                archive,
                $"EPUB/text/chapter-{index}.xhtml",
                $$"""
                <?xml version="1.0" encoding="utf-8"?>
                <html xmlns="http://www.w3.org/1999/xhtml" lang="pt-BR">
                  <head><title>Capítulo {{index}}</title></head>
                  <body>
                    <h1 id="chapter-{{index}}">Capítulo {{index}}</h1>
                    <unknown>Texto preservado do capítulo {{index}}.</unknown>
                  </body>
                </html>
                """);
        }

        AddText(
            archive,
            "EPUB/package.opf",
            $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="book-id">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="book-id">urn:flow:test:diagnostic-heavy</dc:identifier>
                <dc:title>Diagnósticos agregados</dc:title>
                <dc:language>pt-BR</dc:language>
              </metadata>
              <manifest>
            {{manifest.ToString().TrimEnd()}}
              </manifest>
              <spine>
            {{spine.ToString().TrimEnd()}}
              </spine>
            </package>
            """);
    }

    private static void CreateGateReadyEpub(string path, int chapterCount)
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

        var manifest = new StringBuilder();
        var spine = new StringBuilder();
        var navigation = new StringBuilder();
        for (var index = 1; index <= chapterCount; index++)
        {
            manifest.AppendLine($"    <item id=\"chapter-{index}\" href=\"text/chapter-{index}.xhtml\" media-type=\"application/xhtml+xml\" />");
            spine.AppendLine($"    <itemref idref=\"chapter-{index}\" />");
            navigation.AppendLine($"      <li><a href=\"text/chapter-{index}.xhtml#chapter-{index}\">Chapter {index}</a></li>");
            AddText(
                archive,
                $"EPUB/text/chapter-{index}.xhtml",
                $$"""
                <?xml version="1.0" encoding="utf-8"?>
                <html xmlns="http://www.w3.org/1999/xhtml" lang="en">
                  <head><title>Chapter {{index}}</title></head>
                  <body>
                    <h1 id="chapter-{{index}}">Chapter {{index}}</h1>
                    <p id="paragraph-{{index}}">Deterministic gate content for chapter {{index}}.</p>
                  </body>
                </html>
                """);
        }

        AddText(
            archive,
            "EPUB/package.opf",
            $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="book-id">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="book-id">urn:flow:test:large-gate</dc:identifier>
                <dc:title>Large gate fixture</dc:title>
                <dc:language>en</dc:language>
              </metadata>
              <manifest>
                <item id="navigation" href="navigation.xhtml" media-type="application/xhtml+xml" properties="nav" />
                <item id="image" href="image.png" media-type="image/png" />
            {{manifest.ToString().TrimEnd()}}
              </manifest>
              <spine>
            {{spine.ToString().TrimEnd()}}
              </spine>
            </package>
            """);
        AddText(
            archive,
            "EPUB/navigation.xhtml",
            $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops" lang="en">
              <head><title>Contents</title></head>
              <body><nav epub:type="toc"><h1>Contents</h1><ol>
            {{navigation.ToString().TrimEnd()}}
              </ol></nav></body>
            </html>
            """);
        AddBytes(
            archive,
            "EPUB/image.png",
            Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
    }

    private static void WriteCorpusManifest(string path, string epubPath)
    {
        var file = new FileInfo(epubPath);
        var manifest = new EpubCorpusManifest(
            EpubCorpusManifest.CurrentFormat,
            [
                new EpubCorpusPublication(
                    new EpubCorpusPublicationId("cli-corpus-book"),
                    "CLI corpus book",
                    "project:Flow.Cli.Tests",
                    new EpubCorpusLicense("Flow project fixture", "Generated by the CLI integration test"),
                    EpubCorpusPublicationKind.ProjectFixture,
                    EpubCorpusRedistribution.Allowed,
                    "generated/corpus-book.epub",
                    EpubVersionFamily.Epub3,
                    file.Length,
                    ["pt-PT"],
                    ["xhtml"],
                    [],
                    [],
                    [],
                    new EpubCorpusSha256(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(epubPath)))))
            ]);
        using var destination = File.Create(path);
        new EpubCorpusManifestJsonSerializer().Write(manifest, destination);
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

    private static void AddBytes(ZipArchive archive, string path, ReadOnlySpan<byte> content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(content);
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
