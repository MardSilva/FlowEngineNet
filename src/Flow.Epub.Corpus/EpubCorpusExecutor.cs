using System.Collections.Immutable;
using System.Diagnostics;
using Flow.Core;
using Flow.Documents;
using Flow.Layout;
using Flow.Rendering.Html;
using Flow.Security;

namespace Flow.Epub.Corpus;

/// <summary>Runs cataloged EPUBs through the complete offline Flow pipeline without sharing failure state.</summary>
public sealed class EpubCorpusExecutor : IEpubCorpusExecutor
{
    private static readonly UserReadingPreferences DefaultReadingPreferences = new();
    private readonly IEpubCorpusDiscoveryService discoveryService;
    private readonly IEpubPublicationInspector inspector;
    private readonly IEpubImporter importer;
    private readonly DocumentValidator validator;
    private readonly IEpubFidelityAnalyzer fidelityAnalyzer;
    private readonly IFlowDocumentSerializer serializer;
    private readonly IDocumentIntegrityService integrityService;
    private readonly ILayoutEngine layoutEngine;
    private readonly IHtmlBookPackageRenderer htmlRenderer;
    private readonly IEpubCheckAdapter epubCheckAdapter;
    private readonly string temporaryDirectoryRoot;

    public EpubCorpusExecutor()
        : this(
            new EpubCorpusDiscoveryService(),
            new EpubPublicationInspector(),
            new EpubImporter(),
            new DocumentValidator(),
            new EpubFidelityAnalyzer(),
            new FlowJsonDocumentSerializer(),
            new Sha256DocumentIntegrityService(new FlowDocumentCanonicalizer()),
            new AdaptiveLayoutEngine(),
            new HtmlBookPackageRenderer(),
            new EpubCheckProcessAdapter())
    {
    }

    public EpubCorpusExecutor(
        IEpubCorpusDiscoveryService discoveryService,
        IEpubPublicationInspector inspector,
        IEpubImporter importer,
        DocumentValidator validator,
        IEpubFidelityAnalyzer fidelityAnalyzer,
        IFlowDocumentSerializer serializer,
        IDocumentIntegrityService integrityService,
        ILayoutEngine layoutEngine,
        IHtmlBookPackageRenderer htmlRenderer,
        IEpubCheckAdapter? epubCheckAdapter = null,
        string? temporaryDirectoryRoot = null)
    {
        ArgumentNullException.ThrowIfNull(discoveryService);
        ArgumentNullException.ThrowIfNull(inspector);
        ArgumentNullException.ThrowIfNull(importer);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(fidelityAnalyzer);
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(integrityService);
        ArgumentNullException.ThrowIfNull(layoutEngine);
        ArgumentNullException.ThrowIfNull(htmlRenderer);
        this.discoveryService = discoveryService;
        this.inspector = inspector;
        this.importer = importer;
        this.validator = validator;
        this.fidelityAnalyzer = fidelityAnalyzer;
        this.serializer = serializer;
        this.integrityService = integrityService;
        this.layoutEngine = layoutEngine;
        this.htmlRenderer = htmlRenderer;
        this.epubCheckAdapter = epubCheckAdapter ?? new EpubCheckProcessAdapter();
        this.temporaryDirectoryRoot = Path.GetFullPath(temporaryDirectoryRoot ?? Path.GetTempPath());
    }

    public async Task<EpubCorpusExecutionReport> ExecuteAsync(
        EpubCorpusManifest manifest,
        EpubCorpusDiscoveryOptions discoveryOptions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(discoveryOptions);
        cancellationToken.ThrowIfCancellationRequested();

        await using var discovery = await discoveryService
            .DiscoverAsync(manifest, discoveryOptions, cancellationToken)
            .ConfigureAwait(false);
        var discoveryById = discovery.Report.Items.ToDictionary(static item => item.Id);
        var results = new List<EpubCorpusPublicationExecutionResult>(manifest.Publications.Length);

        foreach (var publication in manifest.Publications)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var discoveryItem = discoveryById[publication.Id];
            if (discoveryItem.Status != EpubCorpusDiscoveryStatus.Available)
            {
                results.Add(CreateUnavailableResult(discoveryItem));
                continue;
            }

            try
            {
                results.Add(await ExecutePublicationAsync(publication, discovery, cancellationToken).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                results.Add(CreateUnexpectedFailure(publication.Id, exception));
            }
        }

        return new EpubCorpusExecutionReport(results);
    }

