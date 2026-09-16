using System.Collections.Immutable;
using Flow.Epub;

namespace Flow.Epub.Corpus;

/// <summary>Identifies one planned phase of the large-publication gate.</summary>
public enum EpubLargePublicationGatePhaseKind
{
    Preflight,
    Inspection,
    Import,
    Fidelity,
    Validation,
    Serialization,
    Integrity,
    MobileLayout,
    MobileHtmlPackage,
    DesktopLayout,
    DesktopHtmlPackage,
    StructuralAudit,
    DeterminismComparison,
    HumanReview,
}

/// <summary>Classifies the outcome of a gate, phase, or check.</summary>
public enum EpubLargePublicationGateStatus
{
    NotStarted,
    Passed,
    PassedWithWarnings,
    Failed,
    Skipped,
    Inconclusive,
}

/// <summary>Classifies whether a check is automatic or requires a human decision.</summary>
public enum EpubLargePublicationGateCheckKind
{
    Automatic,
    HumanReview,
}

/// <summary>Classifies the effect of a stable gate diagnostic.</summary>
public enum EpubLargePublicationGateDiagnosticSeverity
{
    Information,
    Warning,
    Error,
}

/// <summary>Defines the reproducible inputs that control one gate execution.</summary>
public sealed record EpubLargePublicationGateOptions
{
    public EpubLargePublicationGateOptions(
        EpubCorpusPublicationId candidateId,
        EpubCorpusSha256 expectedSourceSha256,
        int repetitionCount = 2,
        bool requireHumanReview = true)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(repetitionCount, 2);
        CandidateId = candidateId;
        ExpectedSourceSha256 = expectedSourceSha256;
        RepetitionCount = repetitionCount;
        RequireHumanReview = requireHumanReview;
    }

    public EpubCorpusPublicationId CandidateId { get; }

    public EpubCorpusSha256 ExpectedSourceSha256 { get; }

    public int RepetitionCount { get; }

    public bool RequireHumanReview { get; }
}

/// <summary>Records the explicit outcome of one planned gate phase.</summary>
public sealed record EpubLargePublicationGatePhase
{
    public EpubLargePublicationGatePhase(
        EpubLargePublicationGatePhaseKind kind,
        EpubLargePublicationGateStatus status)
    {
        Validate(kind, status);
        Kind = kind;
        Status = status;
    }

    public EpubLargePublicationGatePhaseKind Kind { get; }

    public EpubLargePublicationGateStatus Status { get; }

    private static void Validate(
        EpubLargePublicationGatePhaseKind kind,
        EpubLargePublicationGateStatus status)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }
    }
}

/// <summary>Records one stable, content-free gate assertion.</summary>
public sealed record EpubLargePublicationGateCheck
{
    public EpubLargePublicationGateCheck(
        string id,
        EpubLargePublicationGatePhaseKind phase,
        EpubLargePublicationGateCheckKind kind,
        EpubLargePublicationGateStatus status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (!IsStableIdentifier(id))
        {
            throw new ArgumentException(
                "Gate check IDs must contain lowercase ASCII letters, digits, dots, or hyphens.",
                nameof(id));
        }

        if (!Enum.IsDefined(phase))
        {
            throw new ArgumentOutOfRangeException(nameof(phase));
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        Id = id;
        Phase = phase;
        Kind = kind;
        Status = status;
    }

    public string Id { get; }

    public EpubLargePublicationGatePhaseKind Phase { get; }

    public EpubLargePublicationGateCheckKind Kind { get; }

    public EpubLargePublicationGateStatus Status { get; }

    private static bool IsStableIdentifier(string value) => value.Length <= 96
        && value.All(static character => character is >= 'a' and <= 'z'
            or >= '0' and <= '9'
            or '.'
            or '-');
}

/// <summary>Locates one stable, path-free gate finding.</summary>
public sealed record EpubLargePublicationGateDiagnostic
{
    public EpubLargePublicationGateDiagnostic(
        string code,
        EpubLargePublicationGateDiagnosticSeverity severity,
        EpubLargePublicationGatePhaseKind phase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        if (code.Length > 32 || code.Any(static character => character is not (>= 'A' and <= 'Z' or >= '0' and <= '9')))
        {
            throw new ArgumentException("Gate diagnostic codes must contain uppercase ASCII letters or digits.", nameof(code));
        }

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
    }

