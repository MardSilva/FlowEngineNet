using System.Collections.Immutable;
using System.Xml.Linq;
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
        IEpubCheckAdapter? epubCheckAdapter = null)
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
            catch (Exception exception)
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
        state.Complete(EpubCorpusExecutionPhase.Discovery);

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
        catch (Exception)
        {
            state.Fail(EpubCorpusExecutionDiagnosticCodes.InspectionFailed, EpubCorpusExecutionPhase.Inspection,
                "Structural inspection failed before it could produce a complete result.");
        }

        EpubImportResult? importResult = null;
        FlowDocument? document = null;
        try
        {
            await using var source = discovery.OpenRead(publication.Id);
            importResult = await importer.ImportAsync(source, cancellationToken).ConfigureAwait(false);
            state.ImportMetrics = importResult.Metrics;
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
        catch (Exception)
        {
            state.Fail(EpubCorpusExecutionDiagnosticCodes.ImportFailed, EpubCorpusExecutionPhase.Import,
                "EPUB import failed before it could produce a complete result.");
        }

        if (document is null)
        {
            VerifyExpectations(state, publication, inspection, null, null, null, cancellationToken);
            await AddExternalEvidenceAsync(state, publication, discovery, cancellationToken).ConfigureAwait(false);
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
            catch (Exception)
            {
                state.Fail(EpubCorpusExecutionDiagnosticCodes.FidelityFailed, EpubCorpusExecutionPhase.Fidelity,
                    "Fidelity analysis failed before it could produce a complete report.");
            }
        }

        FlowDocument? restored = null;
        try
        {
            using var json = new MemoryStream();
            await serializer.SerializeAsync(document, json, cancellationToken).ConfigureAwait(false);
            state.FlowJsonBytes = checked((int)json.Length);
            json.Position = 0;
            restored = await serializer.DeserializeAsync(json, cancellationToken).ConfigureAwait(false);
            state.Complete(EpubCorpusExecutionPhase.Serialization);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            state.Fail(EpubCorpusExecutionDiagnosticCodes.RoundTripFailed, EpubCorpusExecutionPhase.Serialization,
                "The Flow JSON round trip failed.");
        }

        if (restored is not null)
        {
            if (document.Identity != restored.Identity)
            {
                state.Fail(EpubCorpusExecutionDiagnosticCodes.IdentityMismatch, EpubCorpusExecutionPhase.Integrity,
                    "Document identity changed during the Flow JSON round trip.");
            }

            var originalHash = integrityService.ComputeHash(document);
            var restoredHash = integrityService.ComputeHash(restored);
            state.CanonicalHash = originalHash.Hash;
            state.Complete(EpubCorpusExecutionPhase.Integrity);
            if (originalHash != restoredHash)
            {
                state.Fail(EpubCorpusExecutionDiagnosticCodes.CanonicalHashMismatch, EpubCorpusExecutionPhase.Integrity,
                    "Canonical document hash changed during the Flow JSON round trip.");
            }
        }

        LayoutDocument? mobileLayout = null;
        LayoutDocument? desktopLayout = null;
        if (validation.IsValid)
        {
            mobileLayout = TryLayout(state, document, new LayoutContext(390, 844, DeviceClass.Phone), EpubCorpusExecutionPhase.MobileLayout, cancellationToken);
            desktopLayout = TryLayout(state, document, new LayoutContext(1600, 1000, DeviceClass.Desktop), EpubCorpusExecutionPhase.DesktopLayout, cancellationToken);
        }

        var packages = new List<HtmlBookPackage>(2);
        if (mobileLayout is not null && desktopLayout is not null && state.CanonicalHash is not null)
        {
            try
            {
                var hash = integrityService.ComputeHash(document);
                var integrity = new HtmlBookIntegrity(hash.Algorithm, hash.Hash, hash.CanonicalizationVersion);
                packages.Add(htmlRenderer.Render(document, mobileLayout, DefaultReadingPreferences, integrity, new HtmlBookPackageOptions(), cancellationToken));
                packages.Add(htmlRenderer.Render(document, desktopLayout, DefaultReadingPreferences, integrity, new HtmlBookPackageOptions(), cancellationToken));
                foreach (var package in packages)
                {
                    VerifyPackage(package);
                }

                state.Packages = packages;
                state.Complete(EpubCorpusExecutionPhase.HtmlBookPackage);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                state.Fail(EpubCorpusExecutionDiagnosticCodes.HtmlPackageFailed, EpubCorpusExecutionPhase.HtmlBookPackage,
                    "HTML book package generation or internal reference verification failed.");
            }
        }

        VerifyExpectations(state, publication, inspection, document, validation, packages, cancellationToken);
        await AddExternalEvidenceAsync(state, publication, discovery, cancellationToken).ConfigureAwait(false);
        return state.Build();
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

    private LayoutDocument? TryLayout(
        ExecutionState state,
        FlowDocument document,
        LayoutContext context,
        EpubCorpusExecutionPhase phase,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var layout = layoutEngine.Layout(document, context);
            EnsureLayoutPreservesIds(document, layout);
            state.SetLayout(phase, layout);
            state.Complete(phase);
            return layout;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            state.Fail(EpubCorpusExecutionDiagnosticCodes.LayoutFailed, phase,
                phase == EpubCorpusExecutionPhase.MobileLayout
                    ? "Mobile layout generation failed."
                    : "Desktop layout generation failed.");
            return null;
        }
    }

    private static void VerifyExpectations(
        ExecutionState state,
        EpubCorpusPublication publication,
        EpubPublicationInspection? inspection,
        FlowDocument? document,
        ValidationResult? validation,
        IReadOnlyList<HtmlBookPackage>? packages,
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
            else if (packages is not null)
            {
                VerifyDeclaredFeatureInPackages(state, feature, packages);
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
                "mobile-layout" => state.MobileLayout is not null,
                "desktop-layout" => state.DesktopLayout is not null,
                "html-book-package" => packages?.Count == 2,
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

    private static void VerifyDeclaredFeatureInPackages(
        ExecutionState state,
        string feature,
        IReadOnlyList<HtmlBookPackage> packages)
    {
        string? elementName = feature switch
        {
            "table-of-contents" or "toc" => "nav",
            "figure" or "image" or "cover" => "img",
            "ordered-list" => "ol",
            "unordered-list" => "ul",
            "footnote" or "note" => "section",
            "table" => "table",
            _ => null,
        };
        if (elementName is null)
        {
            return;
        }

        if (!packages.All(package => PackageContainsElement(package, elementName)))
        {
            state.ExpectationFailed(feature, "Expected feature was not found in every generated HTML book package.");
        }
    }

    private static bool PackageContainsElement(HtmlBookPackage package, string localName) => package.Files
        .Where(static file => file.MediaType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
        .Select(static file => XDocument.Parse(System.Text.Encoding.UTF8.GetString(file.Content.AsSpan())))
        .Any(document => document.Descendants().Any(element => element.Name.LocalName == localName));

    private static void VerifyPackage(HtmlBookPackage package)
    {
        var files = package.Files.ToDictionary(static file => file.Path, StringComparer.Ordinal);
        var documents = package.Files
            .Where(static file => file.MediaType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                static file => file.Path,
                static file => XDocument.Parse(System.Text.Encoding.UTF8.GetString(file.Content.AsSpan())),
                StringComparer.Ordinal);

        foreach (var (currentPath, document) in documents)
        {
            foreach (var attribute in document.Descendants().Attributes()
                         .Where(static attribute => attribute.Name.LocalName is "href" or "src"))
            {
                var target = attribute.Value;
                if (string.IsNullOrEmpty(target)
                    || target.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                    || Uri.TryCreate(target, UriKind.Absolute, out _))
                {
                    continue;
                }

                var hashIndex = target.IndexOf('#');
                var pathPart = hashIndex < 0 ? target : target[..hashIndex];
                var fragment = hashIndex < 0 ? null : Uri.UnescapeDataString(target[(hashIndex + 1)..]);
                var resolvedPath = string.IsNullOrEmpty(pathPart)
                    ? currentPath
                    : ResolvePackagePath(currentPath, Uri.UnescapeDataString(pathPart));
                if (!files.ContainsKey(resolvedPath))
                {
                    throw new InvalidDataException("The HTML package contains an unresolved local file reference.");
                }

                if (!string.IsNullOrEmpty(fragment)
                    && documents.TryGetValue(resolvedPath, out var targetDocument)
                    && !targetDocument.Descendants().Any(element => (string?)element.Attribute("id") == fragment))
                {
                    throw new InvalidDataException("The HTML package contains an unresolved local fragment reference.");
                }
            }
        }
    }

    private static string ResolvePackagePath(string currentPath, string relativePath)
    {
        var segments = currentPath.Split('/').SkipLast(1).ToList();
        foreach (var segment in relativePath.Split('/'))
        {
            if (segment is "" or ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count == 0)
                {
                    throw new InvalidDataException("The HTML package reference escapes its root.");
                }

                segments.RemoveAt(segments.Count - 1);
            }
            else
            {
                segments.Add(segment);
            }
        }

        return string.Join('/', segments);
    }

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
        internal LayoutDocument? MobileLayout;
        internal LayoutDocument? DesktopLayout;
        internal IReadOnlyList<HtmlBookPackage> Packages = [];
        internal int ValidationDiagnosticCount;
        internal int FlowJsonBytes;
        internal string? CanonicalHash;
        internal EpubCheckEvidence? EpubCheckEvidence;

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

        internal void SetLayout(EpubCorpusExecutionPhase phase, LayoutDocument layout)
        {
            if (phase == EpubCorpusExecutionPhase.MobileLayout)
            {
                MobileLayout = layout;
            }
            else
            {
                DesktopLayout = layout;
            }
        }

        internal EpubCorpusPublicationExecutionResult Build()
        {
            var htmlFiles = Packages.Sum(static package => package.Files.Length);
            var htmlBytes = Packages.SelectMany(static package => package.Files).Sum(static file => (long)file.Content.Length);
            var metrics = ImportMetrics is null
                ? null
                : new EpubCorpusEnvironmentMetrics(
                    ImportMetrics.TotalDuration.Ticks,
                    ImportMetrics.ApproximatePeakManagedBytes,
                    ImportMetrics.ArchiveEntryCount,
                    ImportMetrics.CompressedBytes,
                    ImportMetrics.UncompressedBytes,
                    ImportMetrics.AssetBytes,
                    ImportMetrics.SpineDocumentsProcessed,
                    ImportMetrics.NodesProduced,
                    ImportMetrics.CharactersProduced);
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
                MobileLayout is null ? 0 : EnumerateLayoutNodes(MobileLayout.Nodes).Count(),
                DesktopLayout is null ? 0 : EnumerateLayoutNodes(DesktopLayout.Nodes).Count(),
                Packages.Count,
                htmlFiles,
                htmlBytes);
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
                metrics,
                EpubCheckEvidence);
        }
    }
}