    private async Task<EpubCorpusPublicationExecutionResult> ExecutePublicationAsync(
        EpubCorpusPublication publication,
        EpubCorpusDiscoverySession discovery,
        CancellationToken cancellationToken)
    {
        var state = new ExecutionState(publication.Id);
        using var environment = new EnvironmentMetricsCollector();
        using var workspace = PublicationWorkspace.Create(temporaryDirectoryRoot, publication.Id);
        state.Complete(EpubCorpusExecutionPhase.Discovery);
        environment.Sample();

        EpubPublicationInspection? inspection = null;
        try
        {
            await using var source = discovery.OpenRead(publication.Id);
            inspection = await inspector.InspectAsync(source, cancellationToken).ConfigureAwait(false);
            state.Inspection = inspection;
            state.Complete(EpubCorpusExecutionPhase.Inspection);
            AddEpubDiagnostics(state, EpubCorpusExecutionPhase.Inspection, inspection.Diagnostics);
            if (!inspection.IsSuccess)
            {
                state.Fail(EpubCorpusExecutionDiagnosticCodes.InspectionFailed, EpubCorpusExecutionPhase.Inspection,
                    "Structural inspection did not produce a valid EPUB package.");
            }

            if (inspection.Package?.VersionFamily != publication.ExpectedEpubVersion)
            {
                state.Fail(EpubCorpusExecutionDiagnosticCodes.UnexpectedEpubVersion, EpubCorpusExecutionPhase.Inspection,
                    $"Expected {publication.ExpectedEpubVersion} but inspection identified {inspection.Package?.VersionFamily ?? EpubVersionFamily.Unknown}.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            state.Fail(EpubCorpusExecutionDiagnosticCodes.InspectionFailed, EpubCorpusExecutionPhase.Inspection,
                "Structural inspection failed before it could produce a complete result.");
        }
        environment.Sample();

        EpubImportResult? importResult = null;
        FlowDocument? document = null;
        try
        {
            await using var source = discovery.OpenRead(publication.Id);
            importResult = await importer.ImportAsync(source, cancellationToken).ConfigureAwait(false);
            state.ImportMetrics = importResult.Metrics;
            state.SourceMap = importResult.SourceMap;
            document = importResult.Document;
            state.Document = document;
            state.Complete(EpubCorpusExecutionPhase.Import);
            AddEpubDiagnostics(state, EpubCorpusExecutionPhase.Import, importResult.Diagnostics);
            if (!importResult.IsSuccess)
            {
                state.Fail(EpubCorpusExecutionDiagnosticCodes.ImportFailed, EpubCorpusExecutionPhase.Import,
                    document is null
                        ? "EPUB import did not produce a Flow document."
                        : "EPUB import produced a partial document with error diagnostics.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            state.Fail(EpubCorpusExecutionDiagnosticCodes.ImportFailed, EpubCorpusExecutionPhase.Import,
                "EPUB import failed before it could produce a complete result.");
        }
        environment.Sample();

        if (document is null)
        {
            VerifyExpectations(state, publication, inspection, null, null, null, cancellationToken);
            await AddExternalEvidenceAsync(state, publication, discovery, cancellationToken).ConfigureAwait(false);
            environment.Sample();
            state.Environment = environment.Snapshot(state.ImportMetrics);
            return state.Build();
        }

        var validation = validator.Validate(document);
        state.ValidationDiagnosticCount = validation.Diagnostics.Length;
        state.Complete(EpubCorpusExecutionPhase.Validation);
        foreach (var diagnostic in validation.Diagnostics)
        {
            state.Add(new EpubCorpusExecutionDiagnostic(
                EpubCorpusExecutionDiagnosticCodes.ValidationFailed,
                diagnostic.Severity == ValidationSeverity.Error
                    ? EpubCorpusExecutionDiagnosticSeverity.Error
                    : EpubCorpusExecutionDiagnosticSeverity.Warning,
                EpubCorpusExecutionPhase.Validation,
                diagnostic.Message,
                diagnostic.Code,
                diagnostic.NodeId?.Value));
        }

        if (!validation.IsValid)
        {
            state.HasFailure = true;
        }
        environment.Sample();

        EpubFidelityReport? fidelity = null;
        if (importResult is not null)
        {
            try
            {
                fidelity = fidelityAnalyzer.Analyze(importResult, cancellationToken);
                state.Fidelity = fidelity;
                state.Complete(EpubCorpusExecutionPhase.Fidelity);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                state.Fail(EpubCorpusExecutionDiagnosticCodes.FidelityFailed, EpubCorpusExecutionPhase.Fidelity,
                    "Fidelity analysis failed before it could produce a complete report.");
            }
        }
        environment.Sample();

        try
        {
            var roundTrip = await RoundTripAsync(document, workspace.RoundTripPath, cancellationToken).ConfigureAwait(false);
            state.FlowJsonBytes = roundTrip.FlowJsonBytes;
            state.Complete(EpubCorpusExecutionPhase.Serialization);
            if (!roundTrip.IdentityPreserved)
            {
                state.Fail(EpubCorpusExecutionDiagnosticCodes.IdentityMismatch, EpubCorpusExecutionPhase.Integrity,
                    "Document identity changed during the Flow JSON round trip.");
            }

            state.CanonicalHash = roundTrip.OriginalHash.Hash;
            state.Complete(EpubCorpusExecutionPhase.Integrity);
            if (roundTrip.OriginalHash != roundTrip.RestoredHash)
            {
                state.Fail(EpubCorpusExecutionDiagnosticCodes.CanonicalHashMismatch, EpubCorpusExecutionPhase.Integrity,
                    "Canonical document hash changed during the Flow JSON round trip.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            state.Fail(EpubCorpusExecutionDiagnosticCodes.RoundTripFailed, EpubCorpusExecutionPhase.Serialization,
                "The Flow JSON round trip failed.");
        }
        environment.Sample();

        if (validation.IsValid && state.CanonicalHash is not null)
        {
            var hash = integrityService.ComputeHash(document);
            var integrity = new HtmlBookIntegrity(hash.Algorithm, hash.Hash, hash.CanonicalizationVersion);
            TryProducePackage(state, publication, document, new LayoutContext(390, 844, DeviceClass.Phone),
                EpubCorpusExecutionPhase.MobileLayout, integrity, cancellationToken);
            environment.Sample();
            TryProducePackage(state, publication, document, new LayoutContext(1600, 1000, DeviceClass.Desktop),
                EpubCorpusExecutionPhase.DesktopLayout, integrity, cancellationToken);
            environment.Sample();
            if (state.HtmlPackageCount == 2)
            {
                state.Complete(EpubCorpusExecutionPhase.HtmlBookPackage);
            }
        }

        VerifyExpectations(state, publication, inspection, document, validation, state.HtmlPackageCount, cancellationToken);
        await AddExternalEvidenceAsync(state, publication, discovery, cancellationToken).ConfigureAwait(false);
        environment.Sample();
        state.Environment = environment.Snapshot(state.ImportMetrics);
        return state.Build();
    }

    private async Task<RoundTripEvidence> RoundTripAsync(
        FlowDocument document,
        string temporaryPath,
        CancellationToken cancellationToken)
    {
        try
        {
            await using (var output = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             64 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await serializer.SerializeAsync(document, output, cancellationToken).ConfigureAwait(false);
            }

            var length = checked((int)new FileInfo(temporaryPath).Length);
            FlowDocument restored;
            await using (var input = new FileStream(
                             temporaryPath,
                             FileMode.Open,
                             FileAccess.Read,
                             FileShare.Read,
                             64 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                restored = await serializer.DeserializeAsync(input, cancellationToken).ConfigureAwait(false);
            }

            return new RoundTripEvidence(
                length,
                document.Identity == restored.Identity,
                integrityService.ComputeHash(document),
                integrityService.ComputeHash(restored));
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private async Task AddExternalEvidenceAsync(
        ExecutionState state,
        EpubCorpusPublication publication,
        EpubCorpusDiscoverySession discovery,
        CancellationToken cancellationToken)
    {
        await using var source = discovery.OpenRead(publication.Id);
        var evidence = await epubCheckAdapter
            .EvaluateAsync(source, publication.Id.Value, cancellationToken)
            .ConfigureAwait(false);
        state.EpubCheckEvidence = evidence;
        foreach (var diagnostic in evidence.Diagnostics)
        {
            state.Add(diagnostic);
        }

        state.Complete(EpubCorpusExecutionPhase.ExternalConformance);
    }

    private void TryProducePackage(
        ExecutionState state,
        EpubCorpusPublication publication,
        FlowDocument document,
        LayoutContext context,
        EpubCorpusExecutionPhase phase,
        HtmlBookIntegrity integrity,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var layout = layoutEngine.Layout(document, context);
            EnsureLayoutPreservesIds(document, layout);
            state.SetLayoutNodeCount(phase, EnumerateLayoutNodes(layout.Nodes).Count());
            state.Complete(phase);
            var package = htmlRenderer.Render(
                document,
                layout,
                DefaultReadingPreferences,
                integrity,
                new HtmlBookPackageOptions(),
                cancellationToken);
            var evidence = HtmlBookPackageVerifier.Verify(package, cancellationToken);
            state.RecordPackage(evidence, publication.ExpectedFeatures);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (!state.Completed.Contains(phase))
            {
                state.Fail(EpubCorpusExecutionDiagnosticCodes.LayoutFailed, phase,
                    phase == EpubCorpusExecutionPhase.MobileLayout
                        ? "Mobile layout generation failed."
                        : "Desktop layout generation failed.");
            }
            else
            {
                state.Fail(EpubCorpusExecutionDiagnosticCodes.HtmlPackageFailed, EpubCorpusExecutionPhase.HtmlBookPackage,
                    "HTML book package generation or internal reference verification failed.");
            }
        }
    }

    private static void VerifyExpectations(
        ExecutionState state,
        EpubCorpusPublication publication,
        EpubPublicationInspection? inspection,
        FlowDocument? document,
        ValidationResult? validation,
        int? htmlPackageCount,
        CancellationToken cancellationToken)
    {
        foreach (var resource in publication.ExpectedResources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var predicate = ResourcePredicate(resource);
            if (predicate is null)
            {
                state.Inconclusive(resource, "resource class");
            }
            else if (inspection is null || !inspection.Manifest.Any(predicate))
            {
                state.ExpectationFailed(resource, "Expected resource class was not found by structural inspection.");
            }
        }

        foreach (var feature in publication.ExpectedFeatures)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool? satisfied = feature switch
            {
                "spine" => inspection is not null && inspection.Spine.Length > 0,
                "table-of-contents" or "toc" => HasNode<TableOfContents>(document),
                "figure" or "image" => HasNode<Figure>(document),
                "internal-link" => HasInternalLink(document),
                "ordered-list" => HasNode<OrderedList>(document),
                "unordered-list" => HasNode<UnorderedList>(document),
                "footnote" or "note" => HasFootnote(document),
                "table" => HasNode<Table>(document),
                "cover" => document?.Presentation?.Cover is not null,
                _ => null,
            };

            if (satisfied is null)
            {
                state.Inconclusive(feature, "feature");
            }
            else if (!satisfied.Value)
            {
                state.ExpectationFailed(feature, "Expected semantic feature was not preserved.");
            }
            else if (htmlPackageCount > 0 && !state.PackageHasFeatureInEveryVerifiedPackage(feature))
            {
                state.ExpectationFailed(feature, "Expected feature was not found in every generated HTML book package.");
            }
        }

        foreach (var result in publication.ExpectedResults)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool? satisfied = result switch
            {
                "inspection-success" => inspection?.IsSuccess == true,
                "import-success" => document is not null && !state.Diagnostics.Any(static item =>
                    item.Phase == EpubCorpusExecutionPhase.Import && item.Severity == EpubCorpusExecutionDiagnosticSeverity.Error),
                "valid-flow-document" => validation?.IsValid == true,
                "roundtrip-stable" => state.Completed.Contains(EpubCorpusExecutionPhase.Serialization),
                "canonical-hash-stable" => state.Completed.Contains(EpubCorpusExecutionPhase.Integrity)
                    && !state.Diagnostics.Any(static item => item.Code == EpubCorpusExecutionDiagnosticCodes.CanonicalHashMismatch),
                "mobile-layout" => state.MobileLayoutNodeCount > 0,
                "desktop-layout" => state.DesktopLayoutNodeCount > 0,
                "html-book-package" => htmlPackageCount == 2,
                _ => null,
            };

            if (satisfied is null)
            {
                state.Inconclusive(result, "result");
            }
            else if (!satisfied.Value)
            {
                state.ExpectationFailed(result, "Declared corpus result was not achieved.");
            }
        }

        state.Complete(EpubCorpusExecutionPhase.Expectations);
    }

    private static Func<EpubManifestItemInfo, bool>? ResourcePredicate(string resource) => resource switch
    {
        "xhtml" => static item => item.MediaType == "application/xhtml+xml",
        "css" => static item => item.MediaType == "text/css",
        "ncx" => static item => item.MediaType == "application/x-dtbncx+xml",
        "jpeg" or "jpg" => static item => item.MediaType == "image/jpeg",
        "png" => static item => item.MediaType == "image/png",
        "gif" => static item => item.MediaType == "image/gif",
        "webp" => static item => item.MediaType == "image/webp",
        "svg" => static item => item.MediaType == "image/svg+xml",
        _ => null,
    };

    private static void EnsureLayoutPreservesIds(FlowDocument document, LayoutDocument layout)
    {
        var semanticIds = document.Index.Locations.Select(static item => item.Node.Id).ToHashSet();
        var layoutIds = EnumerateLayoutNodes(layout.Nodes).Select(static item => item.SemanticId).ToHashSet();
        if (!semanticIds.SetEquals(layoutIds))
        {
            throw new InvalidDataException("Layout did not preserve the complete semantic node ID set.");
        }
    }

    private static IEnumerable<LayoutNode> EnumerateLayoutNodes(IEnumerable<LayoutNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in EnumerateLayoutNodes(node.Children))
            {
                yield return child;
            }
        }
    }

    private static IEnumerable<InlineNode> EnumerateInlineNodes(FlowDocument document)
    {
        foreach (var location in document.Index.Locations)
        {
            foreach (var inline in GetInlineContent(location.Node))
            {
                yield return inline;
                foreach (var child in EnumerateInlineChildren(inline))
                {
                    yield return child;
                }
            }
        }
    }

    private static IEnumerable<InlineNode> GetInlineContent(DocumentNode node) => node switch
    {
        Heading heading => heading.Content,
        Paragraph paragraph => paragraph.Content,
        Caption caption => caption.Content,
        TableOfContents toc => toc.Title.Concat(toc.Entries.SelectMany(static entry => entry.Label)),
        CodeBlock code => [new Text(code.Code)],
        _ => [],
    };

    private static IEnumerable<InlineNode> EnumerateInlineChildren(InlineNode node)
    {
        IEnumerable<InlineNode> children = node switch
        {
            InlineContainerNode container => container.Children,
            FootnoteReference reference => reference.Label,
            _ => [],
        };
        foreach (var child in children)
        {
            yield return child;
            foreach (var descendant in EnumerateInlineChildren(child))
            {
                yield return descendant;
            }
        }
    }

    private static bool HasNode<T>(FlowDocument? document)
        where T : DocumentNode => document?.Index.Locations.Any(static item => item.Node is T) == true;

    private static bool HasInternalLink(FlowDocument? document) => document is not null
        && EnumerateInlineNodes(document).OfType<Link>().Any(static link => DocumentAnchor.TryParse(link.Target, out _));

    private static bool HasFootnote(FlowDocument? document) => document is not null
        && HasNode<Footnote>(document)
        && EnumerateInlineNodes(document).OfType<FootnoteReference>().Any();

    private static void AddEpubDiagnostics(
        ExecutionState state,
        EpubCorpusExecutionPhase phase,
        IEnumerable<EpubDiagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            state.Add(new EpubCorpusExecutionDiagnostic(
                diagnostic.Code,
                diagnostic.Severity switch
                {
                    EpubDiagnosticSeverity.Error => EpubCorpusExecutionDiagnosticSeverity.Error,
                    EpubDiagnosticSeverity.Warning => EpubCorpusExecutionDiagnosticSeverity.Warning,
                    _ => EpubCorpusExecutionDiagnosticSeverity.Information,
                },
                phase,
                diagnostic.Message,
                diagnostic.Code,
                diagnostic.Resource));
        }
    }

