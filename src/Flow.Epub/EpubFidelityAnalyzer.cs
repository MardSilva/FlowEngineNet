using System.Text;
using Flow.Core;
using Flow.Documents;

namespace Flow.Epub;

/// <summary>Reference analyzer for the experimental EPUB fidelity profile.</summary>
public sealed class EpubFidelityAnalyzer : IEpubFidelityAnalyzer
{
    /// <inheritdoc />
    public EpubFidelityReport Analyze(EpubImportResult importResult) => Analyze(importResult, default);

    /// <inheritdoc />
    public EpubFidelityReport Analyze(
        EpubImportResult importResult,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(importResult);
        cancellationToken.ThrowIfCancellationRequested();

        var source = importResult.FidelitySource?.Items ?? [];
        var destination = CountDestination(importResult, cancellationToken);
        var measurements = Enum.GetValues<EpubFidelityMetric>()
            .Select(metric => CreateMeasurement(
                metric,
                source.Where(item => item.Metric == metric).ToArray(),
                destination.GetValueOrDefault(metric)))
            .ToArray();

        cancellationToken.ThrowIfCancellationRequested();
        var findings = CreateDiagnosticFindings(importResult, cancellationToken).ToList();
        AddMeasurementFindings(measurements, source, importResult, findings, cancellationToken);

        var sourceTotal = measurements.Sum(static item => item.SourceCount);
        var preserved = measurements.Sum(static item => item.PreservedCount);
        var transformed = measurements.Sum(static item => item.TransformedCount);
        var approximated = measurements.Sum(static item => item.ApproximatedCount);
        var unsupported = measurements.Sum(static item => item.UnsupportedCount);
        var lost = measurements.Sum(static item => item.LostCount);
        decimal? percentage = sourceTotal == 0
            ? null
            : decimal.Round((preserved + transformed) * 100m / sourceTotal, 6, MidpointRounding.ToZero);
        var partial = importResult.FidelitySource is null || importResult.Document is null || !importResult.IsSuccess;
        var summary = new EpubFidelitySummary(
            sourceTotal,
            preserved,
            transformed,
            approximated,
            unsupported,
            lost,
            percentage,
            partial);
        return new EpubFidelityReport(importResult.IsSuccess, summary, measurements, findings);
    }

    private static EpubFidelityMeasurement CreateMeasurement(
        EpubFidelityMetric metric,
        IReadOnlyCollection<EpubFidelitySourceCount> sourceItems,
        long destination)
    {
        var source = sourceItems.Sum(static item => item.Count);
        if (source == 0)
        {
            return new EpubFidelityMeasurement(metric, 0, destination, 0, 0, 0, 0, 0);
        }

        var approximated = sourceItems
            .Where(static item => item.ForcedStatus == EpubFidelityStatus.Approximated)
            .Sum(static item => item.Count);
        var unsupported = sourceItems
            .Where(static item => item.ForcedStatus == EpubFidelityStatus.Unsupported)
            .Sum(static item => item.Count);
        var forcedLost = sourceItems
            .Where(static item => item.ForcedStatus == EpubFidelityStatus.Lost)
            .Sum(static item => item.Count);
        var eligible = source - approximated - unsupported - forcedLost;
        var represented = Math.Min(eligible, destination);
        var lost = forcedLost + eligible - represented;
        var transformed = IsTransformation(metric) ? represented : 0;
        var preserved = transformed == 0 ? represented : 0;
        return new EpubFidelityMeasurement(
            metric,
            source,
            destination,
            preserved,
            transformed,
            approximated,
            unsupported,
            lost);
    }

    private static bool IsTransformation(EpubFidelityMetric metric) => metric is
        EpubFidelityMetric.LinearSpineItems
        or EpubFidelityMetric.NonLinearSpineItems
        or EpubFidelityMetric.InternalLinks
        or EpubFidelityMetric.Images
        or EpubFidelityMetric.Covers
        or EpubFidelityMetric.TableOfContentsEntries
        or EpubFidelityMetric.ManifestResources;

