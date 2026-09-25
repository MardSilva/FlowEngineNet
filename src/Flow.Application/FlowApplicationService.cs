using System.Collections.Immutable;
using Flow.Documents;
using Flow.Epub;
using Flow.Layout;
using Flow.Rendering;
using Flow.Rendering.Html;
using Flow.Security;

namespace Flow.Application;

/// <summary>Coordinates reusable Flow use cases without console, filesystem, or process concerns.</summary>
public sealed class FlowApplicationService : IFlowApplicationService
{
    private readonly IEpubImporter _epubImporter;
    private readonly IEpubPublicationInspector _epubInspector;
    private readonly DocumentValidator _validator;
    private readonly IDocumentIntegrityService _integrityService;
    private readonly ILayoutEngine _layoutEngine;
    private readonly IDocumentRenderer _htmlRenderer;
    private readonly IHtmlBookPackageRenderer _htmlBookRenderer;

    public FlowApplicationService(
        IEpubImporter epubImporter,
        IEpubPublicationInspector epubInspector,
        DocumentValidator validator,
        IDocumentIntegrityService integrityService,
        ILayoutEngine layoutEngine,
        IDocumentRenderer htmlRenderer,
        IHtmlBookPackageRenderer htmlBookRenderer)
    {
        ArgumentNullException.ThrowIfNull(epubImporter);
        ArgumentNullException.ThrowIfNull(epubInspector);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(integrityService);
        ArgumentNullException.ThrowIfNull(layoutEngine);
        ArgumentNullException.ThrowIfNull(htmlRenderer);
        ArgumentNullException.ThrowIfNull(htmlBookRenderer);
        _epubImporter = epubImporter;
        _epubInspector = epubInspector;
        _validator = validator;
        _integrityService = integrityService;
        _layoutEngine = layoutEngine;
        _htmlRenderer = htmlRenderer;
        _htmlBookRenderer = htmlBookRenderer;
    }

    /// <summary>Creates the default in-process composition used by first-party hosts.</summary>
    public static FlowApplicationService CreateDefault() =>
        new(
            new EpubImporter(),
            new EpubPublicationInspector(),
            new DocumentValidator(),
            new Sha256DocumentIntegrityService(new FlowDocumentCanonicalizer()),
            new AdaptiveLayoutEngine(),
            new HtmlDocumentRenderer(),
            new HtmlBookPackageRenderer());

    public async Task<InspectEpubResult> InspectEpubAsync(
        InspectEpubRequest request,
        IProgress<FlowApplicationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        const FlowApplicationOperation operation = FlowApplicationOperation.InspectEpub;
        Report(progress, operation, FlowApplicationProgressStage.Started);
        cancellationToken.ThrowIfCancellationRequested();
        var adapter = CreateEpubProgressAdapter(operation, progress);
        var inspection = await _epubInspector.InspectAsync(request.Source, adapter, cancellationToken)
            .ConfigureAwait(false);
        var diagnostics = MapEpubDiagnostics(inspection.Diagnostics);
        Report(progress, operation, FlowApplicationProgressStage.Completed, 1, 1);
        return new InspectEpubResult(inspection, diagnostics);
    }

    public async Task<ImportEpubResult> ImportEpubAsync(
        ImportEpubRequest request,
        IProgress<FlowApplicationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        const FlowApplicationOperation operation = FlowApplicationOperation.ImportEpub;
        Report(progress, operation, FlowApplicationProgressStage.Started);
        cancellationToken.ThrowIfCancellationRequested();
        var adapter = CreateEpubProgressAdapter(operation, progress);
        var import = await _epubImporter.ImportAsync(request.Source, adapter, cancellationToken).ConfigureAwait(false);
        ValidationResult? validation = null;
        DocumentHash? hash = null;
        var diagnostics = MapEpubDiagnostics(import.Diagnostics).ToBuilder();
        if (import.Document is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, operation, FlowApplicationProgressStage.Validating);
            validation = _validator.Validate(import.Document);
            diagnostics.AddRange(MapValidationDiagnostics(validation.Diagnostics));
            if (validation.IsValid)
            {
                cancellationToken.ThrowIfCancellationRequested();
                hash = _integrityService.ComputeHash(import.Document);
            }
        }