    private static EpubCorpusPublicationExecutionResult CreateUnavailableResult(EpubCorpusDiscoveryItem item)
    {
        var skipped = item.Status == EpubCorpusDiscoveryStatus.Missing;
        var diagnostics = item.Diagnostics.Select(diagnostic => new EpubCorpusExecutionDiagnostic(
            diagnostic.Code,
            skipped ? EpubCorpusExecutionDiagnosticSeverity.Warning : EpubCorpusExecutionDiagnosticSeverity.Error,
            EpubCorpusExecutionPhase.Discovery,
            diagnostic.Message,
            diagnostic.Code,
            diagnostic.Resource));
        return new EpubCorpusPublicationExecutionResult(
            item.Id,
            skipped ? EpubCorpusExecutionStatus.Skipped : EpubCorpusExecutionStatus.Failed,
            [EpubCorpusExecutionPhase.Discovery],
            EmptyEvidence(),
            diagnostics);
    }

    private static EpubCorpusPublicationExecutionResult CreateUnexpectedFailure(
        EpubCorpusPublicationId id,
        Exception exception) => new(
        id,
        EpubCorpusExecutionStatus.Failed,
        [],
        EmptyEvidence(),
        [new EpubCorpusExecutionDiagnostic(
            EpubCorpusExecutionDiagnosticCodes.ProcessingFailed,
            EpubCorpusExecutionDiagnosticSeverity.Error,
            EpubCorpusExecutionPhase.Discovery,
            $"Publication processing failed with {exception.GetType().Name}.")]);

