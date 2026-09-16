using System.Security.Cryptography;
using Flow.Core;
using Flow.Documents;
using Flow.Epub.Corpus;
using Flow.Layout;
using Flow.Rendering.Html;
using Flow.Security;

namespace Flow.Epub.Tests;

public sealed class EpubLargePublicationGateExecutorTests
{
    [Fact]
    public async Task LongProjectFixture_CompletesTwiceWithStableEvidenceAndSequentialPackages()
    {
        using var workspace = new TemporaryWorkspace();
        var bytes = EpubCorpusFixtureFactory.CreateLargeGateFixture();
        var candidate = workspace.AddCandidate("large-fixture", bytes);
        var options = Options(candidate.Id, bytes, requireHumanReview: false);
        var gate = CreateGate(workspace);

        var report = await gate.ExecuteAsync(candidate, options);

        Assert.Contains(
            report.Result.Status,
            new[] { EpubLargePublicationGateStatus.Passed, EpubLargePublicationGateStatus.PassedWithWarnings });
        Assert.Equal(24, report.Result.Evidence.SpineItemCount);
        Assert.Equal(24, report.Result.Evidence.ChapterCount);
        Assert.True(report.Result.Evidence.ImportedNodeCount >= 72);
        Assert.NotNull(report.Result.Evidence.CanonicalHash);
        Assert.Equal(
            EpubLargePublicationGateStatus.Passed,
            Phase(report, EpubLargePublicationGatePhaseKind.DeterminismComparison));
        Assert.Equal(
            EpubLargePublicationGateStatus.Passed,
            Phase(report, EpubLargePublicationGatePhaseKind.MobileHtmlPackage));
        Assert.Equal(
            EpubLargePublicationGateStatus.Passed,
            Phase(report, EpubLargePublicationGatePhaseKind.DesktopHtmlPackage));
        Assert.Contains(
            report.Result.EnvironmentObservations!.Phases,
            static item => item.Phase == EpubLargePublicationGatePhaseKind.Import && item.DurationTicks >= 0);
        Assert.Empty(Directory.EnumerateFileSystemEntries(workspace.GateRoot));
    }

    [Fact]
    public async Task DefaultHumanReviewRequirement_KeepsOtherwiseSuccessfulGateInconclusive()
    {
        using var workspace = new TemporaryWorkspace();
        var bytes = EpubCorpusFixtureFactory.CreateLargeGateFixture(20);
        var candidate = workspace.AddCandidate("review-fixture", bytes);
        var gate = CreateGate(workspace);

        var report = await gate.ExecuteAsync(candidate, Options(candidate.Id, bytes, requireHumanReview: true));

        Assert.Equal(EpubLargePublicationGateStatus.Inconclusive, report.Result.Status);
        Assert.Equal(
            EpubLargePublicationGateStatus.NotStarted,
            Phase(report, EpubLargePublicationGatePhaseKind.HumanReview));
        Assert.All(
            report.Result.HumanReviewItems,
            static item => Assert.Equal(EpubLargePublicationGateStatus.NotStarted, item.Status));
    }

    [Fact]
    public async Task WrongHash_FailsBeforeInspectionAndRemovesWorkspace()
    {
        using var workspace = new TemporaryWorkspace();
        var bytes = EpubCorpusFixtureFactory.CreateLargeGateFixture(20);
        var candidate = workspace.AddCandidate("wrong-hash", bytes);
        var options = new EpubLargePublicationGateOptions(
            candidate.Id,
            new EpubCorpusSha256(new string('F', 64)),
            requireHumanReview: false);
        var gate = CreateGate(workspace);

        var report = await gate.ExecuteAsync(candidate, options);

        Assert.Equal(EpubLargePublicationGateStatus.Failed, report.Result.Status);
        Assert.Equal(EpubLargePublicationGateStatus.Failed, Phase(report, EpubLargePublicationGatePhaseKind.Preflight));
        Assert.Equal(EpubLargePublicationGateStatus.NotStarted, Phase(report, EpubLargePublicationGatePhaseKind.Inspection));
        Assert.Contains(
            report.Result.Diagnostics,
            static item => item.Code == EpubLargePublicationGateDiagnosticCodes.SourceHashMismatch);
        Assert.Empty(Directory.EnumerateFileSystemEntries(workspace.GateRoot));
    }

