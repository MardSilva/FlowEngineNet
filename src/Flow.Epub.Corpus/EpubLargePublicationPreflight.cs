using System.Collections.Immutable;
using Flow.Epub;

namespace Flow.Epub.Corpus;

/// <summary>Classifies whether a local EPUB can participate in the large-publication gate.</summary>
public enum EpubLargePublicationCandidateStatus
{
    Suitable,
    Inconclusive,
    Rejected,
}

/// <summary>Describes whether a feature can be inferred without importing publication content.</summary>
public enum EpubPreflightFeatureStatus
{
    Unknown,
    Absent,
    Present,
}

/// <summary>Describes whether preflight selected one explicit candidate.</summary>
public enum EpubLargePublicationSelectionStatus
{
    Selected,
    Inconclusive,
}

/// <summary>Identifies the severity of one stable preflight diagnostic.</summary>
public enum EpubLargePublicationPreflightSeverity
{
    Information,
    Warning,
    Error,
}

/// <summary>Contains stable diagnostic codes emitted by large-publication preflight.</summary>
public static class EpubLargePublicationPreflightDiagnosticCodes
{
    public const string LegalUseNotDeclared = "EPL001";
    public const string DrmFreeNotDeclared = "EPL002";
    public const string CandidateInsideRepository = "EPL003";
    public const string CandidateUnavailable = "EPL004";
    public const string CandidatePathUnsafe = "EPL005";
    public const string ArchiveLimitExceeded = "EPL006";
    public const string InspectionFailed = "EPL007";
    public const string UnknownEpubVersion = "EPL008";
    public const string MissingReadingOrder = "EPL009";
    public const string MissingXhtml = "EPL010";
    public const string MissingNavigation = "EPL011";
    public const string InsufficientLengthEvidence = "EPL012";
    public const string InsufficientResourceVariety = "EPL013";
    public const string NoSuitableCandidate = "EPL014";
}

/// <summary>Supplies one explicitly chosen local EPUB without persisting its physical path.</summary>
public sealed record EpubLargePublicationCandidate
{
    public EpubLargePublicationCandidate(
        EpubCorpusPublicationId id,
        string path,
        bool legalUseDeclared,
        bool drmFreeDeclared)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Id = id;
        Path = System.IO.Path.GetFullPath(path);
        LegalUseDeclared = legalUseDeclared;
        DrmFreeDeclared = drmFreeDeclared;
    }

    public EpubCorpusPublicationId Id { get; }

    /// <summary>Gets the local input path, which is deliberately excluded from reports.</summary>
    public string Path { get; }

    public bool LegalUseDeclared { get; }

    public bool DrmFreeDeclared { get; }
}

/// <summary>Defines structural evidence required to call a candidate representative and long.</summary>
public sealed record EpubLargePublicationPreflightCriteria
{
    public EpubLargePublicationPreflightCriteria(
        int minimumLinearSpineItems = 20,
        int minimumXhtmlDocuments = 20,
        long minimumXhtmlBytes = 512 * 1024,
        int minimumSpineItemsForByteEvidence = 5,
        int minimumResourceClasses = 2)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumLinearSpineItems);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumXhtmlDocuments);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumXhtmlBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumSpineItemsForByteEvidence);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumResourceClasses);
        MinimumLinearSpineItems = minimumLinearSpineItems;
        MinimumXhtmlDocuments = minimumXhtmlDocuments;
        MinimumXhtmlBytes = minimumXhtmlBytes;
        MinimumSpineItemsForByteEvidence = minimumSpineItemsForByteEvidence;
        MinimumResourceClasses = minimumResourceClasses;
    }

    public int MinimumLinearSpineItems { get; }

    public int MinimumXhtmlDocuments { get; }

    public long MinimumXhtmlBytes { get; }

    public int MinimumSpineItemsForByteEvidence { get; }

    public int MinimumResourceClasses { get; }
}

/// <summary>Contains counts grouped by resource role without exposing manifest paths.</summary>
public sealed record EpubLargePublicationResourceCounts(
    int Xhtml,
    int Css,
    int RasterImages,
    int Svg,
    int Fonts,
    int Audio,
    int Other,
    long XhtmlBytes)
{
    public int PresentClassCount => new[] { Xhtml, Css, RasterImages, Svg, Fonts, Audio, Other }
        .Count(static count => count > 0);
}