    private static EpubCorpusPublicationEvidence EmptyEvidence() => new(
        null, 0, 0, 0, 0, 0, 0, 0, null, null, 0, 0, 0, 0, 0, 0);

    private sealed class ExecutionState(EpubCorpusPublicationId id)
    {
        internal readonly List<EpubCorpusExecutionDiagnostic> Diagnostics = [];
        internal readonly HashSet<EpubCorpusExecutionPhase> Completed = [];
        internal bool HasFailure;
        internal bool HasInconclusive;
        internal EpubPublicationInspection? Inspection;
        internal FlowDocument? Document;
        internal EpubFidelityReport? Fidelity;
        internal EpubImportMetrics? ImportMetrics;
        internal EpubSourceMap? SourceMap;
        internal int MobileLayoutNodeCount;
        internal int DesktopLayoutNodeCount;
        internal int HtmlPackageCount;
        internal int HtmlFileCount;
        internal long HtmlBytes;
        internal readonly Dictionary<string, int> PackageFeatureCounts = new(StringComparer.Ordinal);
        internal int ValidationDiagnosticCount;
        internal int FlowJsonBytes;
        internal string? CanonicalHash;
        internal EpubCheckEvidence? EpubCheckEvidence;
        internal EpubCorpusEnvironmentMetrics? Environment;