    [Fact]
    public async Task InjectedSerializationFailure_IsLocalizedAndDependentRenderingIsSkipped()
    {
        using var workspace = new TemporaryWorkspace();
        var bytes = EpubCorpusFixtureFactory.CreateLargeGateFixture(20);
        var candidate = workspace.AddCandidate("serializer-failure", bytes);
        var gate = CreateGate(workspace, serializer: new ThrowingSerializer());

        var report = await gate.ExecuteAsync(candidate, Options(candidate.Id, bytes, requireHumanReview: false));

        Assert.Equal(EpubLargePublicationGateStatus.Failed, report.Result.Status);
        Assert.Equal(EpubLargePublicationGateStatus.Failed, Phase(report, EpubLargePublicationGatePhaseKind.Serialization));
        Assert.Equal(EpubLargePublicationGateStatus.Skipped, Phase(report, EpubLargePublicationGatePhaseKind.Integrity));
        Assert.Equal(EpubLargePublicationGateStatus.Skipped, Phase(report, EpubLargePublicationGatePhaseKind.MobileHtmlPackage));
        Assert.Contains(
            report.Result.Diagnostics,
            static item => item.Code == EpubLargePublicationGateDiagnosticCodes.SerializationFailed
                && item.Phase == EpubLargePublicationGatePhaseKind.Serialization);
        Assert.Empty(Directory.EnumerateFileSystemEntries(workspace.GateRoot));
    }

    [Fact]
    public async Task InjectedRendererFailure_IsLocalizedToEachPackagePhase()
    {
        using var workspace = new TemporaryWorkspace();
        var bytes = EpubCorpusFixtureFactory.CreateLargeGateFixture(20);
        var candidate = workspace.AddCandidate("renderer-failure", bytes);
        var gate = CreateGate(workspace, renderer: new ThrowingRenderer());

        var report = await gate.ExecuteAsync(candidate, Options(candidate.Id, bytes, requireHumanReview: false));

        Assert.Equal(EpubLargePublicationGateStatus.Passed, Phase(report, EpubLargePublicationGatePhaseKind.MobileLayout));
        Assert.Equal(EpubLargePublicationGateStatus.Failed, Phase(report, EpubLargePublicationGatePhaseKind.MobileHtmlPackage));
        Assert.Equal(EpubLargePublicationGateStatus.Passed, Phase(report, EpubLargePublicationGatePhaseKind.DesktopLayout));
        Assert.Equal(EpubLargePublicationGateStatus.Failed, Phase(report, EpubLargePublicationGatePhaseKind.DesktopHtmlPackage));
        Assert.Empty(Directory.EnumerateFileSystemEntries(workspace.GateRoot));
    }

    [Fact]
    public async Task DifferentSemanticEvidenceBetweenRuns_FailsDeterminismComparison()
    {
        using var workspace = new TemporaryWorkspace();
        var bytes = EpubCorpusFixtureFactory.CreateLargeGateFixture(20);
        var candidate = workspace.AddCandidate("unstable-import", bytes);
        var gate = CreateGate(workspace, importer: new AlternatingImporter());

        var report = await gate.ExecuteAsync(candidate, Options(candidate.Id, bytes, requireHumanReview: false));

        Assert.Equal(EpubLargePublicationGateStatus.Failed, report.Result.Status);
        Assert.Equal(
            EpubLargePublicationGateStatus.Failed,
            Phase(report, EpubLargePublicationGatePhaseKind.DeterminismComparison));
        Assert.Contains(
            report.Result.Diagnostics,
            static item => item.Code == EpubLargePublicationGateDiagnosticCodes.NonDeterministicResult);
        Assert.Empty(Directory.EnumerateFileSystemEntries(workspace.GateRoot));
    }

