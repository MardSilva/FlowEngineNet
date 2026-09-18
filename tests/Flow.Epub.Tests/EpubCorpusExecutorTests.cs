using System.Security.Cryptography;
using System.Text;
using Flow.Core;
using Flow.Documents;
using Flow.Epub.Corpus;
using Flow.Layout;
using Flow.Rendering.Html;
using Flow.Security;

namespace Flow.Epub.Tests;

public sealed class EpubCorpusExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_ProvidesEndToEndEvidenceForPassingFixture()
    {
        using var workspace = new CorpusExecutionWorkspace();
        var publication = workspace.AddValidFixture("passing");

        var report = await new EpubCorpusExecutor().ExecuteAsync(workspace.Manifest(publication), workspace.Options());

        var result = Assert.Single(report.Publications);
        Assert.True(
            result.Status == EpubCorpusExecutionStatus.Passed,
            string.Join(Environment.NewLine, result.Diagnostics.Select(static item => $"{item.Code}/{item.Phase}/{item.Resource}: {item.Message}")));
        Assert.Equal(1, report.Summary.Passed);
        Assert.All(Enum.GetValues<EpubCorpusExecutionPhase>(), phase => Assert.Contains(phase, result.CompletedPhases));
        Assert.Equal("Epub3", result.Evidence.EpubVersion);
        Assert.NotNull(result.Evidence.CanonicalHash);
        Assert.True(result.Evidence.ImportedNodeCount > 0);
        Assert.True(result.Evidence.ImportedAssetCount > 0);
        Assert.Equal(2, result.Evidence.HtmlPackageCount);
        Assert.True(result.Evidence.HtmlFileCount > 0);
        Assert.NotNull(result.EnvironmentMetrics);
        Assert.True(result.EnvironmentMetrics.ApproximatePeakManagedBytes > 0);
        Assert.True(result.EnvironmentMetrics.ApproximatePeakWorkingSetBytes > 0);
    }

    [Fact]
    public async Task ExecuteAsync_VerifiesHtmlPackagesContainingSafePercentEncodedMailto()
    {
        const string chapter = """
            <html xmlns="http://www.w3.org/1999/xhtml"><body>
              <h1 id="start">Start</h1>
              <p><a href="mailto:reader%40example.invalid?subject=Hello%20Flow">Email</a></p>
            </body></html>
            """;
        using var workspace = new CorpusExecutionWorkspace();
        using var epub = MinimalEpubFactory.Create(chapterOne: chapter);
        var publication = workspace.AddBytes(
            "encoded-mailto",
            epub.ToArray(),
            expectedFeatures: ["spine"],
            expectedResults: ["html-book-package"]);

        var result = Assert.Single((await new EpubCorpusExecutor()
            .ExecuteAsync(workspace.Manifest(publication), workspace.Options())).Publications);

        Assert.Equal(EpubCorpusExecutionStatus.Passed, result.Status);
        Assert.Equal(2, result.Evidence.HtmlPackageCount);
        Assert.DoesNotContain(result.Diagnostics, static item =>
            item.Code == EpubCorpusExecutionDiagnosticCodes.HtmlPackageFailed);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidImportedDocumentFailsValidationAndKeepsPartialEvidence()
    {
        using var workspace = new CorpusExecutionWorkspace();
        var publication = workspace.AddValidFixture("invalid-document", expectedFeatures: [], expectedResults: []);
        var invalid = CreateInvalidDocument();
        var executor = CreateExecutor(importer: new FixedImporter(new EpubImportResult(invalid, [])));

        var report = await executor.ExecuteAsync(workspace.Manifest(publication), workspace.Options());

        var result = Assert.Single(report.Publications);
        Assert.Equal(EpubCorpusExecutionStatus.Failed, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Phase == EpubCorpusExecutionPhase.Validation
            && diagnostic.SourceCode == ValidationDiagnosticCodes.DuplicateNodeId);
        Assert.True(result.Evidence.ImportedNodeCount > 0);
        Assert.DoesNotContain(EpubCorpusExecutionPhase.MobileLayout, result.CompletedPhases);
    }

    [Fact]
    public async Task ExecuteAsync_CanonicalHashDivergenceIsFailed()
    {
        using var workspace = new CorpusExecutionWorkspace();
        var publication = workspace.AddValidFixture("hash-divergence");
        var executor = CreateExecutor(serializer: new MutatingRoundTripSerializer());

        var result = Assert.Single((await executor.ExecuteAsync(workspace.Manifest(publication), workspace.Options())).Publications);

        Assert.Equal(EpubCorpusExecutionStatus.Failed, result.Status);
        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Code == EpubCorpusExecutionDiagnosticCodes.CanonicalHashMismatch);
    }

    [Fact]
    public async Task ExecuteAsync_CancellationIsPropagatedAndDiscoveryWorkspaceIsCleaned()
    {
        using var workspace = new CorpusExecutionWorkspace();
        var publication = workspace.AddValidFixture("cancelled");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var discovery = new EpubCorpusDiscoveryService(() =>
            Directory.CreateDirectory(Path.Combine(workspace.TemporaryRoot, "cancelled-session")).FullName);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateExecutor(discoveryService: discovery).ExecuteAsync(
            workspace.Manifest(publication),
            workspace.Options(),
            cancellation.Token));

        Assert.Empty(Directory.EnumerateDirectories(workspace.TemporaryRoot));
    }

    [Fact]
    public async Task ExecuteAsync_RoundTripWorkspaceIsCleanedAfterSuccessFailureAndCancellation()
    {
        using var workspace = new CorpusExecutionWorkspace();
        var publication = workspace.AddValidFixture("temporary-cleanup");

        var successful = await CreateExecutor(temporaryDirectoryRoot: workspace.TemporaryRoot)
            .ExecuteAsync(workspace.Manifest(publication), workspace.Options());
        Assert.Equal(EpubCorpusExecutionStatus.Passed, Assert.Single(successful.Publications).Status);
        Assert.Empty(Directory.EnumerateFileSystemEntries(workspace.TemporaryRoot));

        var failed = await CreateExecutor(
                serializer: new FailingSerializer(),
                temporaryDirectoryRoot: workspace.TemporaryRoot)
            .ExecuteAsync(workspace.Manifest(publication), workspace.Options());
        Assert.Equal(EpubCorpusExecutionStatus.Failed, Assert.Single(failed.Publications).Status);
        Assert.Empty(Directory.EnumerateFileSystemEntries(workspace.TemporaryRoot));

        using var cancellation = new CancellationTokenSource();
        var cancellingExecutor = CreateExecutor(
            serializer: new CancellingSerializer(cancellation),
            temporaryDirectoryRoot: workspace.TemporaryRoot);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancellingExecutor.ExecuteAsync(
            workspace.Manifest(publication),
            workspace.Options(),
            cancellation.Token));
        Assert.Empty(Directory.EnumerateFileSystemEntries(workspace.TemporaryRoot));
    }

    [Fact]
    public async Task ExecuteAsync_MemoryFailureIsPropagatedAndTemporaryWorkspaceIsCleaned()
    {
        using var workspace = new CorpusExecutionWorkspace();
        var publication = workspace.AddValidFixture("memory-failure");
        var executor = CreateExecutor(
            serializer: new MemoryFailingSerializer(),
            temporaryDirectoryRoot: workspace.TemporaryRoot);

        await Assert.ThrowsAsync<OutOfMemoryException>(() => executor.ExecuteAsync(
            workspace.Manifest(publication),
            workspace.Options()));

        Assert.Empty(Directory.EnumerateFileSystemEntries(workspace.TemporaryRoot));
    }

    [Fact]
    [Trait("Category", "Regression")]
    public async Task ExecuteAsync_LargerOwnedFixtureProcessesPackagesSequentiallyAndKeepsStableEvidence()
    {
        using var workspace = new CorpusExecutionWorkspace();
        var publication = workspace.AddLargeFixture("larger-owned", paragraphCount: 400);
        var events = new List<string>();
        var executor = CreateExecutor(
            layoutEngine: new RecordingLayoutEngine(events),
            htmlRenderer: new RecordingHtmlRenderer(events),
            temporaryDirectoryRoot: workspace.TemporaryRoot);

        var first = Assert.Single((await executor.ExecuteAsync(workspace.Manifest(publication), workspace.Options())).Publications);
        var second = Assert.Single((await executor.ExecuteAsync(workspace.Manifest(publication), workspace.Options())).Publications);

        Assert.Equal(EpubCorpusExecutionStatus.Passed, first.Status);
        Assert.Equal(["layout-small", "render-small", "layout-large", "render-large", "layout-small", "render-small", "layout-large", "render-large"], events);
        Assert.Equal(first.Evidence.CanonicalHash, second.Evidence.CanonicalHash);
        Assert.Equal(first.Evidence.FlowJsonBytes, second.Evidence.FlowJsonBytes);
        Assert.Equal(first.Evidence.HtmlBytes, second.Evidence.HtmlBytes);
        Assert.Equal(2, first.Evidence.HtmlPackageCount);
        Assert.True(first.Evidence.ImportedNodeCount >= 400);
        Assert.True(first.EnvironmentMetrics?.ApproximatePeakManagedBytes > 0);
        Assert.True(first.EnvironmentMetrics?.ApproximatePeakWorkingSetBytes > 0);
        Assert.Empty(Directory.EnumerateFileSystemEntries(workspace.TemporaryRoot));

        Assert.Equal(
            EpubCorpusExecutionReportJsonSerializer.Serialize(new EpubCorpusExecutionReport([first])),
            EpubCorpusExecutionReportJsonSerializer.Serialize(new EpubCorpusExecutionReport([second])));
    }

    [Fact]
    public async Task ExecuteAsync_MissingExternalCorpusIsSkipped()
    {
        using var workspace = new CorpusExecutionWorkspace();
        var bytes = "private publication"u8.ToArray();
        var publication = workspace.CreatePublication(
            "missing-private",
            bytes,
            "private/book.epub",
            EpubCorpusPublicationKind.LocalNonRedistributablePublication,
            EpubCorpusRedistribution.Prohibited,
            [],
            []);

        var report = await new EpubCorpusExecutor().ExecuteAsync(workspace.Manifest(publication), workspace.Options());

        var result = Assert.Single(report.Publications);
        Assert.Equal(EpubCorpusExecutionStatus.Skipped, result.Status);
        Assert.Equal(1, report.Summary.Skipped);
    }

    [Fact]
    public async Task ExecuteAsync_FailureOfOnePublicationDoesNotBlockAnother()
    {
        using var workspace = new CorpusExecutionWorkspace();
        var broken = workspace.AddBytes("broken", "not a zip"u8.ToArray());
        var passing = workspace.AddValidFixture("passing-after-failure");

        var report = await new EpubCorpusExecutor().ExecuteAsync(workspace.Manifest(broken, passing), workspace.Options());

        Assert.Equal(EpubCorpusExecutionStatus.Failed, report.Publications.Single(item => item.Id == broken.Id).Status);
        Assert.Equal(EpubCorpusExecutionStatus.Passed, report.Publications.Single(item => item.Id == passing.Id).Status);
        Assert.Equal(1, report.Summary.Failed);
        Assert.Equal(1, report.Summary.Passed);
    }

    [Fact]
    public async Task DeterministicReport_OmitsEnvironmentAndWritesAtomicallyAsUtf8Lf()
    {
        using var workspace = new CorpusExecutionWorkspace();
        var publication = workspace.AddValidFixture("deterministic");
        var executor = new EpubCorpusExecutor();
        var first = await executor.ExecuteAsync(workspace.Manifest(publication), workspace.Options());
        var second = await executor.ExecuteAsync(workspace.Manifest(publication), workspace.Options());

        var firstBytes = EpubCorpusExecutionReportJsonSerializer.Serialize(first);
        var secondBytes = EpubCorpusExecutionReportJsonSerializer.Serialize(second);
        var text = Encoding.UTF8.GetString(firstBytes);
        Assert.Equal(firstBytes, secondBytes);
        Assert.DoesNotContain("totalDurationTicks", text, StringComparison.Ordinal);
        Assert.DoesNotContain("approximatePeakWorkingSetBytes", text, StringComparison.Ordinal);
        Assert.Contains("\"included\": false", text, StringComparison.Ordinal);
        Assert.DoesNotContain(workspace.Root, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain('\r', text);
        Assert.Equal((byte)'\n', firstBytes[^1]);
        Assert.False(firstBytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));

        var environmentText = Encoding.UTF8.GetString(
            EpubCorpusExecutionReportJsonSerializer.Serialize(first, includeNonDeterministicEnvironment: true));
        Assert.Contains("approximatePeakManagedBytes", environmentText, StringComparison.Ordinal);
        Assert.Contains("approximatePeakWorkingSetBytes", environmentText, StringComparison.Ordinal);

        var output = Path.Combine(workspace.OutputRoot, "corpus-report.json");
        await EpubCorpusExecutionReportJsonSerializer.WriteAtomicallyAsync(first, output);
        Assert.Equal(firstBytes, await File.ReadAllBytesAsync(output));
        Assert.Empty(Directory.EnumerateFiles(workspace.OutputRoot, "*.tmp"));
    }

    [Fact]
    public async Task UnknownDeclaredExpectationMakesResultInconclusiveRatherThanPassed()
    {
        using var workspace = new CorpusExecutionWorkspace();
        var publication = workspace.AddValidFixture(
            "unknown-expectation",
            expectedFeatures: ["future-layout-feature"],
            expectedResults: ["inspection-success", "import-success", "valid-flow-document"]);

        var result = Assert.Single((await new EpubCorpusExecutor().ExecuteAsync(
            workspace.Manifest(publication), workspace.Options())).Publications);

        Assert.Equal(EpubCorpusExecutionStatus.Inconclusive, result.Status);
        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Code == EpubCorpusExecutionDiagnosticCodes.UnsupportedExpectation);
    }

    [Fact]
    public async Task MissingDeclaredTableFailsExpectationWithoutHidingOtherEvidence()
    {
        using var workspace = new CorpusExecutionWorkspace();
        var publication = workspace.AddValidFixture(
            "missing-table",
            expectedFeatures: ["spine", "table"],
            expectedResults: ["inspection-success", "import-success", "valid-flow-document"]);

        var result = Assert.Single((await new EpubCorpusExecutor().ExecuteAsync(
            workspace.Manifest(publication), workspace.Options())).Publications);

        Assert.Equal(EpubCorpusExecutionStatus.Failed, result.Status);
        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Code == EpubCorpusExecutionDiagnosticCodes.ExpectationFailed
            && diagnostic.Resource == "table");
        Assert.NotNull(result.Evidence.CanonicalHash);
        Assert.Equal(2, result.Evidence.HtmlPackageCount);
    }

    [Fact]
    public async Task AtomicReportWrite_CancellationKeepsExistingDestinationAndLeavesNoTemporaryFile()
    {
        using var workspace = new CorpusExecutionWorkspace();
        var report = new EpubCorpusExecutionReport([]);
        var output = Path.Combine(workspace.OutputRoot, "existing.json");
        await File.WriteAllTextAsync(output, "existing");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            EpubCorpusExecutionReportJsonSerializer.WriteAtomicallyAsync(
                report,
                output,
                cancellationToken: cancellation.Token));

        Assert.Equal("existing", await File.ReadAllTextAsync(output));
        Assert.Single(Directory.EnumerateFiles(workspace.OutputRoot));
    }

    [Fact]
    public async Task EpubCheckFailure_IsReportedAsDivergenceWithoutOverwritingFlowResultOrHash()
    {
        using var workspace = new CorpusExecutionWorkspace();
        var publication = workspace.AddValidFixture("external-divergence");
        var baseline = Assert.Single((await new EpubCorpusExecutor().ExecuteAsync(
            workspace.Manifest(publication), workspace.Options())).Publications);
        var externalEvidence = new EpubCheckEvidence(
            EpubCheckEvidenceStatus.NonConformant,
            "5.4.0",
            1,
            errorCount: 1,
            messages: [new EpubCheckMessageEvidence("RSC-001", "ERROR", "Missing resource.", "EPUB/missing.xhtml")]);

        var result = Assert.Single((await CreateExecutor(epubCheckAdapter: new FixedEpubCheckAdapter(externalEvidence))
            .ExecuteAsync(workspace.Manifest(publication), workspace.Options())).Publications);

        Assert.Equal(EpubCorpusExecutionStatus.Passed, result.Status);
        Assert.Equal(EpubCorpusEvidenceRelationship.FlowPassedEpubCheckFailed, result.EvidenceRelationship);
        Assert.Equal(baseline.Evidence.CanonicalHash, result.Evidence.CanonicalHash);
        var report = Encoding.UTF8.GetString(EpubCorpusExecutionReportJsonSerializer.Serialize(new EpubCorpusExecutionReport([result])));
        Assert.Contains("flow-passed-epub-check-failed", report, StringComparison.Ordinal);
        Assert.Contains("non-conformant", report, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "EpubCorpus")]
    public async Task ExternalCorpus_WhenExplicitlyConfigured_CanRunWithoutAffectingDefaultSuite()
    {
        var root = Environment.GetEnvironmentVariable(EpubCorpusDiscoveryOptions.ExternalCorpusEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        var catalogPath = Path.Combine(root, "epub-corpus.json");
        if (!File.Exists(catalogPath))
        {
            return;
        }

        await using var catalog = File.OpenRead(catalogPath);
        var read = new EpubCorpusManifestJsonSerializer().Read(catalog);
        Assert.True(read.IsSuccess, string.Join(Environment.NewLine, read.Diagnostics.Select(static item => $"{item.Code}: {item.Message}")));
        var repositoryRoot = FindRepositoryRoot();
        var report = await new EpubCorpusExecutor().ExecuteAsync(
            read.Manifest!,
            new EpubCorpusDiscoveryOptions(repositoryRoot, root));

        Assert.DoesNotContain(report.Publications, static item => item.Status == EpubCorpusExecutionStatus.Failed);
    }

    private static EpubCorpusExecutor CreateExecutor(
        IEpubCorpusDiscoveryService? discoveryService = null,
        IEpubImporter? importer = null,
        IFlowDocumentSerializer? serializer = null,
        IEpubCheckAdapter? epubCheckAdapter = null,
        ILayoutEngine? layoutEngine = null,
        IHtmlBookPackageRenderer? htmlRenderer = null,
        string? temporaryDirectoryRoot = null) => new(
        discoveryService ?? new EpubCorpusDiscoveryService(),
        new EpubPublicationInspector(),
        importer ?? new EpubImporter(),
        new DocumentValidator(),
        new EpubFidelityAnalyzer(),
        serializer ?? new FlowJsonDocumentSerializer(),
        new Sha256DocumentIntegrityService(new FlowDocumentCanonicalizer()),
        layoutEngine ?? new AdaptiveLayoutEngine(),
        htmlRenderer ?? new HtmlBookPackageRenderer(),
        epubCheckAdapter,
        temporaryDirectoryRoot);

    private static FlowDocument CreateInvalidDocument()
    {
        var duplicate = new NodeId("duplicate");
        return new FlowDocument(
            new DocumentIdentity(new DocumentId("urn:flow:corpus:invalid")),
            new DocumentMetadata("Invalid"),
            new DocumentContent(
            [
                new Paragraph(duplicate, [new Text("First")]),
                new Paragraph(duplicate, [new Text("Second")]),
            ]));
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Flow.sln")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new DirectoryNotFoundException("The Flow repository root was not found.");
    }

    private sealed class FixedImporter(EpubImportResult result) : IEpubImporter
    {
        public Task<EpubImportResult> ImportAsync(Stream source, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(result);
        }
    }

    private sealed class MutatingRoundTripSerializer : IFlowDocumentSerializer
    {
        private readonly FlowJsonDocumentSerializer inner = new();

        public Task SerializeAsync(FlowDocument document, Stream destination, CancellationToken cancellationToken = default) =>
            inner.SerializeAsync(document, destination, cancellationToken);

        public async Task<FlowDocument> DeserializeAsync(Stream source, CancellationToken cancellationToken = default)
        {
            var document = await inner.DeserializeAsync(source, cancellationToken);
            return new FlowDocument(
                document.Identity,
                new DocumentMetadata(
                    document.Metadata.Title + " changed",
                    document.Metadata.Language,
                    document.Metadata.Authors,
                    document.Metadata.Subtitle,
                    document.Metadata.Description),
                document.Content,
                document.Assets.Values,
                document.Presentation,
                document.Integrity);
        }
    }

    private sealed class FailingSerializer : IFlowDocumentSerializer
    {
        public async Task SerializeAsync(FlowDocument document, Stream destination, CancellationToken cancellationToken = default)
        {
            await destination.WriteAsync("partial"u8.ToArray(), cancellationToken);
            throw new InvalidDataException("Expected test failure.");
        }

        public Task<FlowDocument> DeserializeAsync(Stream source, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class CancellingSerializer(CancellationTokenSource cancellation) : IFlowDocumentSerializer
    {
        public async Task SerializeAsync(FlowDocument document, Stream destination, CancellationToken cancellationToken = default)
        {
            await destination.WriteAsync("partial"u8.ToArray(), cancellationToken);
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
        }

        public Task<FlowDocument> DeserializeAsync(Stream source, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class MemoryFailingSerializer : IFlowDocumentSerializer
    {
        public Task SerializeAsync(FlowDocument document, Stream destination, CancellationToken cancellationToken = default) =>
            throw new OutOfMemoryException("Expected test failure.");

        public Task<FlowDocument> DeserializeAsync(Stream source, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingLayoutEngine(List<string> events) : ILayoutEngine
    {
        private readonly AdaptiveLayoutEngine inner = new();

        public LayoutDocument Layout(FlowDocument document, LayoutContext context)
        {
            events.Add(context.DeviceClass == DeviceClass.Phone ? "layout-small" : "layout-large");
            return inner.Layout(document, context);
        }
    }

    private sealed class RecordingHtmlRenderer(List<string> events) : IHtmlBookPackageRenderer
    {
        private readonly HtmlBookPackageRenderer inner = new();

        public HtmlBookPackage Render(
            FlowDocument document,
            LayoutDocument layout,
            UserReadingPreferences userPreferences,
            HtmlBookIntegrity integrity)
        {
            events.Add(layout.Profile.ViewportCategory == ViewportCategory.Small ? "render-small" : "render-large");
            return inner.Render(document, layout, userPreferences, integrity);
        }
    }

    private sealed class FixedEpubCheckAdapter(EpubCheckEvidence evidence) : IEpubCheckAdapter
    {
        public Task<EpubCheckEvidence> EvaluateAsync(
            Stream epub,
            string logicalPublicationId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(evidence);
        }
    }

    private sealed class CorpusExecutionWorkspace : IDisposable
    {
        public CorpusExecutionWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), $"flow-corpus-executor-tests-{Guid.NewGuid():N}");
            RepositoryRoot = Directory.CreateDirectory(Path.Combine(Root, "repository")).FullName;
            TemporaryRoot = Directory.CreateDirectory(Path.Combine(Root, "temporary")).FullName;
            OutputRoot = Directory.CreateDirectory(Path.Combine(Root, "output")).FullName;
        }

        public string Root { get; }

        public string RepositoryRoot { get; }

        public string TemporaryRoot { get; }

        public string OutputRoot { get; }

        public EpubCorpusPublication AddValidFixture(
            string id,
            IEnumerable<string>? expectedFeatures = null,
            IEnumerable<string>? expectedResults = null)
        {
            using var stream = MinimalEpubFactory.Create();
            return AddBytes(
                id,
                stream.ToArray(),
                expectedFeatures ?? ["figure", "internal-link", "ordered-list", "spine", "unordered-list"],
                expectedResults ??
                [
                    "canonical-hash-stable",
                    "desktop-layout",
                    "html-book-package",
                    "import-success",
                    "inspection-success",
                    "mobile-layout",
                    "roundtrip-stable",
                    "valid-flow-document",
                ]);
        }

        public EpubCorpusPublication AddLargeFixture(string id, int paragraphCount)
        {
            var chapter = new StringBuilder();
            chapter.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?><html xmlns=\"http://www.w3.org/1999/xhtml\" lang=\"pt-BR\"><head><title>Fixture grande</title></head><body><h1 id=\"start\">Fixture grande</h1>");
            for (var index = 0; index < paragraphCount; index++)
            {
                chapter.Append($"<p id=\"p-{index}\">Parágrafo sintético {index} mantido pelo projeto para regressão operacional.</p>");
            }

            chapter.Append("<figure id=\"diagram\"><img src=\"../images/flow.png\" alt=\"Imagem de teste\"/><figcaption>Figura sintética</figcaption></figure></body></html>");
            const string package = """
                <?xml version="1.0" encoding="utf-8"?>
                <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="book-id">
                  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                    <dc:identifier id="book-id">urn:flow:test:larger-owned</dc:identifier>
                    <dc:title>Fixture sintética grande</dc:title>
                    <dc:language>pt-BR</dc:language>
                  </metadata>
                  <manifest>
                    <item id="chapter" href="text/chapter-1.xhtml" media-type="application/xhtml+xml" />
                    <item id="image" href="images/flow.png" media-type="image/png" />
                  </manifest>
                  <spine><itemref idref="chapter" /></spine>
                </package>
                """;
            using var stream = MinimalEpubFactory.Create(
                package: package,
                chapterOne: chapter.ToString(),
                includeSecondChapter: false);
            return AddBytes(id, stream.ToArray(), ["figure", "spine"],
            [
                "canonical-hash-stable",
                "desktop-layout",
                "html-book-package",
                "import-success",
                "inspection-success",
                "mobile-layout",
                "roundtrip-stable",
                "valid-flow-document",
            ]);
        }

        public EpubCorpusPublication AddBytes(
            string id,
            byte[] bytes,
            IEnumerable<string>? expectedFeatures = null,
            IEnumerable<string>? expectedResults = null)
        {
            var relativePath = $"fixtures/{id}.epub";
            var path = Path.Combine(RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
            return CreatePublication(
                id,
                bytes,
                relativePath,
                EpubCorpusPublicationKind.ProjectFixture,
                EpubCorpusRedistribution.Allowed,
                expectedFeatures ?? [],
                expectedResults ?? []);
        }

        public EpubCorpusPublication CreatePublication(
            string id,
            byte[] bytes,
            string relativePath,
            EpubCorpusPublicationKind kind,
            EpubCorpusRedistribution redistribution,
            IEnumerable<string> expectedFeatures,
            IEnumerable<string> expectedResults) => new(
            new EpubCorpusPublicationId(id),
            "Corpus publication",
            "project:test",
            new EpubCorpusLicense("Test fixture", "Generated by the test suite"),
            kind,
            redistribution,
            relativePath,
            EpubVersionFamily.Epub3,
            bytes.LongLength,
            ["en"],
            ["png", "xhtml"],
            expectedFeatures,
            expectedResults,
            [],
            new EpubCorpusSha256(Convert.ToHexString(SHA256.HashData(bytes))));

        public EpubCorpusManifest Manifest(params EpubCorpusPublication[] publications) =>
            new(EpubCorpusManifest.CurrentFormat, publications);

        public EpubCorpusDiscoveryOptions Options() => new(RepositoryRoot);

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