    public string Code { get; }

    public EpubLargePublicationGateDiagnosticSeverity Severity { get; }

    public EpubLargePublicationGatePhaseKind Phase { get; }
}

/// <summary>Contains stable evidence suitable for deterministic gate comparison.</summary>
public sealed record EpubLargePublicationGateEvidence(
    EpubVersionFamily? EpubVersion = null,
    long SourceEpubBytes = 0,
    int ManifestItemCount = 0,
    int SpineItemCount = 0,
    int ChapterCount = 0,
    int ImportedNodeCount = 0,
    int ImportedAssetCount = 0,
    long ImportedCharacterCount = 0,
    int ValidationDiagnosticCount = 0,
    long FidelitySourceUnitCount = 0,
    long FidelityLostUnitCount = 0,
    int FlowJsonBytes = 0,
    EpubCorpusSha256? CanonicalHash = null,
    EpubCorpusSha256? ReadingOrderHash = null,
    EpubCorpusSha256? AnchorSetHash = null,
    int MobileLayoutNodeCount = 0,
    int MobileHtmlFileCount = 0,
    long MobileHtmlBytes = 0,
    int DesktopLayoutNodeCount = 0,
    int DesktopHtmlFileCount = 0,
    long DesktopHtmlBytes = 0);

/// <summary>Contains an approximate observation for one phase in one environment.</summary>
public sealed record EpubLargePublicationGatePhaseObservation(
    EpubLargePublicationGatePhaseKind Phase,
    long DurationTicks,
    long ApproximateManagedBytes,
    long ApproximateWorkingSetBytes);

/// <summary>Contains non-deterministic runtime observations excluded from stable comparison.</summary>
public sealed record EpubLargePublicationGateEnvironmentObservations
{
    public EpubLargePublicationGateEnvironmentObservations(
        long totalDurationTicks,
        long approximatePeakManagedBytes,
        long approximatePeakWorkingSetBytes,
        IEnumerable<EpubLargePublicationGatePhaseObservation>? phases = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalDurationTicks);
        ArgumentOutOfRangeException.ThrowIfNegative(approximatePeakManagedBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(approximatePeakWorkingSetBytes);
        TotalDurationTicks = totalDurationTicks;
        ApproximatePeakManagedBytes = approximatePeakManagedBytes;
        ApproximatePeakWorkingSetBytes = approximatePeakWorkingSetBytes;
        Phases = (phases ?? [])
            .OrderBy(static item => item.Phase)
            .ToImmutableArray();
    }

    public long TotalDurationTicks { get; }

    public long ApproximatePeakManagedBytes { get; }

    public long ApproximatePeakWorkingSetBytes { get; }

    public ImmutableArray<EpubLargePublicationGatePhaseObservation> Phases { get; }
}

/// <summary>Contains one complete gate result with every planned phase represented.</summary>
public sealed record EpubLargePublicationGateResult
{
    public EpubLargePublicationGateResult(
        EpubLargePublicationGateStatus status,
        EpubLargePublicationGateEvidence evidence,
        IEnumerable<EpubLargePublicationGatePhase> phases,
        IEnumerable<EpubLargePublicationGateCheck> automaticChecks,
        IEnumerable<EpubLargePublicationGateCheck> humanReviewItems,
        IEnumerable<EpubLargePublicationGateDiagnostic> diagnostics,
        EpubLargePublicationGateEnvironmentObservations? environmentObservations = null)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(phases);
        ArgumentNullException.ThrowIfNull(automaticChecks);
        ArgumentNullException.ThrowIfNull(humanReviewItems);
        ArgumentNullException.ThrowIfNull(diagnostics);
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        var suppliedPhases = phases.ToImmutableArray();
        if (suppliedPhases.GroupBy(static item => item.Kind).Any(static group => group.Count() > 1))
        {
            throw new ArgumentException("Each gate phase can be supplied only once.", nameof(phases));
        }