    [Theory]
    [InlineData("inspection", EpubLargePublicationGatePhaseKind.Inspection)]
    [InlineData("import", EpubLargePublicationGatePhaseKind.Import)]
    [InlineData("fidelity", EpubLargePublicationGatePhaseKind.Fidelity)]
    [InlineData("validation", EpubLargePublicationGatePhaseKind.Validation)]
    [InlineData("integrity", EpubLargePublicationGatePhaseKind.Integrity)]
    [InlineData("layout", EpubLargePublicationGatePhaseKind.MobileLayout)]
    public async Task InjectedPhaseFailure_IsReportedAtItsOrigin(
        string injectedFailure,
        EpubLargePublicationGatePhaseKind expectedPhase)
    {
        using var workspace = new TemporaryWorkspace();
        var bytes = EpubCorpusFixtureFactory.CreateLargeGateFixture(20);
        var candidate = workspace.AddCandidate($"failure-{injectedFailure}", bytes);
        var gate = injectedFailure switch
        {
            "inspection" => CreateGate(workspace, inspector: new ThrowingInspector()),
            "import" => CreateGate(workspace, importer: new ThrowingImporter()),
            "fidelity" => CreateGate(workspace, fidelityAnalyzer: new ThrowingFidelityAnalyzer()),
            "validation" => CreateGate(workspace, importer: new InvalidDocumentImporter()),
            "integrity" => CreateGate(workspace, integrityService: new DivergingIntegrityService()),
            "layout" => CreateGate(workspace, layoutEngine: new ThrowingLayoutEngine()),
            _ => throw new ArgumentOutOfRangeException(nameof(injectedFailure)),
        };

        var report = await gate.ExecuteAsync(candidate, Options(candidate.Id, bytes, requireHumanReview: false));

        Assert.Equal(EpubLargePublicationGateStatus.Failed, report.Result.Status);
        Assert.Equal(EpubLargePublicationGateStatus.Failed, Phase(report, expectedPhase));
        Assert.Contains(
            report.Result.Diagnostics,
            item => item.Phase == expectedPhase && item.Severity == EpubLargePublicationGateDiagnosticSeverity.Error);
        Assert.Empty(Directory.EnumerateFileSystemEntries(workspace.GateRoot));
    }

    [Fact]
    public async Task CancellationDuringInspection_PropagatesAndRemovesEveryTemporaryFile()
    {
        using var workspace = new TemporaryWorkspace();
        var bytes = EpubCorpusFixtureFactory.CreateLargeGateFixture(20);
        var candidate = workspace.AddCandidate("cancelled", bytes);
        using var cancellation = new CancellationTokenSource();
        var gate = CreateGate(workspace, inspector: new CancellingInspector(cancellation));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            gate.ExecuteAsync(candidate, Options(candidate.Id, bytes, requireHumanReview: false), cancellation.Token));

