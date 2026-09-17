using System.Collections.Immutable;
using Flow.Epub;

namespace Flow.Epub.Corpus;

/// <summary>Classifies the final outcome of one cataloged publication.</summary>
public enum EpubCorpusExecutionStatus
{
    Passed,
    Failed,
    Skipped,
    Inconclusive,
}

/// <summary>Identifies a reproducible phase of the corpus pipeline.</summary>
public enum EpubCorpusExecutionPhase
{
    Discovery,
    Inspection,
    Import,
    Validation,
    Fidelity,
    Serialization,
    Integrity,
    MobileLayout,
    DesktopLayout,
    HtmlBookPackage,
    ExternalConformance,
    Expectations,
}

/// <summary>Classifies the effect of one corpus execution diagnostic.</summary>
public enum EpubCorpusExecutionDiagnosticSeverity
{
    Information,
    Warning,
    Error,
}

/// <summary>Locates one path-free execution finding.</summary>
public sealed record EpubCorpusExecutionDiagnostic
{
    public EpubCorpusExecutionDiagnostic(
        string code,
        EpubCorpusExecutionDiagnosticSeverity severity,
        EpubCorpusExecutionPhase phase,
        string message,
        string? sourceCode = null,
        string? resource = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (!Enum.IsDefined(severity))
        {
            throw new ArgumentOutOfRangeException(nameof(severity));
        }

        if (!Enum.IsDefined(phase))
        {
            throw new ArgumentOutOfRangeException(nameof(phase));
        }

        Code = code;
        Severity = severity;
        Phase = phase;
        Message = message;
        SourceCode = string.IsNullOrWhiteSpace(sourceCode) ? null : sourceCode;
        Resource = NormalizeResource(resource);
    }

    public string Code { get; }

    public EpubCorpusExecutionDiagnosticSeverity Severity { get; }

    public EpubCorpusExecutionPhase Phase { get; }

    public string Message { get; }

    public string? SourceCode { get; }

    public string? Resource { get; }

    private static string? NormalizeResource(string? resource)
    {
        if (string.IsNullOrWhiteSpace(resource))
        {
            return null;
        }

        var value = resource.Replace('\\', '/');
        return Path.IsPathFullyQualified(resource) || Uri.TryCreate(value, UriKind.Absolute, out _)
            ? null
            : value;
    }
}

/// <summary>Contains stable, environment-independent evidence produced for one publication.</summary>
public sealed record EpubCorpusPublicationEvidence(
    string? EpubVersion,
    int ManifestItemCount,
    int SpineItemCount,
    int ImportedNodeCount,
    int ImportedAssetCount,
    int ValidationDiagnosticCount,
    long FidelitySourceUnitCount,
    long FidelityLostUnitCount,
    string? DocumentId,
    string? CanonicalHash,
    int FlowJsonBytes,
    int MobileLayoutNodeCount,
    int DesktopLayoutNodeCount,
    int HtmlPackageCount,
    int HtmlFileCount,
    long HtmlBytes,
    EpubCorpusSemanticEvidence? Semantic = null);

/// <summary>Captures stable semantic evidence used to localize corpus regressions.</summary>
public sealed record EpubCorpusSemanticEvidence
{
    public EpubCorpusSemanticEvidence(
        IEnumerable<string> orderedNodeIds,
        int sourceLocationCount,
        int chapterCount,
        int headingCount,
        int paragraphCount,
        int tableOfContentsEntryCount,
        int internalLinkCount,
        int figureCount,
        int footnoteCount,
        int footnoteReferenceCount,
        int tableCount,
        int tableCellCount,
        IEnumerable<string>? orderedChapterIds = null)
    {
        ArgumentNullException.ThrowIfNull(orderedNodeIds);
        OrderedNodeIds = orderedNodeIds.ToImmutableArray();
        SourceLocationCount = sourceLocationCount;
        ChapterCount = chapterCount;
        HeadingCount = headingCount;
        ParagraphCount = paragraphCount;
        TableOfContentsEntryCount = tableOfContentsEntryCount;
        InternalLinkCount = internalLinkCount;
        FigureCount = figureCount;
        FootnoteCount = footnoteCount;
        FootnoteReferenceCount = footnoteReferenceCount;
        TableCount = tableCount;
        TableCellCount = tableCellCount;
        OrderedChapterIds = (orderedChapterIds ?? []).ToImmutableArray();
    }

    public ImmutableArray<string> OrderedNodeIds { get; }

    public ImmutableArray<string> OrderedChapterIds { get; }

    public int SourceLocationCount { get; }

    public int ChapterCount { get; }

    public int HeadingCount { get; }

    public int ParagraphCount { get; }

    public int TableOfContentsEntryCount { get; }

    public int InternalLinkCount { get; }

    public int FigureCount { get; }

    public int FootnoteCount { get; }

    public int FootnoteReferenceCount { get; }

    public int TableCount { get; }

    public int TableCellCount { get; }
}

/// <summary>
/// Contains runtime observations that are useful for capacity analysis but excluded from deterministic comparison.
/// </summary>
public sealed record EpubCorpusEnvironmentMetrics(
    long TotalDurationTicks,
    long ApproximatePeakManagedBytes,
    int ArchiveEntryCount,
    long CompressedBytes,
    long UncompressedBytes,
    long AssetBytes,
    int SpineDocumentsProcessed,
    int NodesProduced,
    long CharactersProduced,
    long ApproximatePeakWorkingSetBytes = 0);

