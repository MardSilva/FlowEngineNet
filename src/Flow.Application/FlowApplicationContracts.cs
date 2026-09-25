using System.Collections.Immutable;
using Flow.Documents;
using Flow.Epub;
using Flow.Layout;
using Flow.Rendering;
using Flow.Rendering.Html;
using Flow.Security;

namespace Flow.Application;

/// <summary>Identifies a reusable Flow application operation.</summary>
public enum FlowApplicationOperation
{
    InspectEpub,
    ImportEpub,
    ValidateDocument,
    CalculateDocumentHash,
    RenderHtml,
    RenderHtmlBook,
}

/// <summary>Identifies the lifecycle stage of an application operation.</summary>
public enum FlowApplicationProgressStage
{
    Started,
    Processing,
    Validating,
    LayingOut,
    Rendering,
    Completed,
}

/// <summary>Contains one immutable, noncanonical progress observation.</summary>
public sealed record FlowApplicationProgress
{
    public FlowApplicationProgress(
        FlowApplicationOperation operation,
        FlowApplicationProgressStage stage,
        long completedUnits = 0,
        long? totalUnits = null,
        string? currentResource = null)
    {
        if (!Enum.IsDefined(operation))
        {
            throw new ArgumentOutOfRangeException(nameof(operation));
        }

        if (!Enum.IsDefined(stage))
        {
            throw new ArgumentOutOfRangeException(nameof(stage));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(completedUnits);
        if (totalUnits is not null)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(totalUnits.Value);
            if (completedUnits > totalUnits.Value)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(completedUnits),
                    "Completed units cannot exceed the known total.");
            }
        }

        Operation = operation;
        Stage = stage;
        CompletedUnits = completedUnits;
        TotalUnits = totalUnits;
        CurrentResource = string.IsNullOrWhiteSpace(currentResource) ? null : currentResource;
    }

    public FlowApplicationOperation Operation { get; }

    public FlowApplicationProgressStage Stage { get; }

    public long CompletedUnits { get; }

    public long? TotalUnits { get; }

    public string? CurrentResource { get; }
}

/// <summary>Classifies an application diagnostic without binding it to a presentation technology.</summary>
public enum FlowApplicationDiagnosticSeverity
{
    Information,
    Warning,
    Error,
}

/// <summary>Contains a stable diagnostic suitable for a CLI or graphical host.</summary>
public sealed record FlowApplicationDiagnostic(
    string Code,
    FlowApplicationDiagnosticSeverity Severity,
    string Message,
    string? Location = null,
    int Count = 1)
{
    public string Code { get; init; } = !string.IsNullOrWhiteSpace(Code)
        ? Code
        : throw new ArgumentException("A diagnostic code is required.", nameof(Code));

    public string Message { get; init; } = !string.IsNullOrWhiteSpace(Message)
        ? Message
        : throw new ArgumentException("A diagnostic message is required.", nameof(Message));

    public int Count { get; init; } = Count > 0
        ? Count
        : throw new ArgumentOutOfRangeException(nameof(Count), "A diagnostic count must be positive.");
}

/// <summary>Requests structural inspection of a caller-owned readable EPUB stream.</summary>
public sealed record InspectEpubRequest
{
    public InspectEpubRequest(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
        {
            throw new ArgumentException("The EPUB source stream must be readable.", nameof(source));
        }

        Source = source;
    }

    public Stream Source { get; }
}

/// <summary>Contains the typed outcome of an EPUB inspection.</summary>
public sealed record InspectEpubResult(
    EpubPublicationInspection Inspection,
    ImmutableArray<FlowApplicationDiagnostic> Diagnostics);

/// <summary>Requests import of a caller-owned readable EPUB stream.</summary>
public sealed record ImportEpubRequest
{
    public ImportEpubRequest(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
        {
            throw new ArgumentException("The EPUB source stream must be readable.", nameof(source));
        }

        Source = source;
    }

    public Stream Source { get; }
}

/// <summary>Contains EPUB import evidence and semantic checks useful to every host.</summary>
public sealed record ImportEpubResult(
    EpubImportResult Import,
    ValidationResult? Validation,
    DocumentHash? Hash,
    ImmutableArray<FlowApplicationDiagnostic> Diagnostics);

/// <summary>Requests validation of an already loaded semantic document.</summary>
public sealed record ValidateDocumentRequest(FlowDocument Document);

/// <summary>Contains semantic validation and normalized diagnostics.</summary>
public sealed record ValidateDocumentResult(
    ValidationResult Validation,
    ImmutableArray<FlowApplicationDiagnostic> Diagnostics);

/// <summary>Requests the canonical hash of an already loaded semantic document.</summary>
public sealed record CalculateDocumentHashRequest(FlowDocument Document);

/// <summary>Contains the canonical document hash.</summary>
public sealed record CalculateDocumentHashResult(DocumentHash Hash);

/// <summary>Requests one standalone HTML rendering without selecting an output path.</summary>
public sealed record RenderHtmlRequest
{
    public RenderHtmlRequest(
        FlowDocument document,
        double viewportWidth,
        double viewportHeight,
        UserReadingPreferences? userPreferences = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidateViewport(viewportWidth, nameof(viewportWidth));
        ValidateViewport(viewportHeight, nameof(viewportHeight));
        Document = document;
        ViewportWidth = viewportWidth;
        ViewportHeight = viewportHeight;
        UserPreferences = userPreferences ?? new UserReadingPreferences();
    }

    public FlowDocument Document { get; }

    public double ViewportWidth { get; }

    public double ViewportHeight { get; }

    public UserReadingPreferences UserPreferences { get; }

    internal static void ValidateViewport(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "A viewport dimension must be finite and positive.");
        }
    }
}

/// <summary>Contains a standalone HTML artifact and the noncanonical layout used to produce it.</summary>
public sealed record RenderHtmlResult(
    RenderedDocument RenderedDocument,
    LayoutDocument Layout,
    DocumentHash Hash);

/// <summary>Requests a navigable HTML book package without selecting an output directory.</summary>
public sealed record RenderHtmlBookRequest
{
    public RenderHtmlBookRequest(
        FlowDocument document,
        double viewportWidth = 1024,
        double viewportHeight = 768,
        UserReadingPreferences? userPreferences = null,
        HtmlBookUiLanguage uiLanguage = HtmlBookUiLanguage.Automatic)
    {
        ArgumentNullException.ThrowIfNull(document);
        RenderHtmlRequest.ValidateViewport(viewportWidth, nameof(viewportWidth));
        RenderHtmlRequest.ValidateViewport(viewportHeight, nameof(viewportHeight));
        if (!Enum.IsDefined(uiLanguage))
        {
            throw new ArgumentOutOfRangeException(nameof(uiLanguage));
        }

        Document = document;
        ViewportWidth = viewportWidth;
        ViewportHeight = viewportHeight;
        UserPreferences = userPreferences ?? new UserReadingPreferences();
        UiLanguage = uiLanguage;
    }

    public FlowDocument Document { get; }

    public double ViewportWidth { get; }

    public double ViewportHeight { get; }

    public UserReadingPreferences UserPreferences { get; }

    public HtmlBookUiLanguage UiLanguage { get; }
}

/// <summary>Contains a multi-file HTML package and the noncanonical layout used to produce it.</summary>
public sealed record RenderHtmlBookResult(
    HtmlBookPackage Package,
    LayoutDocument Layout,
    DocumentHash Hash);