/// <summary>Contains feature evidence that structural inspection can establish safely.</summary>
public sealed record EpubLargePublicationFeatureEvidence(
    EpubPreflightFeatureStatus TableOfContents,
    EpubPreflightFeatureStatus Links,
    EpubPreflightFeatureStatus Images,
    EpubPreflightFeatureStatus Notes,
    EpubPreflightFeatureStatus Tables);

/// <summary>Contains one path-free and metadata-free preflight diagnostic.</summary>
public sealed record EpubLargePublicationPreflightDiagnostic(
    string Code,
    EpubLargePublicationPreflightSeverity Severity);

/// <summary>Contains stable preflight evidence for one explicit candidate.</summary>
public sealed record EpubLargePublicationCandidateResult
{
    public EpubLargePublicationCandidateResult(
        EpubCorpusPublicationId id,
        EpubLargePublicationCandidateStatus status,
        EpubCorpusSha256? sha256,
        EpubVersionFamily epubVersion,
        int manifestItemCount,
        int spineItemCount,
        int linearSpineItemCount,
        int nonLinearSpineItemCount,
        long compressedBytes,
        long uncompressedBytes,
        EpubLargePublicationResourceCounts resources,
        EpubLargePublicationFeatureEvidence features,
        IEnumerable<EpubLargePublicationPreflightDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(diagnostics);
        Id = id;
        Status = status;
        Sha256 = sha256;
        EpubVersion = epubVersion;
        ManifestItemCount = manifestItemCount;
        SpineItemCount = spineItemCount;
        LinearSpineItemCount = linearSpineItemCount;
        NonLinearSpineItemCount = nonLinearSpineItemCount;
        CompressedBytes = compressedBytes;
        UncompressedBytes = uncompressedBytes;
        Resources = resources;
        Features = features;
        Diagnostics = diagnostics
            .Distinct()
            .OrderBy(static item => item.Code, StringComparer.Ordinal)
            .ThenBy(static item => item.Severity)
            .ToImmutableArray();
    }

    public EpubCorpusPublicationId Id { get; }

    public EpubLargePublicationCandidateStatus Status { get; }

    public EpubCorpusSha256? Sha256 { get; }

    public EpubVersionFamily EpubVersion { get; }

    public int ManifestItemCount { get; }

    public int SpineItemCount { get; }

    public int LinearSpineItemCount { get; }

    public int NonLinearSpineItemCount { get; }

    public long CompressedBytes { get; }

    public long UncompressedBytes { get; }

    public EpubLargePublicationResourceCounts Resources { get; }

    public EpubLargePublicationFeatureEvidence Features { get; }

    public ImmutableArray<EpubLargePublicationPreflightDiagnostic> Diagnostics { get; }
}

/// <summary>Contains deterministic selection evidence for explicitly supplied local candidates.</summary>
public sealed record EpubLargePublicationPreflightReport
{
    public const string CurrentFormat = "flow-epub-large-preflight-0.1";

    public EpubLargePublicationPreflightReport(
        EpubLargePublicationSelectionStatus status,
        EpubCorpusPublicationId? selectedCandidateId,
        EpubLargePublicationPreflightCriteria criteria,
        IEnumerable<EpubLargePublicationCandidateResult> candidates,
        IEnumerable<EpubLargePublicationPreflightDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(diagnostics);
        Status = status;
        SelectedCandidateId = selectedCandidateId;
        Criteria = criteria;
        Candidates = candidates.OrderBy(static item => item.Id.Value, StringComparer.Ordinal).ToImmutableArray();
        Diagnostics = diagnostics
            .Distinct()
            .OrderBy(static item => item.Code, StringComparer.Ordinal)
            .ThenBy(static item => item.Severity)
            .ToImmutableArray();
    }

    public EpubLargePublicationSelectionStatus Status { get; }

    public EpubCorpusPublicationId? SelectedCandidateId { get; }

    public EpubLargePublicationPreflightCriteria Criteria { get; }

    public ImmutableArray<EpubLargePublicationCandidateResult> Candidates { get; }

    public ImmutableArray<EpubLargePublicationPreflightDiagnostic> Diagnostics { get; }
}

/// <summary>Inspects and selects from only the local EPUB candidates supplied by the caller.</summary>
public interface IEpubLargePublicationPreflightService
{
    public Task<EpubLargePublicationPreflightReport> EvaluateAsync(
        IEnumerable<EpubLargePublicationCandidate> candidates,
        string repositoryRoot,
        EpubLargePublicationPreflightCriteria? criteria = null,
        CancellationToken cancellationToken = default);
}
