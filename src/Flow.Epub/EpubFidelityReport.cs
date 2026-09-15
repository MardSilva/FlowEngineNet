using System.Collections.Immutable;
using Flow.Core;

namespace Flow.Epub;

/// <summary>Classifies how one source unit survived EPUB-to-Flow conversion.</summary>
public enum EpubFidelityStatus
{
    Preserved,
    Transformed,
    Approximated,
    Unsupported,
    Lost,
}

/// <summary>Classifies the practical effect of a fidelity finding.</summary>
public enum EpubFidelityImpact
{
    Informational,
    Minor,
    Moderate,
    Major,
    Critical,
}

/// <summary>Identifies a source/destination quantity measured by the fidelity analyzer.</summary>
public enum EpubFidelityMetric
{
    LinearSpineItems,
    NonLinearSpineItems,
    SignificantCharacters,
    Headings,
    Paragraphs,
    InternalLinks,
    ExternalLinks,
    Images,
    Covers,
    Notes,
    NoteReferences,
    Tables,
    TableRows,
    TableCells,
    TableOfContentsEntries,
    ManifestResources,
    UnknownOrUnrepresentableElements,
}

/// <summary>Measures source units and their conversion outcomes without using rendered geometry.</summary>
public sealed record EpubFidelityMeasurement
{
    public EpubFidelityMeasurement(
        EpubFidelityMetric metric,
        long sourceCount,
        long destinationCount,
        long preservedCount,
        long transformedCount,
        long approximatedCount,
        long unsupportedCount,
        long lostCount)
    {
        if (!Enum.IsDefined(metric))
        {
            throw new ArgumentOutOfRangeException(nameof(metric));
        }

        var counts = new[]
        {
            sourceCount,
            destinationCount,
            preservedCount,
            transformedCount,
            approximatedCount,
            unsupportedCount,
            lostCount,
        };
        if (counts.Any(static value => value < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceCount), "Fidelity counts cannot be negative.");
        }

        if (preservedCount + transformedCount + approximatedCount + unsupportedCount + lostCount != sourceCount)
        {
            throw new ArgumentException("Every source unit must have exactly one fidelity status.");
        }

        Metric = metric;
        SourceCount = sourceCount;
        DestinationCount = destinationCount;
        PreservedCount = preservedCount;
        TransformedCount = transformedCount;
        ApproximatedCount = approximatedCount;
        UnsupportedCount = unsupportedCount;
        LostCount = lostCount;
    }

    public EpubFidelityMetric Metric { get; }

    public long SourceCount { get; }

    public long DestinationCount { get; }

    public long PreservedCount { get; }

    public long TransformedCount { get; }

    public long ApproximatedCount { get; }

    public long UnsupportedCount { get; }

    public long LostCount { get; }

    /// <summary>
    /// Gets (preserved + transformed) / source * 100, or <see langword="null" /> when no source unit exists.
    /// Approximated, unsupported and lost units deliberately receive no preservation credit.
    /// </summary>
    public decimal? PreservationPercentage => SourceCount == 0
        ? null
        : decimal.Round(
            (PreservedCount + TransformedCount) * 100m / SourceCount,
            6,
            MidpointRounding.ToZero);
}

/// <summary>Locates and explains one aggregated fidelity outcome.</summary>
public sealed record EpubFidelityFinding
{
    public EpubFidelityFinding(
        string code,
        EpubFidelityStatus status,
        EpubFidelityImpact impact,
        string explanation,
        int count = 1,
        string? resourcePath = null,
        string? fragment = null,
        NodeId? nodeId = null,
        AssetId? assetId = null,
        string? relatedDiagnosticCode = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(explanation);
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        if (!Enum.IsDefined(impact))
        {
            throw new ArgumentOutOfRangeException(nameof(impact));
        }

        if (count < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        Code = code;
        Status = status;
        Impact = impact;
        Explanation = explanation;
        Count = count;
        ResourcePath = resourcePath;
        Fragment = fragment;
        NodeId = nodeId;
        AssetId = assetId;
        RelatedDiagnosticCode = relatedDiagnosticCode;
    }

    public string Code { get; }

    public EpubFidelityStatus Status { get; }

    public EpubFidelityImpact Impact { get; }

    public string Explanation { get; }

    public int Count { get; }

    public string? ResourcePath { get; }

    public string? Fragment { get; }

    public NodeId? NodeId { get; }

    public AssetId? AssetId { get; }

    public string? RelatedDiagnosticCode { get; }
}

/// <summary>Summarizes mutually exclusive source-unit outcomes.</summary>
public sealed record EpubFidelitySummary(
    long SourceUnitCount,
    long PreservedCount,
    long TransformedCount,
    long ApproximatedCount,
    long UnsupportedCount,
    long LostCount,
    decimal? PreservationPercentage,
    bool IsPartial);

/// <summary>Provides deterministic, noncanonical evidence about one EPUB conversion.</summary>
public sealed record EpubFidelityReport
{
    public const string Format = "flow-epub-fidelity-0.1";

    public EpubFidelityReport(
        bool importSucceeded,
        EpubFidelitySummary summary,
        IEnumerable<EpubFidelityMeasurement> measurements,
        IEnumerable<EpubFidelityFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(measurements);
        ArgumentNullException.ThrowIfNull(findings);
        ImportSucceeded = importSucceeded;
        Summary = summary;
        Measurements = measurements.OrderBy(static item => item.Metric).ToImmutableArray();
        Findings = findings
            .OrderBy(static item => item.Code, StringComparer.Ordinal)
            .ThenBy(static item => item.ResourcePath, StringComparer.Ordinal)
            .ThenBy(static item => item.Fragment, StringComparer.Ordinal)
            .ThenBy(static item => item.NodeId?.Value, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    public bool ImportSucceeded { get; }

    public EpubFidelitySummary Summary { get; }

    public ImmutableArray<EpubFidelityMeasurement> Measurements { get; }

    public ImmutableArray<EpubFidelityFinding> Findings { get; }
}

internal sealed record EpubFidelitySourceCount(
    EpubFidelityMetric Metric,
    string? ResourcePath,
    long Count,
    EpubFidelityStatus? ForcedStatus = null);

internal sealed record EpubFidelitySourceSnapshot(IEnumerable<EpubFidelitySourceCount> Counts)
{
    internal ImmutableArray<EpubFidelitySourceCount> Items { get; } = Counts.ToImmutableArray();
}
