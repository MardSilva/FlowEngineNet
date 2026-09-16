using System.Collections.Immutable;
using Flow.Epub;

namespace Flow.Epub.Corpus;

/// <summary>Relates catalog expectations, Flow fidelity, and optional EPUBCheck evidence.</summary>
public sealed record EpubCorpusQualificationEntry(
    EpubCorpusPublicationId Id,
    EpubCorpusExecutionStatus FlowStatus,
    long FidelityLostUnitCount,
    EpubCheckEvidenceStatus EpubCheckStatus,
    EpubCorpusEvidenceRelationship EvidenceRelationship,
    int ExpectationFailureCount);

/// <summary>Contains repeated-execution evidence and an optional accepted-baseline comparison.</summary>
public sealed record EpubCorpusQualificationResult
{
    public EpubCorpusQualificationResult(
        EpubCorpusExecutionReport report,
        EpubCorpusBaseline observedBaseline,
        bool isDeterministic,
        EpubCorpusBaselineComparison? acceptedBaselineComparison,
        IEnumerable<EpubCorpusQualificationEntry> publications)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(observedBaseline);
        ArgumentNullException.ThrowIfNull(publications);
        Report = report;
        ObservedBaseline = observedBaseline;
        IsDeterministic = isDeterministic;
        AcceptedBaselineComparison = acceptedBaselineComparison;
        Publications = publications.OrderBy(static item => item.Id.Value, StringComparer.Ordinal).ToImmutableArray();
    }

    public EpubCorpusExecutionReport Report { get; }

    public EpubCorpusBaseline ObservedBaseline { get; }

    public bool IsDeterministic { get; }

    public EpubCorpusBaselineComparison? AcceptedBaselineComparison { get; }

    public ImmutableArray<EpubCorpusQualificationEntry> Publications { get; }
}

/// <summary>Runs a corpus twice and qualifies stable evidence without changing EPUB or Flow bytes.</summary>
public sealed class EpubCorpusQualificationService
{
    private readonly IEpubCorpusExecutor executor;

    public EpubCorpusQualificationService(IEpubCorpusExecutor? executor = null)
    {
        this.executor = executor ?? new EpubCorpusExecutor();
    }

    public async Task<EpubCorpusQualificationResult> QualifyAsync(
        EpubCorpusManifest manifest,
        EpubCorpusDiscoveryOptions discoveryOptions,
        EpubCorpusBaseline? acceptedBaseline = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(discoveryOptions);
        var first = await executor.ExecuteAsync(manifest, discoveryOptions, cancellationToken).ConfigureAwait(false);
        var second = await executor.ExecuteAsync(manifest, discoveryOptions, cancellationToken).ConfigureAwait(false);
        var firstBaseline = EpubCorpusBaseline.FromReport(first);
        var secondBaseline = EpubCorpusBaseline.FromReport(second);
        var deterministic = EpubCorpusBaselineComparer.Compare(firstBaseline, secondBaseline).IsMatch;
        var comparison = acceptedBaseline is null
            ? null
            : EpubCorpusBaselineComparer.Compare(acceptedBaseline, firstBaseline);
        var entries = first.Publications.Select(static result => new EpubCorpusQualificationEntry(
            result.Id,
            result.Status,
            result.Evidence.FidelityLostUnitCount,
            result.EpubCheckEvidence?.Status ?? EpubCheckEvidenceStatus.Disabled,
            result.EvidenceRelationship,
            result.Diagnostics.Count(static diagnostic =>
                diagnostic.Code == EpubCorpusExecutionDiagnosticCodes.ExpectationFailed)));
        return new EpubCorpusQualificationResult(first, firstBaseline, deterministic, comparison, entries);
    }

    public static Task WriteLocalDetailedReportAsync(
        EpubCorpusQualificationResult result,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        return EpubCorpusExecutionReportJsonSerializer.WriteAtomicallyAsync(
            result.Report,
            outputPath,
            includeNonDeterministicEnvironment: true,
            cancellationToken);
    }
}