/// <summary>Contains the complete result for one stable corpus publication ID.</summary>
public sealed record EpubCorpusPublicationExecutionResult
{
    public EpubCorpusPublicationExecutionResult(
        EpubCorpusPublicationId id,
        EpubCorpusExecutionStatus status,
        IEnumerable<EpubCorpusExecutionPhase> completedPhases,
        EpubCorpusPublicationEvidence evidence,
        IEnumerable<EpubCorpusExecutionDiagnostic> diagnostics,
        EpubCorpusEnvironmentMetrics? environmentMetrics = null,
        EpubCheckEvidence? epubCheckEvidence = null)
    {
        ArgumentNullException.ThrowIfNull(completedPhases);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(diagnostics);
        Id = id;
        Status = status;
        CompletedPhases = completedPhases.Distinct().Order().ToImmutableArray();
        Evidence = evidence;
        Diagnostics = diagnostics
            .OrderBy(static item => item.Phase)
            .ThenBy(static item => item.Code, StringComparer.Ordinal)
            .ThenBy(static item => item.Resource, StringComparer.Ordinal)
            .ThenBy(static item => item.Message, StringComparer.Ordinal)
            .ToImmutableArray();
        EnvironmentMetrics = environmentMetrics;
        EpubCheckEvidence = epubCheckEvidence;
        EvidenceRelationship = ResolveRelationship(status, epubCheckEvidence?.Status);
    }

    public EpubCorpusPublicationId Id { get; }

    public EpubCorpusExecutionStatus Status { get; }

    public ImmutableArray<EpubCorpusExecutionPhase> CompletedPhases { get; }

    public EpubCorpusPublicationEvidence Evidence { get; }

    public ImmutableArray<EpubCorpusExecutionDiagnostic> Diagnostics { get; }

    public EpubCorpusEnvironmentMetrics? EnvironmentMetrics { get; }

    public EpubCheckEvidence? EpubCheckEvidence { get; }

    public EpubCorpusEvidenceRelationship EvidenceRelationship { get; }

    private static EpubCorpusEvidenceRelationship ResolveRelationship(
        EpubCorpusExecutionStatus flowStatus,
        EpubCheckEvidenceStatus? epubStatus) => (flowStatus, epubStatus) switch
        {
            (EpubCorpusExecutionStatus.Passed, EpubCheckEvidenceStatus.Conformant) => EpubCorpusEvidenceRelationship.AgreePassed,
            (EpubCorpusExecutionStatus.Failed, EpubCheckEvidenceStatus.NonConformant) => EpubCorpusEvidenceRelationship.AgreeFailed,
            (EpubCorpusExecutionStatus.Passed, EpubCheckEvidenceStatus.NonConformant) => EpubCorpusEvidenceRelationship.FlowPassedEpubCheckFailed,
            (EpubCorpusExecutionStatus.Failed, EpubCheckEvidenceStatus.Conformant) => EpubCorpusEvidenceRelationship.FlowFailedEpubCheckPassed,
            _ => EpubCorpusEvidenceRelationship.NotEvaluated,
        };
}

/// <summary>Summarizes mutually exclusive corpus outcomes.</summary>
public sealed record EpubCorpusExecutionSummary(int Passed, int Failed, int Skipped, int Inconclusive)
{
    public int Total => Passed + Failed + Skipped + Inconclusive;
}

/// <summary>Contains deterministic results and separately marked runtime observations.</summary>
public sealed record EpubCorpusExecutionReport
{
    public const string CurrentFormat = "flow-epub-corpus-execution-0.1";

    public EpubCorpusExecutionReport(IEnumerable<EpubCorpusPublicationExecutionResult> publications)
    {
        ArgumentNullException.ThrowIfNull(publications);
        Publications = publications.OrderBy(static item => item.Id.Value, StringComparer.Ordinal).ToImmutableArray();
        Summary = new EpubCorpusExecutionSummary(
            Publications.Count(static item => item.Status == EpubCorpusExecutionStatus.Passed),
            Publications.Count(static item => item.Status == EpubCorpusExecutionStatus.Failed),
            Publications.Count(static item => item.Status == EpubCorpusExecutionStatus.Skipped),
            Publications.Count(static item => item.Status == EpubCorpusExecutionStatus.Inconclusive));
    }

    public EpubCorpusExecutionSummary Summary { get; }

    public ImmutableArray<EpubCorpusPublicationExecutionResult> Publications { get; }
}

/// <summary>Defines stable diagnostics emitted by corpus execution.</summary>
public static class EpubCorpusExecutionDiagnosticCodes
{
    public const string DiscoveryUnavailable = "EPC020";
    public const string InspectionFailed = "EPC021";
    public const string UnexpectedEpubVersion = "EPC022";
    public const string ImportFailed = "EPC023";
    public const string ValidationFailed = "EPC024";
    public const string FidelityFailed = "EPC025";
    public const string RoundTripFailed = "EPC026";
    public const string IdentityMismatch = "EPC027";
    public const string CanonicalHashMismatch = "EPC028";
    public const string LayoutFailed = "EPC029";
    public const string HtmlPackageFailed = "EPC030";
    public const string ExpectationFailed = "EPC031";
    public const string UnsupportedExpectation = "EPC032";
    public const string ProcessingFailed = "EPC033";
}

/// <summary>Executes the bounded EPUB corpus pipeline.</summary>
public interface IEpubCorpusExecutor
{
    public Task<EpubCorpusExecutionReport> ExecuteAsync(
        EpubCorpusManifest manifest,
        EpubCorpusDiscoveryOptions discoveryOptions,
        CancellationToken cancellationToken = default);
}