        internal void Add(EpubCorpusExecutionDiagnostic diagnostic)
        {
            Diagnostics.Add(diagnostic);
            HasFailure |= diagnostic.Severity == EpubCorpusExecutionDiagnosticSeverity.Error;
        }

        internal void Complete(EpubCorpusExecutionPhase phase) => Completed.Add(phase);

        internal void Fail(string code, EpubCorpusExecutionPhase phase, string message) =>
            Add(new EpubCorpusExecutionDiagnostic(code, EpubCorpusExecutionDiagnosticSeverity.Error, phase, message));

        internal void Inconclusive(string expectation, string kind)
        {
            HasInconclusive = true;
            Add(new EpubCorpusExecutionDiagnostic(
                EpubCorpusExecutionDiagnosticCodes.UnsupportedExpectation,
                EpubCorpusExecutionDiagnosticSeverity.Warning,
                EpubCorpusExecutionPhase.Expectations,
                $"The catalog {kind} expectation '{expectation}' is not recognized.",
                resource: expectation));
        }

        internal void ExpectationFailed(string expectation, string message) => Add(new EpubCorpusExecutionDiagnostic(
            EpubCorpusExecutionDiagnosticCodes.ExpectationFailed,
            EpubCorpusExecutionDiagnosticSeverity.Error,
            EpubCorpusExecutionPhase.Expectations,
            message,
            resource: expectation));

