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
        Assert.Contains("\"included\": false", text, StringComparison.Ordinal);
        Assert.DoesNotContain(workspace.Root, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain('\r', text);
        Assert.Equal((byte)'\n', firstBytes[^1]);
        Assert.False(firstBytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));

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
        IEpubCheckAdapter? epubCheckAdapter = null) => new(
        discoveryService ?? new EpubCorpusDiscoveryService(),
        new EpubPublicationInspector(),
        importer ?? new EpubImporter(),
        new DocumentValidator(),
        new EpubFidelityAnalyzer(),
        serializer ?? new FlowJsonDocumentSerializer(),
        new Sha256DocumentIntegrityService(new FlowDocumentCanonicalizer()),
        new AdaptiveLayoutEngine(),
        new HtmlBookPackageRenderer(),
        epubCheckAdapter);

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