        Report(progress, operation, FlowApplicationProgressStage.Completed, 1, 1);
        return new ImportEpubResult(import, validation, hash, diagnostics.ToImmutable());
    }

    public Task<ValidateDocumentResult> ValidateDocumentAsync(
        ValidateDocumentRequest request,
        IProgress<FlowApplicationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Document);
        const FlowApplicationOperation operation = FlowApplicationOperation.ValidateDocument;
        Report(progress, operation, FlowApplicationProgressStage.Started);
        cancellationToken.ThrowIfCancellationRequested();
        Report(progress, operation, FlowApplicationProgressStage.Validating);
        var validation = _validator.Validate(request.Document);
        cancellationToken.ThrowIfCancellationRequested();
        Report(progress, operation, FlowApplicationProgressStage.Completed, 1, 1);
        return Task.FromResult(new ValidateDocumentResult(
            validation,
            MapValidationDiagnostics(validation.Diagnostics)));
    }

    public Task<CalculateDocumentHashResult> CalculateDocumentHashAsync(
        CalculateDocumentHashRequest request,
        IProgress<FlowApplicationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Document);
        const FlowApplicationOperation operation = FlowApplicationOperation.CalculateDocumentHash;
        Report(progress, operation, FlowApplicationProgressStage.Started);
        cancellationToken.ThrowIfCancellationRequested();
        var hash = _integrityService.ComputeHash(request.Document);
        cancellationToken.ThrowIfCancellationRequested();
        Report(progress, operation, FlowApplicationProgressStage.Completed, 1, 1);
        return Task.FromResult(new CalculateDocumentHashResult(hash));
    }

    public Task<RenderHtmlResult> RenderHtmlAsync(
        RenderHtmlRequest request,
        IProgress<FlowApplicationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        const FlowApplicationOperation operation = FlowApplicationOperation.RenderHtml;
        Report(progress, operation, FlowApplicationProgressStage.Started);
        cancellationToken.ThrowIfCancellationRequested();
        Report(progress, operation, FlowApplicationProgressStage.LayingOut);
        var layout = CreateLayout(
            request.Document,
            request.ViewportWidth,
            request.ViewportHeight,
            request.UserPreferences);
        cancellationToken.ThrowIfCancellationRequested();
        Report(progress, operation, FlowApplicationProgressStage.Rendering);
        var rendered = _htmlRenderer.Render(request.Document, layout, request.UserPreferences);
        cancellationToken.ThrowIfCancellationRequested();
        var hash = _integrityService.ComputeHash(request.Document);
        Report(progress, operation, FlowApplicationProgressStage.Completed, 1, 1);
        return Task.FromResult(new RenderHtmlResult(rendered, layout, hash));
    }

    public Task<RenderHtmlBookResult> RenderHtmlBookAsync(
        RenderHtmlBookRequest request,
        IProgress<FlowApplicationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        const FlowApplicationOperation operation = FlowApplicationOperation.RenderHtmlBook;
        Report(progress, operation, FlowApplicationProgressStage.Started);
        cancellationToken.ThrowIfCancellationRequested();
        Report(progress, operation, FlowApplicationProgressStage.LayingOut);
        var layout = CreateLayout(
            request.Document,
            request.ViewportWidth,
            request.ViewportHeight,
            request.UserPreferences);
        cancellationToken.ThrowIfCancellationRequested();
        var hash = _integrityService.ComputeHash(request.Document);
        Report(progress, operation, FlowApplicationProgressStage.Rendering);
        var package = _htmlBookRenderer.Render(
            request.Document,
            layout,
            request.UserPreferences,
            new HtmlBookIntegrity(hash.Algorithm, hash.Hash, hash.CanonicalizationVersion),
            new HtmlBookPackageOptions(request.UiLanguage),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        Report(progress, operation, FlowApplicationProgressStage.Completed, 1, 1);
        return Task.FromResult(new RenderHtmlBookResult(package, layout, hash));
    }

    private LayoutDocument CreateLayout(
        FlowDocument document,
        double viewportWidth,
        double viewportHeight,
        UserReadingPreferences preferences) =>
        _layoutEngine.Layout(
            document,
            new LayoutContext(
                viewportWidth,
                viewportHeight,
                GetDeviceClass(viewportWidth),
                ReadingMode.Flow,
                userPreferences: preferences));

    private static DeviceClass GetDeviceClass(double width) => width switch
    {
        < 600 => DeviceClass.Phone,
        < 1200 => DeviceClass.Tablet,
        _ => DeviceClass.Desktop,
    };

    private static IProgress<EpubImportProgress>? CreateEpubProgressAdapter(
        FlowApplicationOperation operation,
        IProgress<FlowApplicationProgress>? progress) =>
        progress is null
            ? null
            : new InlineProgress<EpubImportProgress>(update => progress.Report(new FlowApplicationProgress(
                operation,
                FlowApplicationProgressStage.Processing,
                update.CompletedUnits,
                update.TotalUnits,
                update.CurrentResource)));

    private static ImmutableArray<FlowApplicationDiagnostic> MapEpubDiagnostics(
        IEnumerable<EpubDiagnostic> diagnostics) =>
        [.. diagnostics.Select(static diagnostic => new FlowApplicationDiagnostic(
            diagnostic.Code,
            diagnostic.Severity switch
            {
                EpubDiagnosticSeverity.Information => FlowApplicationDiagnosticSeverity.Information,
                EpubDiagnosticSeverity.Warning => FlowApplicationDiagnosticSeverity.Warning,
                _ => FlowApplicationDiagnosticSeverity.Error,
            },
            diagnostic.Message,
            diagnostic.Resource,
            diagnostic.Count))];

    private static ImmutableArray<FlowApplicationDiagnostic> MapValidationDiagnostics(
        IEnumerable<ValidationDiagnostic> diagnostics) =>
        [.. diagnostics.Select(static diagnostic => new FlowApplicationDiagnostic(
            diagnostic.Code,
            diagnostic.Severity switch
            {
                ValidationSeverity.Information => FlowApplicationDiagnosticSeverity.Information,
                ValidationSeverity.Warning => FlowApplicationDiagnosticSeverity.Warning,
                _ => FlowApplicationDiagnosticSeverity.Error,
            },
            diagnostic.Message,
            diagnostic.NodeId?.ToString() ?? diagnostic.Anchor?.ToString()))];

    private static void Report(
        IProgress<FlowApplicationProgress>? progress,
        FlowApplicationOperation operation,
        FlowApplicationProgressStage stage,
        long completedUnits = 0,
        long? totalUnits = null) =>
        progress?.Report(new FlowApplicationProgress(operation, stage, completedUnits, totalUnits));

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        private readonly Action<T> _report = report ?? throw new ArgumentNullException(nameof(report));

        public void Report(T value) => _report(value);
    }
}