        internal void SetLayoutNodeCount(EpubCorpusExecutionPhase phase, int count)
        {
            if (phase == EpubCorpusExecutionPhase.MobileLayout)
            {
                MobileLayoutNodeCount = count;
            }
            else
            {
                DesktopLayoutNodeCount = count;
            }
        }

        internal void RecordPackage(HtmlBookPackageEvidence evidence, IEnumerable<string> expectedFeatures)
        {
            HtmlPackageCount++;
            HtmlFileCount += evidence.FileCount;
            HtmlBytes += evidence.Bytes;
            foreach (var feature in expectedFeatures.Distinct(StringComparer.Ordinal))
            {
                var elementName = ExpectedHtmlElement(feature);
                if (elementName is not null && evidence.ElementNames.Contains(elementName))
                {
                    PackageFeatureCounts[feature] = PackageFeatureCounts.GetValueOrDefault(feature) + 1;
                }
            }
        }

        internal bool PackageHasFeatureInEveryVerifiedPackage(string feature)
        {
            var elementName = ExpectedHtmlElement(feature);
            return elementName is null
                   || PackageFeatureCounts.GetValueOrDefault(feature) == HtmlPackageCount;
        }

        internal EpubCorpusPublicationExecutionResult Build()
        {
            var evidence = new EpubCorpusPublicationEvidence(
                Inspection?.Package?.VersionFamily.ToString(),
                Inspection?.Manifest.Length ?? 0,
                Inspection?.Spine.Length ?? 0,
                Document?.Index.Locations.Length ?? 0,
                Document?.Assets.Count ?? 0,
                ValidationDiagnosticCount,
                Fidelity?.Summary.SourceUnitCount ?? 0,
                Fidelity?.Summary.LostCount ?? 0,
                Document?.Identity.Id.Value,
                CanonicalHash,
                FlowJsonBytes,
                MobileLayoutNodeCount,
                DesktopLayoutNodeCount,
                HtmlPackageCount,
                HtmlFileCount,
                HtmlBytes,
                CreateSemanticEvidence(Document, SourceMap));
            return new EpubCorpusPublicationExecutionResult(
                id,
                HasFailure
                    ? EpubCorpusExecutionStatus.Failed
                    : HasInconclusive
                        ? EpubCorpusExecutionStatus.Inconclusive
                        : EpubCorpusExecutionStatus.Passed,
                Completed,
                evidence,
                Diagnostics,
                Environment,
                EpubCheckEvidence);
        }

