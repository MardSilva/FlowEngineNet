using System.Collections.Immutable;
using Flow.Epub;

namespace Flow.Epub.Corpus;

/// <summary>Classifies the private qualification outcome without exposing publication identity.</summary>
public enum EpubPrivateQualificationStatus
{
    Passed,
    Failed,
    Inconclusive,
    Nondeterministic,
    SkippedProtected,
    SkippedCorrupt,
    SkippedUnsuitable,
}

/// <summary>Contains a stable diagnostic count without source paths or diagnostic prose.</summary>
public sealed record EpubPrivateQualificationDiagnosticCount
{
    public EpubPrivateQualificationDiagnosticCount(
        string code,
        EpubCorpusExecutionDiagnosticSeverity severity,
        EpubCorpusExecutionPhase? phase,
        int count)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        Code = code;
        Severity = severity;
        Phase = phase;
        Count = count;
    }

    public string Code { get; }

    public EpubCorpusExecutionDiagnosticSeverity Severity { get; }

    public EpubCorpusExecutionPhase? Phase { get; }

    public int Count { get; }
}

/// <summary>Contains path-free evidence from the complete Flow pipeline.</summary>
public sealed record EpubPrivateQualificationEvidence(
    int ManifestItemCount,
    int SpineItemCount,
    int ImportedNodeCount,
    int ImportedAssetCount,
    int ValidationDiagnosticCount,
    long FidelitySourceUnitCount,
    long FidelityLostUnitCount,
    string? CanonicalHash,
    int FlowJsonBytes,
    int MobileLayoutNodeCount,
    int DesktopLayoutNodeCount,
    int HtmlPackageCount,
    int HtmlFileCount,
    long HtmlBytes,
    int ChapterCount,
    int HeadingCount,
    int ParagraphCount,
    int TableOfContentsEntryCount,
    int InternalLinkCount,
    int FigureCount,
    int FootnoteCount,
    int FootnoteReferenceCount,
    int TableCount,
    int TableCellCount)
{
    public static EpubPrivateQualificationEvidence Empty { get; } = new(
        0, 0, 0, 0, 0, 0, 0, null, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}

/// <summary>Describes one qualified or explicitly skipped neutral inventory candidate.</summary>
public sealed record EpubPrivateQualificationItem
{
    public EpubPrivateQualificationItem(
        EpubCorpusPublicationId id,
        EpubCorpusSha256? sourceSha256,
        EpubPrivateInventoryStatus inventoryStatus,
        EpubPrivateQualificationStatus status,
        bool eligible,
        bool stableAcrossRepeatedRuns,
        IEnumerable<EpubCorpusExecutionPhase> completedPhases,
        EpubPrivateQualificationEvidence evidence,
        IEnumerable<EpubPrivateQualificationDiagnosticCount> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(completedPhases);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(diagnostics);
        Id = id;
        SourceSha256 = sourceSha256;
        InventoryStatus = inventoryStatus;
        Status = status;
        Eligible = eligible;
        StableAcrossRepeatedRuns = stableAcrossRepeatedRuns;
        CompletedPhases = completedPhases.Distinct().Order().ToImmutableArray();
        Evidence = evidence;
        Diagnostics = diagnostics
            .OrderBy(static item => item.Phase)
            .ThenBy(static item => item.Code, StringComparer.Ordinal)
            .ThenBy(static item => item.Severity)
            .ToImmutableArray();
    }

    public EpubCorpusPublicationId Id { get; }

    public EpubCorpusSha256? SourceSha256 { get; }

    public EpubPrivateInventoryStatus InventoryStatus { get; }

    public EpubPrivateQualificationStatus Status { get; }

    public bool Eligible { get; }

    public bool StableAcrossRepeatedRuns { get; }

    public ImmutableArray<EpubCorpusExecutionPhase> CompletedPhases { get; }

    public EpubPrivateQualificationEvidence Evidence { get; }

    public ImmutableArray<EpubPrivateQualificationDiagnosticCount> Diagnostics { get; }
}

/// <summary>Summarizes mutually exclusive private qualification outcomes.</summary>
public sealed record EpubPrivateQualificationSummary(
    int Total,
    int Eligible,
    int Passed,
    int Failed,
    int Inconclusive,
    int Nondeterministic,
    int Skipped);

/// <summary>Contains deterministic, path-free evidence for a private EPUB directory.</summary>
public sealed record EpubPrivateQualificationReport
{
    public const string CurrentFormat = "flow-epub-private-qualification-0.1";

    public EpubPrivateQualificationReport(
        bool deterministicAcrossRepeatedRuns,
        IEnumerable<EpubPrivateQualificationItem> publications)
    {
        ArgumentNullException.ThrowIfNull(publications);
        DeterministicAcrossRepeatedRuns = deterministicAcrossRepeatedRuns;
        Publications = publications.OrderBy(static item => item.Id.Value, StringComparer.Ordinal).ToImmutableArray();
        Summary = new EpubPrivateQualificationSummary(
            Publications.Length,
            Publications.Count(static item => item.Eligible),
            Publications.Count(static item => item.Status == EpubPrivateQualificationStatus.Passed),
            Publications.Count(static item => item.Status == EpubPrivateQualificationStatus.Failed),
            Publications.Count(static item => item.Status == EpubPrivateQualificationStatus.Inconclusive),
            Publications.Count(static item => item.Status == EpubPrivateQualificationStatus.Nondeterministic),
            Publications.Count(static item => !item.Eligible));
    }

    public bool DeterministicAcrossRepeatedRuns { get; }

    public EpubPrivateQualificationSummary Summary { get; }

    public ImmutableArray<EpubPrivateQualificationItem> Publications { get; }
}

/// <summary>Configures explicit legal declarations and bounded inventory discovery.</summary>
public sealed record EpubPrivateQualificationOptions
{
    public EpubPrivateQualificationOptions(
        bool legalUseDeclared,
        bool drmFreeDeclared,
        EpubPrivateInventoryOptions? inventoryOptions = null)
    {
        if (!legalUseDeclared || !drmFreeDeclared)
        {
            throw new ArgumentException("Private qualification requires explicit legal-use and DRM-free declarations.");
        }

        LegalUseDeclared = legalUseDeclared;
        DrmFreeDeclared = drmFreeDeclared;
        InventoryOptions = inventoryOptions ?? new EpubPrivateInventoryOptions();
    }

    public bool LegalUseDeclared { get; }

    public bool DrmFreeDeclared { get; }

    public EpubPrivateInventoryOptions InventoryOptions { get; }
}

/// <summary>Qualifies eligible private inventory candidates through the existing corpus pipeline.</summary>
public interface IEpubPrivateQualificationService
{
    public Task<EpubPrivateQualificationReport> QualifyAsync(
        string sourceDirectory,
        string repositoryRoot,
        EpubPrivateQualificationOptions options,
        CancellationToken cancellationToken = default);
}