        Assert.Empty(Directory.EnumerateFileSystemEntries(workspace.GateRoot));
    }

    private static EpubLargePublicationGate CreateGate(
        TemporaryWorkspace workspace,
        IEpubPublicationInspector? inspector = null,
        IEpubImporter? importer = null,
        IEpubFidelityAnalyzer? fidelityAnalyzer = null,
        IFlowDocumentSerializer? serializer = null,
        IDocumentIntegrityService? integrityService = null,
        ILayoutEngine? layoutEngine = null,
        IHtmlBookPackageRenderer? renderer = null) => new(
        inspector ?? new EpubPublicationInspector(),
        importer ?? new EpubImporter(),
        new DocumentValidator(),
        fidelityAnalyzer ?? new EpubFidelityAnalyzer(),
        serializer ?? new FlowJsonDocumentSerializer(),
        integrityService ?? new Sha256DocumentIntegrityService(new FlowDocumentCanonicalizer()),
        layoutEngine ?? new AdaptiveLayoutEngine(),
        renderer ?? new HtmlBookPackageRenderer(),
        temporaryDirectoryRoot: workspace.GateRoot);

    private static EpubLargePublicationGateOptions Options(
        EpubCorpusPublicationId id,
        byte[] bytes,
        bool requireHumanReview) => new(
        id,
        new EpubCorpusSha256(Convert.ToHexString(SHA256.HashData(bytes))),
        repetitionCount: 2,
        requireHumanReview);

    private static EpubLargePublicationGateStatus Phase(
        EpubLargePublicationGateReport report,
        EpubLargePublicationGatePhaseKind phase) =>
        report.Result.Phases.Single(item => item.Kind == phase).Status;

    private sealed class ThrowingSerializer : IFlowDocumentSerializer
    {
        public Task SerializeAsync(FlowDocument document, Stream destination, CancellationToken cancellationToken = default) =>
            throw new InvalidDataException("Injected serialization failure.");

        public Task<FlowDocument> DeserializeAsync(Stream source, CancellationToken cancellationToken = default) =>
            throw new InvalidDataException("Injected deserialization failure.");
    }

    private sealed class ThrowingRenderer : IHtmlBookPackageRenderer
    {
        public HtmlBookPackage Render(
            FlowDocument document,
            LayoutDocument layout,
            UserReadingPreferences userPreferences,
            HtmlBookIntegrity integrity) => throw new InvalidDataException("Injected renderer failure.");
    }

    private sealed class CancellingInspector(CancellationTokenSource cancellation) : IEpubPublicationInspector
    {
        public Task<EpubPublicationInspection> InspectAsync(Stream source, CancellationToken cancellationToken = default)
        {
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Cancellation was expected.");
        }
    }

    private sealed class ThrowingInspector : IEpubPublicationInspector
    {
        public Task<EpubPublicationInspection> InspectAsync(Stream source, CancellationToken cancellationToken = default) =>
            throw new InvalidDataException("Injected inspection failure.");
    }

    private sealed class ThrowingImporter : IEpubImporter
    {
        public Task<EpubImportResult> ImportAsync(Stream source, CancellationToken cancellationToken = default) =>
            throw new InvalidDataException("Injected import failure.");
    }

    private sealed class ThrowingFidelityAnalyzer : IEpubFidelityAnalyzer
    {
        public EpubFidelityReport Analyze(EpubImportResult importResult) =>
            throw new InvalidDataException("Injected fidelity failure.");
    }

    private sealed class InvalidDocumentImporter : IEpubImporter
    {
        private readonly EpubImporter inner = new();

        public async Task<EpubImportResult> ImportAsync(Stream source, CancellationToken cancellationToken = default)
        {
            var imported = await inner.ImportAsync(source, cancellationToken);
            var original = Assert.IsType<FlowDocument>(imported.Document);
            var invalid = new FlowDocument(
                original.Identity,
                original.Metadata,
                new DocumentContent(original.Content.Children.Append(
                    new Figure(new NodeId("invalid-figure"), new AssetId("missing-asset")))),
                original.Assets.Values,
                original.Presentation,
                original.Integrity);
            return new EpubImportResult(
                invalid,
                imported.Diagnostics,
                imported.MetadataReport,
                imported.ProcessingReport,
                imported.SourceMap);
        }
    }

    private sealed class AlternatingImporter : IEpubImporter
    {
        private readonly EpubImporter inner = new();
        private int calls;

        public async Task<EpubImportResult> ImportAsync(Stream source, CancellationToken cancellationToken = default)
        {
            var imported = await inner.ImportAsync(source, cancellationToken);
            if (++calls % 2 != 0)
            {
                return imported;
            }

            var original = Assert.IsType<FlowDocument>(imported.Document);
            var changed = new FlowDocument(
                original.Identity,
                original.Metadata,
                new DocumentContent(original.Content.Children.Append(new Chapter(
                    new NodeId("unstable-extra-chapter"),
                    [new Paragraph(new NodeId("unstable-extra-paragraph"), [new Text("extra")])]))),
                original.Assets.Values,
                original.Presentation,
                original.Integrity);
            return new EpubImportResult(
                changed,
                imported.Diagnostics,
                imported.MetadataReport,
                imported.ProcessingReport,
                imported.SourceMap);
        }
    }

    private sealed class DivergingIntegrityService : IDocumentIntegrityService
    {
        private int calls;

        public DocumentHash ComputeHash(FlowDocument document) => new(
            "SHA-256",
            (++calls % 2 == 0 ? new string('B', 64) : new string('A', 64)),
            "flow-c14n-0.1");
    }

    private sealed class ThrowingLayoutEngine : ILayoutEngine
    {
        public LayoutDocument Layout(FlowDocument document, LayoutContext context) =>
            throw new InvalidDataException("Injected layout failure.");
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        public TemporaryWorkspace()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"flow-large-gate-tests-{Guid.NewGuid():N}");
            GateRoot = System.IO.Path.Combine(Path, "gate");
            Directory.CreateDirectory(GateRoot);
        }

        public string Path { get; }

        public string GateRoot { get; }

        public EpubLargePublicationCandidate AddCandidate(string id, byte[] bytes)
        {
            var path = System.IO.Path.Combine(Path, $"{id}.epub");
            File.WriteAllBytes(path, bytes);
            return new EpubLargePublicationCandidate(new EpubCorpusPublicationId(id), path, true, true);
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