        private static EpubCorpusSemanticEvidence? CreateSemanticEvidence(
            FlowDocument? document,
            EpubSourceMap? sourceMap)
        {
            if (document is null)
            {
                return null;
            }

            var nodes = document.Index.Locations.Select(static location => location.Node).ToArray();
            var inline = EnumerateInlineNodes(document).ToArray();
            return new EpubCorpusSemanticEvidence(
                nodes.Select(static node => node.Id.Value),
                sourceMap?.Locations.Length ?? 0,
                nodes.OfType<Chapter>().Count(),
                nodes.OfType<Heading>().Count(),
                nodes.OfType<Paragraph>().Count(),
                nodes.OfType<TableOfContents>().Sum(static toc => toc.Entries.Length),
                inline.OfType<Link>().Count(static link => DocumentAnchor.TryParse(link.Target, out _)),
                nodes.OfType<Figure>().Count(),
                nodes.OfType<Footnote>().Count(),
                inline.OfType<FootnoteReference>().Count(),
                nodes.OfType<Table>().Count(),
                nodes.Count(static node => node is TableCell or TableHeaderCell),
                nodes.OfType<Chapter>().Select(static chapter => chapter.Id.Value));
        }
    }

    private static string? ExpectedHtmlElement(string feature) => feature switch
    {
        "table-of-contents" or "toc" => "nav",
        "figure" or "image" or "cover" => "img",
        "ordered-list" => "ol",
        "unordered-list" => "ul",
        "footnote" or "note" => "section",
        "table" => "table",
        _ => null,
    };

