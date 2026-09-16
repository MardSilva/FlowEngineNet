using System.Collections.Immutable;
using Flow.Epub;

namespace Flow.Epub.Corpus;

/// <summary>Contains reviewable, environment-independent expectations for a corpus execution.</summary>
public sealed record EpubCorpusBaseline
{
    public const string CurrentFormat = "flow-epub-corpus-baseline-0.1";

    public EpubCorpusBaseline(IEnumerable<EpubCorpusBaselineEntry> publications)
    {
        ArgumentNullException.ThrowIfNull(publications);
        Publications = publications.OrderBy(static item => item.Id.Value, StringComparer.Ordinal).ToImmutableArray();
    }

    public ImmutableArray<EpubCorpusBaselineEntry> Publications { get; }

    public static EpubCorpusBaseline FromReport(EpubCorpusExecutionReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return new EpubCorpusBaseline(report.Publications.Select(EpubCorpusBaselineEntry.FromResult));
    }
}

/// <summary>Stores only stable evidence that is useful when locating a regression.</summary>
public sealed record EpubCorpusBaselineEntry
{
    public EpubCorpusBaselineEntry(
        EpubCorpusPublicationId id,
        EpubCorpusExecutionStatus status,
        string? epubVersion,
        int manifestItemCount,
        int spineItemCount,
        int importedNodeCount,
        int importedAssetCount,
        int validationDiagnosticCount,
        long fidelityLostUnitCount,
        string? documentId,
        string? canonicalHash,
        EpubCorpusSemanticEvidence? semantic,
        IEnumerable<string> requiredDiagnosticCodes)
    {
        ArgumentNullException.ThrowIfNull(requiredDiagnosticCodes);
        Id = id;
        Status = status;
        EpubVersion = epubVersion;
        ManifestItemCount = manifestItemCount;
        SpineItemCount = spineItemCount;
        ImportedNodeCount = importedNodeCount;
        ImportedAssetCount = importedAssetCount;
        ValidationDiagnosticCount = validationDiagnosticCount;
        FidelityLostUnitCount = fidelityLostUnitCount;
        DocumentId = documentId;
        CanonicalHash = canonicalHash;
        Semantic = semantic;
        RequiredDiagnosticCodes = requiredDiagnosticCodes
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();
    }

    public EpubCorpusPublicationId Id { get; }

    public EpubCorpusExecutionStatus Status { get; }

    public string? EpubVersion { get; }

    public int ManifestItemCount { get; }

    public int SpineItemCount { get; }

    public int ImportedNodeCount { get; }

    public int ImportedAssetCount { get; }

    public int ValidationDiagnosticCount { get; }

    public long FidelityLostUnitCount { get; }

    public string? DocumentId { get; }

    public string? CanonicalHash { get; }

    public EpubCorpusSemanticEvidence? Semantic { get; }

    public ImmutableArray<string> RequiredDiagnosticCodes { get; }

    internal static EpubCorpusBaselineEntry FromResult(EpubCorpusPublicationExecutionResult result) => new(
        result.Id,
        result.Status,
        result.Evidence.EpubVersion,
        result.Evidence.ManifestItemCount,
        result.Evidence.SpineItemCount,
        result.Evidence.ImportedNodeCount,
        result.Evidence.ImportedAssetCount,
        result.Evidence.ValidationDiagnosticCount,
        result.Evidence.FidelityLostUnitCount,
        result.Evidence.DocumentId,
        result.Evidence.CanonicalHash,
        result.Evidence.Semantic,
        result.Diagnostics.Select(static item => item.Code));
}

/// <summary>Describes one stable difference between an accepted and an observed baseline.</summary>
public sealed record EpubCorpusBaselineDifference(
    EpubCorpusPublicationId Id,
    string Field,
    string Expected,
    string Actual);