    private static Dictionary<EpubFidelityMetric, long> CountDestination(
        EpubImportResult import,
        CancellationToken cancellationToken)
    {
        var counts = Enum.GetValues<EpubFidelityMetric>().ToDictionary(static metric => metric, static _ => 0L);
        var document = import.Document;
        if (document is null)
        {
            return counts;
        }

        foreach (var location in document.Index.Locations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (location.Node)
            {
                case Chapter:
                    var role = FindSpineRole(import, location.Node.Id);
                    counts[role == EpubSpineReadingRole.Supplemental
                        ? EpubFidelityMetric.NonLinearSpineItems
                        : EpubFidelityMetric.LinearSpineItems]++;
                    break;
                case Heading heading:
                    counts[EpubFidelityMetric.Headings]++;
                    CountInline(heading.Content, counts, cancellationToken);
                    break;
                case Paragraph paragraph:
                    counts[EpubFidelityMetric.Paragraphs]++;
                    CountInline(paragraph.Content, counts, cancellationToken);
                    break;
                case Caption caption:
                    CountInline(caption.Content, counts, cancellationToken);
                    break;
                case CodeBlock code:
                    counts[EpubFidelityMetric.SignificantCharacters] += CountSignificant(code.Code);
                    break;
                case Figure figure:
                    counts[EpubFidelityMetric.Images]++;
                    if (figure.Link is not null)
                    {
                        counts[figure.Link.IsInternal
                            ? EpubFidelityMetric.InternalLinks
                            : EpubFidelityMetric.ExternalLinks]++;
                    }

                    break;
                case Footnote:
                    counts[EpubFidelityMetric.Notes]++;
                    break;
                case Table table:
                    counts[EpubFidelityMetric.Tables]++;
                    break;
                case TableRow:
                    counts[EpubFidelityMetric.TableRows]++;
                    break;
                case TableCell or TableHeaderCell:
                    counts[EpubFidelityMetric.TableCells]++;
                    break;
                case TableOfContents toc:
                    counts[EpubFidelityMetric.TableOfContentsEntries] += toc.Entries.Length;
                    break;
            }
        }

        counts[EpubFidelityMetric.Covers] = document.Presentation?.Cover is null ? 0 : 1;
        counts[EpubFidelityMetric.ManifestResources] = import.FidelitySource?.Items
            .Where(static item => item.Metric == EpubFidelityMetric.ManifestResources
                                  && item.ForcedStatus is null)
            .Sum(static item => item.Count) ?? 0;
        return counts;
    }

    private static EpubSpineReadingRole FindSpineRole(EpubImportResult import, NodeId chapterId)
    {
        var sourceLocation = import.SourceMap?.GetLocations(chapterId).FirstOrDefault();
        if (sourceLocation is null)
        {
            return EpubSpineReadingRole.Linear;
        }

        return import.ProcessingReport?.Spine.FirstOrDefault(
                   decision => string.Equals(
                       decision.SelectedResourcePath,
                       sourceLocation.ResourcePath,
                       StringComparison.Ordinal))?.ReadingRole
               ?? EpubSpineReadingRole.Linear;
    }

    private static void CountInline(
        IEnumerable<InlineNode> nodes,
        IDictionary<EpubFidelityMetric, long> counts,
        CancellationToken cancellationToken)
    {
        foreach (var node in nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (node)
            {
                case Text text:
                    counts[EpubFidelityMetric.SignificantCharacters] += CountSignificant(text.Value);
                    break;
                case InlineCode code:
                    counts[EpubFidelityMetric.SignificantCharacters] += CountSignificant(code.Code);
                    break;
                case Link link:
                    counts[DocumentAnchor.TryParse(link.Target, out _)
                        ? EpubFidelityMetric.InternalLinks
                        : EpubFidelityMetric.ExternalLinks]++;
                    CountInline(link.Children, counts, cancellationToken);
                    break;
                case FootnoteReference reference:
                    counts[EpubFidelityMetric.NoteReferences]++;
                    CountInline(reference.Label, counts, cancellationToken);
                    break;
                case InlineContainerNode container:
                    CountInline(container.Children, counts, cancellationToken);
                    break;
            }
        }
    }

    internal static long CountSignificant(string value) => value
        .EnumerateRunes()
        .LongCount(static rune => !Rune.IsWhiteSpace(rune));