        Status = status;
        Evidence = evidence;
        Phases = Enum.GetValues<EpubLargePublicationGatePhaseKind>()
            .Select(kind => suppliedPhases.FirstOrDefault(item => item.Kind == kind)
                ?? new EpubLargePublicationGatePhase(kind, EpubLargePublicationGateStatus.NotStarted))
            .ToImmutableArray();
        AutomaticChecks = NormalizeChecks(automaticChecks, EpubLargePublicationGateCheckKind.Automatic, nameof(automaticChecks));
        HumanReviewItems = NormalizeChecks(humanReviewItems, EpubLargePublicationGateCheckKind.HumanReview, nameof(humanReviewItems));
        if (AutomaticChecks.Select(static item => item.Id)
            .Intersect(HumanReviewItems.Select(static item => item.Id), StringComparer.Ordinal)
            .Any())
        {
            throw new ArgumentException("Gate check IDs must be unique across automatic and human checks.");
        }

        Diagnostics = diagnostics
            .Distinct()
            .OrderBy(static item => item.Phase)
            .ThenBy(static item => item.Code, StringComparer.Ordinal)
            .ThenBy(static item => item.Severity)
            .ToImmutableArray();
        EnvironmentObservations = environmentObservations;
    }

    public EpubLargePublicationGateStatus Status { get; }

    public EpubLargePublicationGateEvidence Evidence { get; }

    public ImmutableArray<EpubLargePublicationGatePhase> Phases { get; }

    public ImmutableArray<EpubLargePublicationGateCheck> AutomaticChecks { get; }

    public ImmutableArray<EpubLargePublicationGateCheck> HumanReviewItems { get; }

    public ImmutableArray<EpubLargePublicationGateDiagnostic> Diagnostics { get; }

    public EpubLargePublicationGateEnvironmentObservations? EnvironmentObservations { get; }

    private static ImmutableArray<EpubLargePublicationGateCheck> NormalizeChecks(
        IEnumerable<EpubLargePublicationGateCheck> checks,
        EpubLargePublicationGateCheckKind expectedKind,
        string parameterName)
    {
        var result = checks.OrderBy(static item => item.Id, StringComparer.Ordinal).ToImmutableArray();
        if (result.Any(item => item.Kind != expectedKind))
        {
            throw new ArgumentException($"Every check must have kind '{expectedKind}'.", parameterName);
        }

        if (result.GroupBy(static item => item.Id, StringComparer.Ordinal).Any(static group => group.Count() > 1))
        {
            throw new ArgumentException("Gate check IDs must be unique.", parameterName);
        }

        return result;
    }
}

/// <summary>Relates reproducible gate options to one complete result.</summary>
public sealed record EpubLargePublicationGateReport
{
    public const string CurrentFormat = "flow-epub-large-publication-gate-0.1";

    public EpubLargePublicationGateReport(
        EpubLargePublicationGateOptions options,
        EpubLargePublicationGateResult result)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(result);
        Options = options;
        Result = result;
    }

    public EpubLargePublicationGateOptions Options { get; }

    public EpubLargePublicationGateResult Result { get; }
}

/// <summary>Executes the large-publication gate for one explicitly supplied local candidate.</summary>
public interface IEpubLargePublicationGate
{
    public Task<EpubLargePublicationGateReport> ExecuteAsync(
        EpubLargePublicationCandidate candidate,
        EpubLargePublicationGateOptions options,
        CancellationToken cancellationToken = default);
}