/// <summary>Contains deterministic baseline comparison results.</summary>
public sealed record EpubCorpusBaselineComparison
{
    public EpubCorpusBaselineComparison(IEnumerable<EpubCorpusBaselineDifference> differences)
    {
        ArgumentNullException.ThrowIfNull(differences);
        Differences = differences
            .OrderBy(static item => item.Id.Value, StringComparer.Ordinal)
            .ThenBy(static item => item.Field, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    public ImmutableArray<EpubCorpusBaselineDifference> Differences { get; }

    public bool IsMatch => Differences.IsEmpty;
}

/// <summary>Compares stable corpus evidence without freezing runtime or presentation details.</summary>
public static class EpubCorpusBaselineComparer
{
    public static EpubCorpusBaselineComparison Compare(EpubCorpusBaseline expected, EpubCorpusBaseline actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        var differences = new List<EpubCorpusBaselineDifference>();
        var expectedById = expected.Publications.ToDictionary(static item => item.Id);
        var actualById = actual.Publications.ToDictionary(static item => item.Id);

        foreach (var entry in expected.Publications)
        {
            if (!actualById.TryGetValue(entry.Id, out var observed))
            {
                differences.Add(new(entry.Id, "publication", "present", "missing"));
                continue;
            }

            CompareEntry(entry, observed, differences);
        }

        foreach (var entry in actual.Publications.Where(item => !expectedById.ContainsKey(item.Id)))
        {
            differences.Add(new(entry.Id, "publication", "absent", "present"));
        }

        return new EpubCorpusBaselineComparison(differences);
    }

    private static void CompareEntry(
        EpubCorpusBaselineEntry expected,
        EpubCorpusBaselineEntry actual,
        ICollection<EpubCorpusBaselineDifference> differences)
    {
        AddIfDifferent(expected.Id, "status", expected.Status, actual.Status, differences);
        AddIfDifferent(expected.Id, "epubVersion", expected.EpubVersion, actual.EpubVersion, differences);
        AddIfDifferent(expected.Id, "manifestItemCount", expected.ManifestItemCount, actual.ManifestItemCount, differences);
        AddIfDifferent(expected.Id, "spineItemCount", expected.SpineItemCount, actual.SpineItemCount, differences);
        AddIfDifferent(expected.Id, "importedNodeCount", expected.ImportedNodeCount, actual.ImportedNodeCount, differences);
        AddIfDifferent(expected.Id, "importedAssetCount", expected.ImportedAssetCount, actual.ImportedAssetCount, differences);
        AddIfDifferent(expected.Id, "validationDiagnosticCount", expected.ValidationDiagnosticCount, actual.ValidationDiagnosticCount, differences);
        AddIfDifferent(expected.Id, "fidelityLostUnitCount", expected.FidelityLostUnitCount, actual.FidelityLostUnitCount, differences);
        AddIfDifferent(expected.Id, "documentId", expected.DocumentId, actual.DocumentId, differences);
        AddIfDifferent(expected.Id, "canonicalHash", expected.CanonicalHash, actual.CanonicalHash, differences);
        CompareSemantic(expected.Id, expected.Semantic, actual.Semantic, differences);

        var missingCodes = expected.RequiredDiagnosticCodes.Except(actual.RequiredDiagnosticCodes, StringComparer.Ordinal);
        foreach (var code in missingCodes)
        {
            differences.Add(new(expected.Id, $"requiredDiagnosticCodes/{code}", "present", "missing"));
        }
    }

    private static void CompareSemantic(
        EpubCorpusPublicationId id,
        EpubCorpusSemanticEvidence? expected,
        EpubCorpusSemanticEvidence? actual,
        ICollection<EpubCorpusBaselineDifference> differences)
    {
        if (expected is null || actual is null)
        {
            AddIfDifferent(id, "semantic", expected is null ? "null" : "present", actual is null ? "null" : "present", differences);
            return;
        }

        AddIfDifferent(id, "semantic.orderedNodeIds", string.Join('|', expected.OrderedNodeIds), string.Join('|', actual.OrderedNodeIds), differences);
        AddIfDifferent(id, "semantic.orderedChapterIds", string.Join('|', expected.OrderedChapterIds), string.Join('|', actual.OrderedChapterIds), differences);
        AddIfDifferent(id, "semantic.sourceLocationCount", expected.SourceLocationCount, actual.SourceLocationCount, differences);
        AddIfDifferent(id, "semantic.chapterCount", expected.ChapterCount, actual.ChapterCount, differences);
        AddIfDifferent(id, "semantic.headingCount", expected.HeadingCount, actual.HeadingCount, differences);
        AddIfDifferent(id, "semantic.paragraphCount", expected.ParagraphCount, actual.ParagraphCount, differences);
        AddIfDifferent(id, "semantic.tableOfContentsEntryCount", expected.TableOfContentsEntryCount, actual.TableOfContentsEntryCount, differences);
        AddIfDifferent(id, "semantic.internalLinkCount", expected.InternalLinkCount, actual.InternalLinkCount, differences);
        AddIfDifferent(id, "semantic.figureCount", expected.FigureCount, actual.FigureCount, differences);
        AddIfDifferent(id, "semantic.footnoteCount", expected.FootnoteCount, actual.FootnoteCount, differences);
        AddIfDifferent(id, "semantic.footnoteReferenceCount", expected.FootnoteReferenceCount, actual.FootnoteReferenceCount, differences);
        AddIfDifferent(id, "semantic.tableCount", expected.TableCount, actual.TableCount, differences);
        AddIfDifferent(id, "semantic.tableCellCount", expected.TableCellCount, actual.TableCellCount, differences);
    }

    private static void AddIfDifferent<T>(
        EpubCorpusPublicationId id,
        string field,
        T expected,
        T actual,
        ICollection<EpubCorpusBaselineDifference> differences)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            differences.Add(new(id, field, expected?.ToString() ?? "null", actual?.ToString() ?? "null"));
        }
    }
}
