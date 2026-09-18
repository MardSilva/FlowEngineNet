using System.Collections.Immutable;
using Flow.Epub;

namespace Flow.Epub.Corpus;

/// <summary>Classifies one automatic difference without implying an editorial decision.</summary>
public enum EpubPrivateDifferenceCategory
{
    Approved,
    ApprovedWithApproximations,
    UnsupportedContent,
    ContentLoss,
    BrokenSourceReference,
    FlowError,
    HumanReviewRequired,
}

/// <summary>Identifies the stable cause assigned to one difference.</summary>
public enum EpubPrivateDifferenceCause
{
    NoAutomaticDifference,
    SourceApproximation,
    EmbeddedFontSubstitution,
    UnsupportedFlowRepresentation,
    MeasuredFidelityLoss,
    InvalidSourceReference,
    FlowProcessingFailure,
    UnclassifiedEvidence,
    HumanDecisionPending,
}

/// <summary>Identifies the evidence area affected by one difference.</summary>
public enum EpubPrivateDifferenceMetric
{
    Inventory,
    Structure,
    Metadata,
    ReadingOrder,
    References,
    Assets,
    Typography,
    Notes,
    Tables,
    Fidelity,
    Serialization,
    Integrity,
    Layout,
    HtmlPackage,
    ExternalConformance,
    Determinism,
    Unknown,
}

/// <summary>Contains one deterministic and path-free classified difference.</summary>
public sealed record EpubPrivateDifference
{
    public EpubPrivateDifference(
        string code,
        EpubPrivateDifferenceCategory category,
        EpubPrivateDifferenceCause cause,
        EpubCorpusExecutionDiagnosticSeverity severity,
        int count,
        EpubPrivateDifferenceMetric metric,
        EpubCorpusExecutionPhase? phase = null,
        string? neutralLocation = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        if (!Enum.IsDefined(category) || !Enum.IsDefined(cause) || !Enum.IsDefined(severity) || !Enum.IsDefined(metric))
        {
            throw new ArgumentOutOfRangeException(nameof(category), "Difference enum values must be defined.");
        }

        if (!string.IsNullOrWhiteSpace(neutralLocation)
            && (Path.IsPathFullyQualified(neutralLocation) || Uri.TryCreate(neutralLocation, UriKind.Absolute, out _)))
        {
            throw new ArgumentException("A difference location must be neutral and relative.", nameof(neutralLocation));
        }

        Code = code;
        Category = category;
        Cause = cause;
        Severity = severity;
        Count = count;
        Metric = metric;
        Phase = phase;
        NeutralLocation = string.IsNullOrWhiteSpace(neutralLocation) ? null : neutralLocation.Replace('\\', '/');
    }

    public string Code { get; }
    public EpubPrivateDifferenceCategory Category { get; }
    public EpubPrivateDifferenceCause Cause { get; }
    public EpubCorpusExecutionDiagnosticSeverity Severity { get; }
    public int Count { get; }
    public EpubPrivateDifferenceMetric Metric { get; }
    public EpubCorpusExecutionPhase? Phase { get; }
    public string? NeutralLocation { get; }
}