    private static IEnumerable<EpubFidelityFinding> CreateDiagnosticFindings(
        EpubImportResult import,
        CancellationToken cancellationToken)
    {
        foreach (var group in import.Diagnostics.GroupBy(diagnostic => new
        {
            diagnostic.Code,
            diagnostic.Resource,
            diagnostic.Message,
            diagnostic.Severity,
        }))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var classification = Classify(group.Key.Code, group.Key.Severity);
            var location = FindLocation(import, group.Key.Resource);
            yield return new EpubFidelityFinding(
                $"FID-{group.Key.Code}",
                classification.Status,
                classification.Impact,
                group.Key.Message,
                group.Sum(static diagnostic => diagnostic.Count),
                group.Key.Resource,
                location?.Fragment,
                location?.NodeId,
                FindAsset(import, location?.NodeId),
                group.Key.Code);
        }
    }

    private static (EpubFidelityStatus Status, EpubFidelityImpact Impact) Classify(
        string code,
        EpubDiagnosticSeverity severity)
    {
        if (code is EpubDiagnosticCodes.UnsupportedElement
            or EpubDiagnosticCodes.UnsupportedManifestProperty
            or EpubDiagnosticCodes.HeadingLevelNormalized
            or EpubDiagnosticCodes.NoteResourceFallbackUsed
            or EpubDiagnosticCodes.UnsupportedCssProperty
            or EpubDiagnosticCodes.UnsupportedCssSelector
            or EpubDiagnosticCodes.CssTargetNotRepresentable
            or EpubDiagnosticCodes.SvgImageSemanticLoss
            or EpubDiagnosticCodes.MathSemanticLoss
            or EpubDiagnosticCodes.EmbeddedFontBytesNotPreserved)
        {
            return (EpubFidelityStatus.Approximated, EpubFidelityImpact.Minor);
        }

        if (code is EpubDiagnosticCodes.UnsupportedResource
            or EpubDiagnosticCodes.UnsupportedImageFormat
            or EpubDiagnosticCodes.UnsupportedMediaOverlay
            or EpubDiagnosticCodes.LinkedImageTargetNotRepresentable
            or EpubDiagnosticCodes.UnsafeSvg)
        {
            return (EpubFidelityStatus.Unsupported, EpubFidelityImpact.Moderate);
        }

        if (severity == EpubDiagnosticSeverity.Error)
        {
            return (EpubFidelityStatus.Lost, EpubFidelityImpact.Major);
        }

        return (EpubFidelityStatus.Transformed, severity == EpubDiagnosticSeverity.Warning
            ? EpubFidelityImpact.Minor
            : EpubFidelityImpact.Informational);
    }

    private static EpubSourceLocation? FindLocation(EpubImportResult import, string? resourcePath) =>
        resourcePath is null
            ? null
            : import.SourceMap?.Locations.FirstOrDefault(
                location => string.Equals(location.ResourcePath, resourcePath, StringComparison.Ordinal));

    private static AssetId? FindAsset(EpubImportResult import, NodeId? nodeId)
    {
        if (nodeId is null || import.Document is null
                           || !import.Document.Index.TryGetUniqueNode(nodeId, out var node)
                           || node is not Figure figure)
        {
            return null;
        }

        return figure.AssetId;
    }

    private static void AddMeasurementFindings(
        IEnumerable<EpubFidelityMeasurement> measurements,
        IEnumerable<EpubFidelitySourceCount> source,
        EpubImportResult import,
        ICollection<EpubFidelityFinding> findings,
        CancellationToken cancellationToken)
    {
        foreach (var measurement in measurements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (measurement.ApproximatedCount > 0)
            {
                AddByResource(measurement, measurement.ApproximatedCount, EpubFidelityStatus.Approximated);
            }

            if (measurement.UnsupportedCount > 0)
            {
                AddByResource(measurement, measurement.UnsupportedCount, EpubFidelityStatus.Unsupported);
            }

            if (measurement.LostCount > 0)
            {
                AddByResource(measurement, measurement.LostCount, EpubFidelityStatus.Lost);
            }

            if (measurement.DestinationCount > measurement.SourceCount)
            {
                var excess = measurement.DestinationCount - measurement.SourceCount;
                findings.Add(new EpubFidelityFinding(
                    $"FID-RECONCILIATION-{measurement.Metric.ToString().ToUpperInvariant()}",
                    EpubFidelityStatus.Approximated,
                    EpubFidelityImpact.Moderate,
                    $"Destination count exceeds the measured source count by {excess}; the metric requires reconciliation.",
                    checked((int)Math.Min(excess, int.MaxValue))));
            }
        }

        void AddByResource(EpubFidelityMeasurement measurement, long count, EpubFidelityStatus status)
        {
            var remaining = count;
            var sources = source
                .Where(item => item.Metric == measurement.Metric)
                .OrderByDescending(item => item.ForcedStatus == status)
                .ThenBy(static item => item.ResourcePath, StringComparer.Ordinal)
                .ToArray();
            if (sources.Length == 0)
            {
                sources = [new EpubFidelitySourceCount(measurement.Metric, null, count)];
            }

            foreach (var item in sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (remaining == 0)
                {
                    break;
                }

                var itemCount = Math.Min(item.Count, remaining);
                var location = FindLocation(import, item.ResourcePath);
                findings.Add(new EpubFidelityFinding(
                    $"FID-{status.ToString().ToUpperInvariant()}-{measurement.Metric.ToString().ToUpperInvariant()}",
                    status,
                    status switch
                    {
                        EpubFidelityStatus.Lost => EpubFidelityImpact.Major,
                        EpubFidelityStatus.Unsupported => EpubFidelityImpact.Moderate,
                        _ => EpubFidelityImpact.Minor,
                    },
                    status switch
                    {
                        EpubFidelityStatus.Lost =>
                            $"{itemCount} source unit(s) measured as {measurement.Metric} have no destination representation.",
                        EpubFidelityStatus.Unsupported =>
                            $"{itemCount} source unit(s) measured as {measurement.Metric} use an unsupported representation.",
                        _ =>
                            $"{itemCount} source unit(s) measured as {measurement.Metric} were retained without a direct semantic equivalent.",
                    },
                    checked((int)Math.Min(itemCount, int.MaxValue)),
                    item.ResourcePath,
                    location?.Fragment,
                    location?.NodeId,
                    FindAsset(import, location?.NodeId)));
                remaining -= itemCount;
            }
        }
    }
}