    private sealed record RoundTripEvidence(
        int FlowJsonBytes,
        bool IdentityPreserved,
        DocumentHash OriginalHash,
        DocumentHash RestoredHash);

    private sealed class EnvironmentMetricsCollector : IDisposable
    {
        private readonly Stopwatch stopwatch = Stopwatch.StartNew();
        private long approximatePeakManagedBytes;
        private long approximatePeakWorkingSetBytes;

        internal void Sample()
        {
            approximatePeakManagedBytes = Math.Max(approximatePeakManagedBytes, GC.GetTotalMemory(false));
            approximatePeakWorkingSetBytes = Math.Max(approximatePeakWorkingSetBytes, Environment.WorkingSet);
        }

        internal EpubCorpusEnvironmentMetrics Snapshot(EpubImportMetrics? importMetrics)
        {
            Sample();
            return new EpubCorpusEnvironmentMetrics(
                stopwatch.Elapsed.Ticks,
                approximatePeakManagedBytes,
                importMetrics?.ArchiveEntryCount ?? 0,
                importMetrics?.CompressedBytes ?? 0,
                importMetrics?.UncompressedBytes ?? 0,
                importMetrics?.AssetBytes ?? 0,
                importMetrics?.SpineDocumentsProcessed ?? 0,
                importMetrics?.NodesProduced ?? 0,
                importMetrics?.CharactersProduced ?? 0,
                approximatePeakWorkingSetBytes);
        }

        public void Dispose() => stopwatch.Stop();
    }

    private sealed class PublicationWorkspace : IDisposable
    {
        private PublicationWorkspace(string path)
        {
            Path = path;
            RoundTripPath = System.IO.Path.Combine(path, "roundtrip.flow.json");
        }

        internal string Path { get; }

        internal string RoundTripPath { get; }

        internal static PublicationWorkspace Create(string root, EpubCorpusPublicationId publicationId)
        {
            Directory.CreateDirectory(root);
            var safeId = string.Concat(publicationId.Value.Select(static character =>
                char.IsAsciiLetterOrDigit(character) || character is '-' or '_' ? character : '-'));
            var path = System.IO.Path.Combine(root, $"flow-corpus-{safeId}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new PublicationWorkspace(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
