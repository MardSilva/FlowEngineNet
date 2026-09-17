using System.Collections.Immutable;
using Flow.Core;
using Flow.Rendering.Html;

namespace Flow.Epub.Corpus;

/// <summary>Classifies a decision that must be recorded by a human reviewer.</summary>
public enum EpubLargePublicationReviewStatus
{
    Approved,
    Rejected,
    NotApplicable,
    Inconclusive,
}

/// <summary>Identifies the semantic position of a sampled chapter.</summary>
public enum EpubLargePublicationSamplePosition
{
    Beginning,
    Middle,
    End,
}

/// <summary>Defines one content-free chapter sample used by the local review material.</summary>
public sealed record EpubLargePublicationReviewSample(
    EpubLargePublicationSamplePosition Position,
    NodeId ChapterId,
    string MobileTarget,
    string DesktopTarget);

/// <summary>Defines one content-free shortcut to a representative semantic construct.</summary>
public sealed record EpubLargePublicationReviewTarget(
    string Category,
    NodeId? NodeId,
    string MobileTarget,
    string DesktopTarget);

/// <summary>Defines one explicit human-review decision without treating it as an automatic result.</summary>
public sealed record EpubLargePublicationReviewItem
{
    public EpubLargePublicationReviewItem(string id, EpubLargePublicationReviewStatus status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (id.Length > 96 || id.Any(static character => character is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-')))
        {
            throw new ArgumentException(
                "Review item IDs must contain lowercase ASCII letters, digits, dots, or hyphens.",
                nameof(id));
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        Id = id;
        Status = status;
    }

    public string Id { get; }

    public EpubLargePublicationReviewStatus Status { get; }
}

/// <summary>Controls generation of review-only artifacts in one explicitly selected local directory.</summary>
public sealed record EpubLargePublicationReviewOptions
{
    public EpubLargePublicationReviewOptions(
        EpubCorpusPublicationId candidateId,
        EpubCorpusSha256 expectedSourceSha256,
        string outputDirectory,
        string repositoryRoot,
        HtmlBookUiLanguage uiLanguage = HtmlBookUiLanguage.Automatic)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        if (!Path.IsPathFullyQualified(outputDirectory))
        {
            throw new ArgumentException("The review output directory must be an explicit absolute path.", nameof(outputDirectory));
        }

        if (!Path.IsPathFullyQualified(repositoryRoot))
        {
            throw new ArgumentException("The repository root must be an absolute path.", nameof(repositoryRoot));
        }

        if (!Enum.IsDefined(uiLanguage))
        {
            throw new ArgumentOutOfRangeException(nameof(uiLanguage));
        }

        CandidateId = candidateId;
        ExpectedSourceSha256 = expectedSourceSha256;
        OutputDirectory = Path.GetFullPath(outputDirectory);
        RepositoryRoot = Path.GetFullPath(repositoryRoot);
        UiLanguage = uiLanguage;
    }

    public EpubCorpusPublicationId CandidateId { get; }

    public EpubCorpusSha256 ExpectedSourceSha256 { get; }

    /// <summary>Gets the local destination. It is never persisted inside the review manifest.</summary>
    public string OutputDirectory { get; }

    /// <summary>Gets the protected repository root. Review artifacts are rejected inside this tree.</summary>
    public string RepositoryRoot { get; }

    public HtmlBookUiLanguage UiLanguage { get; }
}

/// <summary>Returns local paths and content-free hashes after a successful transactional generation.</summary>
public sealed record EpubLargePublicationReviewResult
{
    public EpubLargePublicationReviewResult(
        string outputDirectory,
        EpubCorpusSha256 sourceEpubSha256,
        EpubCorpusSha256 canonicalDocumentSha256,
        EpubCorpusSha256 mobilePackageSha256,
        EpubCorpusSha256 desktopPackageSha256,
        IEnumerable<EpubLargePublicationReviewSample> samples,
        IEnumerable<EpubLargePublicationReviewTarget> targets)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(targets);
        OutputDirectory = Path.GetFullPath(outputDirectory);
        SourceEpubSha256 = sourceEpubSha256;
        CanonicalDocumentSha256 = canonicalDocumentSha256;
        MobilePackageSha256 = mobilePackageSha256;
        DesktopPackageSha256 = desktopPackageSha256;
        Samples = samples.OrderBy(static item => item.Position).ToImmutableArray();
        Targets = targets.OrderBy(static item => item.Category, StringComparer.Ordinal).ToImmutableArray();
    }

    public string OutputDirectory { get; }

    public EpubCorpusSha256 SourceEpubSha256 { get; }

    public EpubCorpusSha256 CanonicalDocumentSha256 { get; }

    public EpubCorpusSha256 MobilePackageSha256 { get; }

    public EpubCorpusSha256 DesktopPackageSha256 { get; }

    public ImmutableArray<EpubLargePublicationReviewSample> Samples { get; }

    public ImmutableArray<EpubLargePublicationReviewTarget> Targets { get; }
}

/// <summary>Produces local, review-only artifacts without assigning human outcomes automatically.</summary>
public interface IEpubLargePublicationReviewPackageGenerator
{
    public Task<EpubLargePublicationReviewResult> GenerateAsync(
        EpubLargePublicationCandidate candidate,
        EpubLargePublicationReviewOptions options,
        CancellationToken cancellationToken = default);
}