/// <summary>Contains the classification for one neutral private candidate.</summary>
public sealed record EpubPrivateDifferenceCandidate
{
    public EpubPrivateDifferenceCandidate(
        EpubCorpusPublicationId id,
        EpubCorpusSha256? sourceSha256,
        EpubPrivateQualificationStatus qualificationStatus,
        EpubPrivateDifferenceCategory overallCategory,
        IEnumerable<EpubPrivateDifference> differences)
    {
        ArgumentNullException.ThrowIfNull(differences);
        Id = id;
        SourceSha256 = sourceSha256;
        QualificationStatus = qualificationStatus;
        OverallCategory = overallCategory;
        HumanReviewPending = true;
        Differences = differences
            .OrderByDescending(static item => EpubPrivateDifferenceMatrixService.Precedence(item.Category))
            .ThenBy(static item => item.Phase)
            .ThenBy(static item => item.Metric)
            .ThenBy(static item => item.Code, StringComparer.Ordinal)
            .ThenBy(static item => item.NeutralLocation, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    public EpubCorpusPublicationId Id { get; }
    public EpubCorpusSha256? SourceSha256 { get; }
    public EpubPrivateQualificationStatus QualificationStatus { get; }
    public EpubPrivateDifferenceCategory OverallCategory { get; }
    public bool HumanReviewPending { get; }
    public ImmutableArray<EpubPrivateDifference> Differences { get; }
}

/// <summary>Summarizes mutually exclusive candidate classifications and all difference rows.</summary>
public sealed record EpubPrivateDifferenceMatrixSummary(
    int TotalCandidates,
    int Approved,
    int ApprovedWithApproximations,
    int UnsupportedContent,
    int ContentLoss,
    int BrokenSourceReference,
    int FlowError,
    int HumanReviewRequired,
    int TotalDifferences);

/// <summary>Contains a deterministic classification bound to one exact qualification report.</summary>
public sealed record EpubPrivateDifferenceMatrix
{
    public const string CurrentFormat = "flow-epub-private-difference-matrix-0.1";

    public EpubPrivateDifferenceMatrix(
        EpubCorpusSha256 qualificationReportSha256,
        IEnumerable<EpubPrivateDifferenceCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        QualificationReportSha256 = qualificationReportSha256;
        Candidates = candidates.OrderBy(static item => item.Id.Value, StringComparer.Ordinal).ToImmutableArray();
        Summary = new EpubPrivateDifferenceMatrixSummary(
            Candidates.Length,
            Candidates.Count(static item => item.OverallCategory == EpubPrivateDifferenceCategory.Approved),
            Candidates.Count(static item => item.OverallCategory == EpubPrivateDifferenceCategory.ApprovedWithApproximations),
            Candidates.Count(static item => item.OverallCategory == EpubPrivateDifferenceCategory.UnsupportedContent),
            Candidates.Count(static item => item.OverallCategory == EpubPrivateDifferenceCategory.ContentLoss),
            Candidates.Count(static item => item.OverallCategory == EpubPrivateDifferenceCategory.BrokenSourceReference),
            Candidates.Count(static item => item.OverallCategory == EpubPrivateDifferenceCategory.FlowError),
            Candidates.Count(static item => item.OverallCategory == EpubPrivateDifferenceCategory.HumanReviewRequired),
            Candidates.Sum(static item => item.Differences.Length));
    }

    public EpubCorpusSha256 QualificationReportSha256 { get; }
    public EpubPrivateDifferenceMatrixSummary Summary { get; }
    public ImmutableArray<EpubPrivateDifferenceCandidate> Candidates { get; }
}

/// <summary>Projects existing private qualification evidence into a typed difference matrix.</summary>
public sealed class EpubPrivateDifferenceMatrixService
{
    private const string AutomaticApprovalCode = "EPM001";
    private const string FidelityLossCode = "EPM002";
    private const string MissingFailureEvidenceCode = "EPM003";
    private const string HumanReviewCode = "EPM004";
    private const string NondeterminismCode = "EPM005";

    public EpubPrivateDifferenceMatrix Create(
        EpubPrivateQualificationReport qualification,
        EpubCorpusSha256 qualificationReportSha256)
    {
        ArgumentNullException.ThrowIfNull(qualification);
        return new EpubPrivateDifferenceMatrix(
            qualificationReportSha256,
            qualification.Publications.Select(ClassifyCandidate));
    }

    internal static int Precedence(EpubPrivateDifferenceCategory category) => category switch
    {
        EpubPrivateDifferenceCategory.FlowError => 7,
        EpubPrivateDifferenceCategory.ContentLoss => 6,
        EpubPrivateDifferenceCategory.BrokenSourceReference => 5,
        EpubPrivateDifferenceCategory.UnsupportedContent => 4,
        EpubPrivateDifferenceCategory.HumanReviewRequired => 3,
        EpubPrivateDifferenceCategory.ApprovedWithApproximations => 2,
        _ => 1,
    };

    private static EpubPrivateDifferenceCandidate ClassifyCandidate(EpubPrivateQualificationItem publication)
    {
        var differences = publication.Diagnostics
            .GroupBy(static item => new { item.Code, item.Severity, item.Phase })
            .Select(static group => ClassifyDiagnostic(
                group.Key.Code,
                group.Key.Severity,
                group.Sum(static item => item.Count),
                group.Key.Phase))
            .ToList();

        if (publication.Evidence.FidelityLostUnitCount > 0)
        {
            differences.Add(new EpubPrivateDifference(
                FidelityLossCode,
                EpubPrivateDifferenceCategory.ContentLoss,
                EpubPrivateDifferenceCause.MeasuredFidelityLoss,
                EpubCorpusExecutionDiagnosticSeverity.Error,
                checked((int)Math.Min(publication.Evidence.FidelityLostUnitCount, int.MaxValue)),
                EpubPrivateDifferenceMetric.Fidelity,
                EpubCorpusExecutionPhase.Fidelity));
        }

        if (!publication.StableAcrossRepeatedRuns
            && publication.Status == EpubPrivateQualificationStatus.Nondeterministic)
        {
            differences.Add(new EpubPrivateDifference(
                NondeterminismCode,
                EpubPrivateDifferenceCategory.FlowError,
                EpubPrivateDifferenceCause.FlowProcessingFailure,
                EpubCorpusExecutionDiagnosticSeverity.Error,
                1,
                EpubPrivateDifferenceMetric.Determinism));
        }

        if (publication.Status == EpubPrivateQualificationStatus.Failed
            && differences.All(static item => item.Category is not EpubPrivateDifferenceCategory.FlowError
                and not EpubPrivateDifferenceCategory.ContentLoss
                and not EpubPrivateDifferenceCategory.BrokenSourceReference))
        {
            differences.Add(new EpubPrivateDifference(
                MissingFailureEvidenceCode,
                EpubPrivateDifferenceCategory.FlowError,
                EpubPrivateDifferenceCause.FlowProcessingFailure,
                EpubCorpusExecutionDiagnosticSeverity.Error,
                1,
                EpubPrivateDifferenceMetric.Unknown));
        }

        if (publication.Status is EpubPrivateQualificationStatus.Inconclusive
            or EpubPrivateQualificationStatus.SkippedProtected
            or EpubPrivateQualificationStatus.SkippedCorrupt
            or EpubPrivateQualificationStatus.SkippedUnsuitable)
        {
            differences.Add(new EpubPrivateDifference(
                HumanReviewCode,
                EpubPrivateDifferenceCategory.HumanReviewRequired,
                EpubPrivateDifferenceCause.HumanDecisionPending,
                EpubCorpusExecutionDiagnosticSeverity.Warning,
                1,
                EpubPrivateDifferenceMetric.Inventory));
        }

        if (differences.Count == 0)
        {
            differences.Add(new EpubPrivateDifference(
                AutomaticApprovalCode,
                EpubPrivateDifferenceCategory.Approved,
                EpubPrivateDifferenceCause.NoAutomaticDifference,
                EpubCorpusExecutionDiagnosticSeverity.Information,
                1,
                EpubPrivateDifferenceMetric.Fidelity));
        }

        var overall = differences.MaxBy(static item => Precedence(item.Category))!.Category;
        return new EpubPrivateDifferenceCandidate(
            publication.Id,
            publication.SourceSha256,
            publication.Status,
            overall,
            differences);
    }

    private static EpubPrivateDifference ClassifyDiagnostic(
        string code,
        EpubCorpusExecutionDiagnosticSeverity severity,
        int count,
        EpubCorpusExecutionPhase? phase)
    {
        var (category, cause) = CategoryFor(code, severity);
        return new EpubPrivateDifference(
            code,
            category,
            cause,
            severity,
            count,
            MetricFor(code, phase),
            phase);
    }

    private static (EpubPrivateDifferenceCategory Category, EpubPrivateDifferenceCause Cause) CategoryFor(
        string code,
        EpubCorpusExecutionDiagnosticSeverity severity)
    {
        if (code == EpubDiagnosticCodes.TransparentContainerTransformed)
        {
            return (EpubPrivateDifferenceCategory.Approved,
                EpubPrivateDifferenceCause.NoAutomaticDifference);
        }

        if (code is EpubDiagnosticCodes.UnsupportedElement
            or EpubDiagnosticCodes.UnsupportedManifestProperty
            or EpubDiagnosticCodes.HeadingLevelNormalized
            or EpubDiagnosticCodes.NoteResourceFallbackUsed
            or EpubDiagnosticCodes.UnsupportedCssProperty
            or EpubDiagnosticCodes.UnsupportedCssSelector
            or EpubDiagnosticCodes.CssTargetNotRepresentable
            or EpubDiagnosticCodes.SvgImageSemanticLoss
            or EpubDiagnosticCodes.MathSemanticLoss
            or EpubDiagnosticCodes.LegacyPageMapNotImported
            or EpubDiagnosticCodes.ArchivePathCaseMismatchRecovered
            or EpubDiagnosticCodes.CssImageResourceNotPreserved)
        {
            return (EpubPrivateDifferenceCategory.ApprovedWithApproximations,
                EpubPrivateDifferenceCause.SourceApproximation);
        }

        if (code is EpubDiagnosticCodes.EmbeddedFontBytesNotPreserved)
        {
            return (EpubPrivateDifferenceCategory.ApprovedWithApproximations,
                EpubPrivateDifferenceCause.EmbeddedFontSubstitution);
        }

        if (code is EpubDiagnosticCodes.UnsupportedResource
            or EpubDiagnosticCodes.UnsupportedImageFormat
            or EpubDiagnosticCodes.UnsupportedMediaOverlay
            or EpubDiagnosticCodes.LinkedImageTargetNotRepresentable
            or EpubDiagnosticCodes.UnsafeSvg
            or EpubPrivateInventoryDiagnosticCodes.UnsupportedEncryption
            or EpubPrivateInventoryDiagnosticCodes.UnsupportedResources)
        {
            return (EpubPrivateDifferenceCategory.UnsupportedContent,
                EpubPrivateDifferenceCause.UnsupportedFlowRepresentation);
        }

        if (code is EpubDiagnosticCodes.MissingResource
            or EpubDiagnosticCodes.InvalidReference
            or EpubDiagnosticCodes.MissingTableOfContentsTarget
            or EpubDiagnosticCodes.CircularTableOfContentsReference
            or EpubDiagnosticCodes.BrokenFallback
            or EpubDiagnosticCodes.DuplicateSourceId
            or EpubDiagnosticCodes.InvalidSourceId
            or EpubDiagnosticCodes.InvalidSvgImageReference
            or EpubDiagnosticCodes.OrphanNoteReference
            or EpubDiagnosticCodes.UnreferencedNote
            or EpubDiagnosticCodes.MissingNoteBacklink
            or EpubDiagnosticCodes.CircularNoteReference
            or EpubDiagnosticCodes.AmbiguousNoteDestination
            or EpubPrivateInventoryDiagnosticCodes.CandidateUnreadable
            or EpubPrivateInventoryDiagnosticCodes.InspectionFailed
            or EpubPrivateInventoryDiagnosticCodes.MissingReadingOrder)
        {
            return (EpubPrivateDifferenceCategory.BrokenSourceReference,
                EpubPrivateDifferenceCause.InvalidSourceReference);
        }

        if (code.StartsWith("FLOW_", StringComparison.Ordinal)
            || code is EpubCorpusExecutionDiagnosticCodes.ImportFailed
                or EpubCorpusExecutionDiagnosticCodes.ValidationFailed
                or EpubCorpusExecutionDiagnosticCodes.FidelityFailed
                or EpubCorpusExecutionDiagnosticCodes.RoundTripFailed
                or EpubCorpusExecutionDiagnosticCodes.IdentityMismatch
                or EpubCorpusExecutionDiagnosticCodes.CanonicalHashMismatch
                or EpubCorpusExecutionDiagnosticCodes.LayoutFailed
                or EpubCorpusExecutionDiagnosticCodes.HtmlPackageFailed
                or EpubCorpusExecutionDiagnosticCodes.ExpectationFailed
                or EpubCorpusExecutionDiagnosticCodes.ProcessingFailed)
        {
            return (EpubPrivateDifferenceCategory.FlowError, EpubPrivateDifferenceCause.FlowProcessingFailure);
        }

        if (!code.StartsWith("EPUB", StringComparison.Ordinal)
            && !code.StartsWith("EPI", StringComparison.Ordinal))
        {
            return (EpubPrivateDifferenceCategory.HumanReviewRequired,
                EpubPrivateDifferenceCause.UnclassifiedEvidence);
        }

        return severity == EpubCorpusExecutionDiagnosticSeverity.Error
            ? (EpubPrivateDifferenceCategory.ContentLoss, EpubPrivateDifferenceCause.MeasuredFidelityLoss)
            : (EpubPrivateDifferenceCategory.ApprovedWithApproximations,
                EpubPrivateDifferenceCause.SourceApproximation);
    }

    private static EpubPrivateDifferenceMetric MetricFor(string code, EpubCorpusExecutionPhase? phase)
    {
        if (phase is EpubCorpusExecutionPhase.Serialization) return EpubPrivateDifferenceMetric.Serialization;
        if (phase is EpubCorpusExecutionPhase.Integrity) return EpubPrivateDifferenceMetric.Integrity;
        if (phase is EpubCorpusExecutionPhase.MobileLayout or EpubCorpusExecutionPhase.DesktopLayout)
            return EpubPrivateDifferenceMetric.Layout;
        if (phase is EpubCorpusExecutionPhase.HtmlBookPackage) return EpubPrivateDifferenceMetric.HtmlPackage;
        if (phase is EpubCorpusExecutionPhase.ExternalConformance) return EpubPrivateDifferenceMetric.ExternalConformance;
        if (code is EpubDiagnosticCodes.HeadingLevelNormalized) return EpubPrivateDifferenceMetric.ReadingOrder;
        if (code is EpubDiagnosticCodes.NoteResourceFallbackUsed
            or EpubDiagnosticCodes.OrphanNoteReference
            or EpubDiagnosticCodes.UnreferencedNote
            or EpubDiagnosticCodes.MissingNoteBacklink
            or EpubDiagnosticCodes.CircularNoteReference
            or EpubDiagnosticCodes.AmbiguousNoteDestination) return EpubPrivateDifferenceMetric.Notes;
        if (code is EpubDiagnosticCodes.InvalidTableStructure
            or EpubDiagnosticCodes.InvalidTableSpan
            or EpubDiagnosticCodes.InvalidTableScope
            or EpubDiagnosticCodes.MissingTableHeader) return EpubPrivateDifferenceMetric.Tables;
        if (code is EpubDiagnosticCodes.InvalidStylesheet
            or EpubDiagnosticCodes.UnsupportedCssSelector
            or EpubDiagnosticCodes.UnsupportedCssProperty
            or EpubDiagnosticCodes.InvalidCssValue
            or EpubDiagnosticCodes.ExternalStylesheetBlocked
            or EpubDiagnosticCodes.CssTargetNotRepresentable
            or EpubDiagnosticCodes.EmbeddedFontBytesNotPreserved) return EpubPrivateDifferenceMetric.Typography;
        if (code is EpubDiagnosticCodes.InvalidReference
            or EpubDiagnosticCodes.MissingTableOfContentsTarget
            or EpubDiagnosticCodes.CircularTableOfContentsReference
            or EpubDiagnosticCodes.LinkedImageTargetNotRepresentable
            or EpubDiagnosticCodes.LegacyPageMapNotImported
            or EpubDiagnosticCodes.ArchivePathCaseMismatchRecovered) return EpubPrivateDifferenceMetric.References;
        if (code is EpubDiagnosticCodes.CssImageResourceNotPreserved)
            return EpubPrivateDifferenceMetric.Assets;
        if (code.StartsWith("EPI", StringComparison.Ordinal)) return EpubPrivateDifferenceMetric.Inventory;
        if (code is EpubDiagnosticCodes.InvalidMetadata
            or EpubDiagnosticCodes.OrphanMetadataRefinement
            or EpubDiagnosticCodes.MetadataConflict
            or EpubDiagnosticCodes.InvalidLanguage
            or EpubDiagnosticCodes.MissingIdentifier) return EpubPrivateDifferenceMetric.Metadata;
        if (code is EpubDiagnosticCodes.MissingResource
            or EpubDiagnosticCodes.UnsupportedResource
            or EpubDiagnosticCodes.UnsupportedMediaOverlay
            || IsEpubCodeBetween(code, 38, 46)
            || IsEpubCodeBetween(code, 62, 69)) return EpubPrivateDifferenceMetric.Assets;
        return phase == EpubCorpusExecutionPhase.Fidelity
            ? EpubPrivateDifferenceMetric.Fidelity
            : EpubPrivateDifferenceMetric.Structure;
    }

    private static bool IsEpubCodeBetween(string code, int minimum, int maximum) =>
        code.Length == 7
        && code.StartsWith("EPUB", StringComparison.Ordinal)
        && int.TryParse(code.AsSpan(4), System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var number)
        && number >= minimum
        && number <= maximum;
}
