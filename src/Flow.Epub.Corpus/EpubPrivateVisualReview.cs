using System.Collections.Immutable;
using Flow.Epub;
using Flow.Rendering.Html;

namespace Flow.Epub.Corpus;

/// <summary>Classifies generation of one private assisted-review package.</summary>
public enum EpubPrivateVisualReviewStatus
{
    Generated,
    MissingSource,
    SkippedQualification,
    Failed,
}

/// <summary>Contains content-free evidence for one local review package.</summary>
public sealed record EpubPrivateVisualReviewItem
{
    public EpubPrivateVisualReviewItem(
        EpubCorpusPublicationId id,
        EpubPrivateVisualReviewStatus status,
        EpubCorpusSha256? sourceSha256,
        EpubCorpusSha256? canonicalDocumentSha256,
        string? relativeDirectory,
        IEnumerable<string>? samples = null,
        IEnumerable<string>? targets = null,
        string? failureCode = null)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        if (relativeDirectory is not null
            && (!string.Equals(relativeDirectory, id.Value, StringComparison.Ordinal)
                || relativeDirectory.Contains(Path.DirectorySeparatorChar)
                || relativeDirectory.Contains(Path.AltDirectorySeparatorChar)))
        {
            throw new ArgumentException("The review directory must be the neutral candidate ID.", nameof(relativeDirectory));
        }

        Id = id;
        Status = status;
        SourceSha256 = sourceSha256;
        CanonicalDocumentSha256 = canonicalDocumentSha256;
        RelativeDirectory = relativeDirectory;
        Samples = (samples ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();
        Targets = (targets ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();
        FailureCode = failureCode;
    }

    public EpubCorpusPublicationId Id { get; }

    public EpubPrivateVisualReviewStatus Status { get; }

    public EpubCorpusSha256? SourceSha256 { get; }

    public EpubCorpusSha256? CanonicalDocumentSha256 { get; }

    public string? RelativeDirectory { get; }

    public ImmutableArray<string> Samples { get; }

    public ImmutableArray<string> Targets { get; }

    public string? FailureCode { get; }
}

/// <summary>Summarizes corpus-wide assisted-review package generation.</summary>
public sealed record EpubPrivateVisualReviewSummary(int Total, int Generated, int Missing, int Skipped, int Failed);

/// <summary>Contains deterministic, path-free evidence for local visual-review material.</summary>
public sealed record EpubPrivateVisualReviewReport
{
    public const string CurrentFormat = "flow-epub-private-visual-review-0.1";

    public EpubPrivateVisualReviewReport(
        EpubCorpusSha256 qualificationReportSha256,
        IEnumerable<EpubPrivateVisualReviewItem> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        QualificationReportSha256 = qualificationReportSha256;
        Candidates = candidates.OrderBy(static item => item.Id.Value, StringComparer.Ordinal).ToImmutableArray();
        Summary = new EpubPrivateVisualReviewSummary(
            Candidates.Length,
            Candidates.Count(static item => item.Status == EpubPrivateVisualReviewStatus.Generated),
            Candidates.Count(static item => item.Status == EpubPrivateVisualReviewStatus.MissingSource),
            Candidates.Count(static item => item.Status == EpubPrivateVisualReviewStatus.SkippedQualification),
            Candidates.Count(static item => item.Status == EpubPrivateVisualReviewStatus.Failed));
    }

    public EpubCorpusSha256 QualificationReportSha256 { get; }

    public EpubPrivateVisualReviewSummary Summary { get; }

    public ImmutableArray<EpubPrivateVisualReviewItem> Candidates { get; }
}

/// <summary>Configures bounded generation of private review packages.</summary>
public sealed record EpubPrivateVisualReviewOptions
{
    public EpubPrivateVisualReviewOptions(
        string sourceDirectory,
        string outputDirectory,
        string repositoryRoot,
        bool legalUseDeclared,
        bool drmFreeDeclared,
        HtmlBookUiLanguage uiLanguage = HtmlBookUiLanguage.Automatic,
        int maximumCandidateFiles = 4_096,
        int maximumRecursionDepth = 32,
        long maximumCandidateBytes = 128 * 1024 * 1024)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        if (!Path.IsPathFullyQualified(sourceDirectory)
            || !Path.IsPathFullyQualified(outputDirectory)
            || !Path.IsPathFullyQualified(repositoryRoot))
        {
            throw new ArgumentException("Source, output, and repository paths must be absolute.");
        }

        if (!legalUseDeclared || !drmFreeDeclared)
        {
            throw new ArgumentException("Private visual review requires legal-use and DRM-free declarations.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCandidateFiles);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumRecursionDepth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCandidateBytes);
        if (!Enum.IsDefined(uiLanguage))
        {
            throw new ArgumentOutOfRangeException(nameof(uiLanguage));
        }

        SourceDirectory = Path.GetFullPath(sourceDirectory);
        OutputDirectory = Path.GetFullPath(outputDirectory);
        RepositoryRoot = Path.GetFullPath(repositoryRoot);
        LegalUseDeclared = legalUseDeclared;
        DrmFreeDeclared = drmFreeDeclared;
        UiLanguage = uiLanguage;
        MaximumCandidateFiles = maximumCandidateFiles;
        MaximumRecursionDepth = maximumRecursionDepth;
        MaximumCandidateBytes = maximumCandidateBytes;
    }

    public string SourceDirectory { get; }

    public string OutputDirectory { get; }

    public string RepositoryRoot { get; }

    public bool LegalUseDeclared { get; }

    public bool DrmFreeDeclared { get; }

    public HtmlBookUiLanguage UiLanguage { get; }

    public int MaximumCandidateFiles { get; }

    public int MaximumRecursionDepth { get; }

    public long MaximumCandidateBytes { get; }
}

/// <summary>Returns the local destination and its path-free deterministic report.</summary>
public sealed record EpubPrivateVisualReviewResult(string OutputDirectory, EpubPrivateVisualReviewReport Report);

/// <summary>Generates isolated review material for a qualified private corpus.</summary>
public interface IEpubPrivateVisualReviewService
{
    public Task<EpubPrivateVisualReviewResult> GenerateAsync(
        EpubPrivateQualificationReport qualification,
        EpubCorpusSha256 qualificationReportSha256,
        EpubPrivateVisualReviewOptions options,
        CancellationToken cancellationToken = default);
}
