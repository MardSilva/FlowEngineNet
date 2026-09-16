using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Flow.Core;
using Flow.Documents;
using Flow.Layout;

namespace Flow.Epub.Corpus;

/// <summary>Identifies one structural or reference family audited across the EPUB-to-HTML pipeline.</summary>
public enum EpubLargePublicationAuditKind
{
    SpineChapterMapping,
    ChapterHtmlPage,
    ReadingOrder,
    TableOfContentsDestination,
    InternalLinkSameResource,
    InternalLinkCrossResource,
    InternalLinkUnclassified,
    ExternalLink,
    FigureAsset,
    CoverAsset,
    SvgAssetSafety,
    AssetDeduplication,
    FootnoteReference,
    FootnoteCrossResource,
    FootnoteBacklink,
    TableStructure,
    TableCaption,
    TableSpan,
    TableHeaderReference,
    SourceMapLocation,
    SerializationId,
    MobileLayoutId,
    DesktopLayoutId,
    MobileHtmlId,
    DesktopHtmlId,
}

/// <summary>Distinguishes present evidence, an absent optional construct, and a non-applicable check.</summary>
public enum EpubLargePublicationAuditApplicability
{
    Present,
    Absent,
    NotApplicable,
}

/// <summary>Counts mutually exclusive outcomes for one audited reference family.</summary>
public sealed record EpubLargePublicationReferenceCounts
{
    public EpubLargePublicationReferenceCounts(
        int found,
        int resolved,
        int broken,
        int ambiguous,
        int approximated,
        int skipped)
    {
        var values = new[] { found, resolved, broken, ambiguous, approximated, skipped };
        if (values.Any(static value => value < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(found), "Reference audit counts cannot be negative.");
        }

        if (resolved + broken + ambiguous + approximated + skipped != found)
        {
            throw new ArgumentException("Every found reference must have exactly one audit outcome.");
        }

        Found = found;
        Resolved = resolved;
        Broken = broken;
        Ambiguous = ambiguous;
        Approximated = approximated;
        Skipped = skipped;
    }

    public int Found { get; }

    public int Resolved { get; }

    public int Broken { get; }

    public int Ambiguous { get; }

    public int Approximated { get; }

    public int Skipped { get; }
}

/// <summary>Contains content-free, deterministic evidence for one audited reference family.</summary>
public sealed record EpubLargePublicationReferenceAudit
{
    public EpubLargePublicationReferenceAudit(
        EpubLargePublicationAuditKind kind,
        EpubLargePublicationAuditApplicability applicability,
        EpubLargePublicationReferenceCounts counts,
        bool essential)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (!Enum.IsDefined(applicability))
        {
            throw new ArgumentOutOfRangeException(nameof(applicability));
        }

        ArgumentNullException.ThrowIfNull(counts);
        if (applicability != EpubLargePublicationAuditApplicability.Present && counts.Found != 0)
        {
            throw new ArgumentException("Absent or non-applicable audit families cannot contain found references.", nameof(counts));
        }

        Kind = kind;
        Applicability = applicability;
        Counts = counts;
        Essential = essential;
    }

    public EpubLargePublicationAuditKind Kind { get; }

    public EpubLargePublicationAuditApplicability Applicability { get; }

    public EpubLargePublicationReferenceCounts Counts { get; }

    public bool Essential { get; }
}

internal sealed record EpubLargePublicationAuditResult(
    ImmutableArray<EpubLargePublicationReferenceAudit> Audits)
{
    internal bool HasEssentialFailure => Audits.Any(static item =>
        item.Essential && (item.Counts.Broken > 0 || item.Counts.Ambiguous > 0));

    internal bool HasApproximation => Audits.Any(static item => item.Counts.Approximated > 0);
}

internal sealed record EpubLargePublicationAuditInput(
    EpubPackageProcessingReport? ProcessingReport,
    EpubSourceMap? SourceMap,
    FlowDocument Document,
    FlowDocument RestoredDocument,
    LayoutDocument? MobileLayout,
    HtmlBookPackageEvidence? MobilePackage,
    LayoutDocument? DesktopLayout,
    HtmlBookPackageEvidence? DesktopPackage);

internal static class EpubLargePublicationAuditor
{
    internal static EpubLargePublicationAuditResult Audit(
        EpubLargePublicationAuditInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var audits = new List<EpubLargePublicationReferenceAudit>();
        var document = input.Document;
        var sourceMap = input.SourceMap;

        var chapters = document.Index.Locations
            .Where(static location => location.Node is Chapter)
            .Select(static location => location.Node.Id)
            .ToArray();
        var includedSpine = input.ProcessingReport?.Spine
            .Where(static item => item.Disposition is EpubSpineDisposition.Included or EpubSpineDisposition.Substituted)
            .OrderBy(static item => item.Position)
            .ToArray() ?? [];
        var mappedChapters = sourceMap?.Locations
            .Where(location => location.Fragment is null
                && document.Index.TryGetUniqueNode(location.NodeId, out var node)
                && node is Chapter)
            .Select(static location => location.NodeId)
            .ToArray() ?? [];

        audits.Add(CompareOrdered(
            EpubLargePublicationAuditKind.SpineChapterMapping,
            includedSpine.Length,
            mappedChapters.Length,
            essential: true));
        audits.Add(CompareSequences(
            EpubLargePublicationAuditKind.ReadingOrder,
            mappedChapters,
            chapters,
            essential: true));
        audits.Add(AuditChapterPages(chapters, input.MobilePackage, input.DesktopPackage));
        audits.Add(AuditToc(document));

        var links = EnumerateLinks(document).ToArray();
        audits.Add(AuditInternalLinks(document, sourceMap, links, sameResource: true));
        audits.Add(AuditInternalLinks(document, sourceMap, links, sameResource: false));
        audits.Add(AuditUnclassifiedInternalLinks(sourceMap, links));
        audits.Add(AuditExternalLinks(links, input.MobilePackage, input.DesktopPackage));
        audits.Add(AuditFigures(document));
        audits.Add(AuditCover(document));
        audits.Add(AuditSvg(document));
        audits.Add(AuditAssetDeduplication(document));
        audits.Add(AuditFootnoteReferences(document));
        audits.Add(AuditCrossResourceFootnotes(document, sourceMap));
        audits.Add(AuditFootnoteBacklinks(document));
        audits.Add(AuditTables(document));
        audits.Add(AuditTableCaptions(document));
        audits.Add(AuditTableSpans(document));
        audits.Add(AuditTableHeaders(document));
        audits.Add(AuditSourceMap(document, sourceMap));
        audits.Add(CompareIds(
            EpubLargePublicationAuditKind.SerializationId,
            document.Index.Locations.Select(static item => item.Node.Id.Value),
            input.RestoredDocument.Index.Locations.Select(static item => item.Node.Id.Value),
            essential: true));
        audits.Add(AuditLayoutIds(EpubLargePublicationAuditKind.MobileLayoutId, document, input.MobileLayout));
        audits.Add(AuditLayoutIds(EpubLargePublicationAuditKind.DesktopLayoutId, document, input.DesktopLayout));
        audits.Add(AuditHtmlIds(EpubLargePublicationAuditKind.MobileHtmlId, document, input.MobilePackage));
        audits.Add(AuditHtmlIds(EpubLargePublicationAuditKind.DesktopHtmlId, document, input.DesktopPackage));

        cancellationToken.ThrowIfCancellationRequested();
        return new EpubLargePublicationAuditResult(audits.OrderBy(static item => item.Kind).ToImmutableArray());
    }

    private static EpubLargePublicationReferenceAudit AuditChapterPages(
        IReadOnlyCollection<Flow.Core.NodeId> chapters,
        HtmlBookPackageEvidence? mobile,
        HtmlBookPackageEvidence? desktop)
    {
        if (chapters.Count == 0)
        {
            return Absent(EpubLargePublicationAuditKind.ChapterHtmlPage, essential: true);
        }

        var resolved = 0;
        var broken = 0;
        var ambiguous = 0;
        foreach (var chapter in chapters)
        {
            var mobilePaths = mobile?.PathsById.GetValueOrDefault(chapter.Value, []) ?? [];
            var desktopPaths = desktop?.PathsById.GetValueOrDefault(chapter.Value, []) ?? [];
            if (mobilePaths.Length > 1 || desktopPaths.Length > 1)
            {
                ambiguous++;
            }
            else if (mobilePaths.Length == 1
                     && desktopPaths.Length == 1
                     && mobilePaths[0].StartsWith("chapters/", StringComparison.Ordinal)
                     && desktopPaths[0].StartsWith("chapters/", StringComparison.Ordinal))
            {
                resolved++;
            }
            else
            {
                broken++;
            }
        }

        return Present(
            EpubLargePublicationAuditKind.ChapterHtmlPage,
            chapters.Count,
            resolved,
            broken,
            ambiguous,
            essential: true);
    }

    private static EpubLargePublicationReferenceAudit AuditToc(FlowDocument document)
    {
        var entries = document.Index.Locations
            .Select(static item => item.Node)
            .OfType<TableOfContents>()
            .SelectMany(static item => item.Entries)
            .ToArray();
        if (entries.Length == 0)
        {
            return Absent(EpubLargePublicationAuditKind.TableOfContentsDestination, essential: true);
        }

        var resolved = entries.Count(entry => document.TryResolveAnchor(entry.Target, out _));
        return Present(
            EpubLargePublicationAuditKind.TableOfContentsDestination,
            entries.Length,
            resolved,
            entries.Length - resolved,
            0,
            essential: true);
    }

    private static EpubLargePublicationReferenceAudit AuditInternalLinks(
        FlowDocument document,
        EpubSourceMap? sourceMap,
        IEnumerable<NodeLink> links,
        bool sameResource)
    {
        var candidates = links
            .Where(static item => DocumentAnchor.TryParse(item.Link.Target, out _))
            .Where(item => IsSameResource(item.Owner.Id, DocumentAnchor.Parse(item.Link.Target).TargetId, sourceMap) == sameResource)
            .ToArray();
        var kind = sameResource
            ? EpubLargePublicationAuditKind.InternalLinkSameResource
            : EpubLargePublicationAuditKind.InternalLinkCrossResource;
        if (candidates.Length == 0)
        {
            return Absent(kind, essential: true);
        }

        var resolved = 0;
        var broken = 0;
        var ambiguous = 0;
        foreach (var candidate in candidates)
        {
            var anchor = DocumentAnchor.Parse(candidate.Link.Target);
            var locations = document.Index.GetLocations(anchor.TargetId);
            if (locations.Length == 1 && document.TryResolveAnchor(anchor, out _))
            {
                resolved++;
            }
            else if (locations.Length > 1)
            {
                ambiguous++;
            }
            else
            {
                broken++;
            }
        }

        return Present(kind, candidates.Length, resolved, broken, ambiguous, essential: true);
    }

    private static EpubLargePublicationReferenceAudit AuditUnclassifiedInternalLinks(
        EpubSourceMap? sourceMap,
        IEnumerable<NodeLink> links)
    {
        var count = links
            .Where(static item => DocumentAnchor.TryParse(item.Link.Target, out _))
            .Count(item => IsSameResource(
                item.Owner.Id,
                DocumentAnchor.Parse(item.Link.Target).TargetId,
                sourceMap) is null);
        return count == 0
            ? NotApplicable(EpubLargePublicationAuditKind.InternalLinkUnclassified, essential: false)
            : Present(
                EpubLargePublicationAuditKind.InternalLinkUnclassified,
                count,
                0,
                0,
                0,
                essential: false,
                approximated: count);
    }

    private static EpubLargePublicationReferenceAudit AuditExternalLinks(
        IEnumerable<NodeLink> links,
        HtmlBookPackageEvidence? mobile,
        HtmlBookPackageEvidence? desktop)
    {
        var targets = links
            .Select(static item => item.Link.Target)
            .Where(static target => !DocumentAnchor.TryParse(target, out _))
            .ToArray();
        if (targets.Length == 0)
        {
            return Absent(EpubLargePublicationAuditKind.ExternalLink, essential: false);
        }

        var resolved = targets.Count(target => Uri.TryCreate(target, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https" or "mailto"
            && mobile?.ExternalReferences.ContainsKey(target) == true
            && desktop?.ExternalReferences.ContainsKey(target) == true);
        return Present(
            EpubLargePublicationAuditKind.ExternalLink,
            targets.Length,
            resolved,
            targets.Length - resolved,
            0,
            essential: false);
    }

    private static EpubLargePublicationReferenceAudit AuditFigures(FlowDocument document)
    {
        var figures = document.Index.Locations.Select(static item => item.Node).OfType<Figure>().ToArray();
        if (figures.Length == 0)
        {
            return Absent(EpubLargePublicationAuditKind.FigureAsset, essential: true);
        }

        var resolved = figures.Count(figure => document.Assets.ContainsKey(figure.AssetId));
        return Present(EpubLargePublicationAuditKind.FigureAsset, figures.Length, resolved, figures.Length - resolved, 0, true);
    }

    private static EpubLargePublicationReferenceAudit AuditCover(FlowDocument document)
    {
        if (document.Presentation?.Cover is not { } cover)
        {
            return Absent(EpubLargePublicationAuditKind.CoverAsset, essential: false);
        }

        var resolved = document.Index.TryGetUniqueNode(cover.FigureId, out var node)
            && node is Figure figure
            && document.Assets.ContainsKey(figure.AssetId);
        return Present(EpubLargePublicationAuditKind.CoverAsset, 1, resolved ? 1 : 0, resolved ? 0 : 1, 0, true);
    }

    private static EpubLargePublicationReferenceAudit AuditSvg(FlowDocument document)
    {
        var assets = document.Assets.Values
            .Where(static item => string.Equals(item.MediaType, "image/svg+xml", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (assets.Length == 0)
        {
            return NotApplicable(EpubLargePublicationAuditKind.SvgAssetSafety, essential: true);
        }

        var safe = assets.Count(static asset => IsPassiveSvg(asset.Data.AsSpan()));
        return Present(EpubLargePublicationAuditKind.SvgAssetSafety, assets.Length, safe, assets.Length - safe, 0, true);
    }

    private static EpubLargePublicationReferenceAudit AuditAssetDeduplication(FlowDocument document)
    {
        if (document.Assets.Count == 0)
        {
            return NotApplicable(EpubLargePublicationAuditKind.AssetDeduplication, essential: true);
        }

        var hashes = document.Assets.Values
            .Select(static asset => Convert.ToHexString(SHA256.HashData(asset.Data.AsSpan())))
            .ToArray();
        var duplicateCount = hashes.Length - hashes.Distinct(StringComparer.Ordinal).Count();
        return Present(
            EpubLargePublicationAuditKind.AssetDeduplication,
            hashes.Length,
            hashes.Length - duplicateCount,
            duplicateCount,
            0,
            essential: true);
    }

    private static EpubLargePublicationReferenceAudit AuditFootnoteReferences(FlowDocument document)
    {
        var references = EnumerateInline(document).OfType<FootnoteReference>().ToArray();
        if (references.Length == 0)
        {
            return Absent(EpubLargePublicationAuditKind.FootnoteReference, essential: true);
        }

        var resolved = 0;
        var ambiguous = 0;
        foreach (var reference in references)
        {
            var locations = document.Index.GetLocations(reference.TargetId);
            if (locations.Length == 1 && locations[0].Node is Footnote)
            {
                resolved++;
            }
            else if (locations.Length > 1)
            {
                ambiguous++;
            }
        }

        return Present(
            EpubLargePublicationAuditKind.FootnoteReference,
            references.Length,
            resolved,
            references.Length - resolved - ambiguous,
            ambiguous,
            essential: true);
    }

    private static EpubLargePublicationReferenceAudit AuditFootnoteBacklinks(FlowDocument document)
    {
        var footnotes = document.Index.Locations.Select(static item => item.Node).OfType<Footnote>().ToArray();
        if (footnotes.Length == 0)
        {
            return NotApplicable(EpubLargePublicationAuditKind.FootnoteBacklink, essential: false);
        }

        var resolved = footnotes.Count(footnote => EnumerateInline(footnote)
            .OfType<Link>()
            .Any(link => DocumentAnchor.TryParse(link.Target, out var anchor)
                && document.TryResolveAnchor(anchor, out _)));
        return Present(
            EpubLargePublicationAuditKind.FootnoteBacklink,
            footnotes.Length,
            resolved,
            0,
            0,
            essential: false,
            approximated: footnotes.Length - resolved);
    }

    private static EpubLargePublicationReferenceAudit AuditCrossResourceFootnotes(
        FlowDocument document,
        EpubSourceMap? sourceMap)
    {
        var references = EnumerateFootnoteReferences(document)
            .Where(item => IsSameResource(item.Owner.Id, item.Reference.TargetId, sourceMap) is false)
            .ToArray();
        if (references.Length == 0)
        {
            return NotApplicable(EpubLargePublicationAuditKind.FootnoteCrossResource, essential: true);
        }

        var resolved = 0;
        var ambiguous = 0;
        foreach (var item in references)
        {
            var locations = document.Index.GetLocations(item.Reference.TargetId);
            if (locations.Length == 1 && locations[0].Node is Footnote)
            {
                resolved++;
            }
            else if (locations.Length > 1)
            {
                ambiguous++;
            }
        }

        return Present(
            EpubLargePublicationAuditKind.FootnoteCrossResource,
            references.Length,
            resolved,
            references.Length - resolved - ambiguous,
            ambiguous,
            essential: true);
    }

    private static EpubLargePublicationReferenceAudit AuditTables(FlowDocument document)
    {
        var tables = document.Index.Locations.Select(static item => item.Node).OfType<Table>().ToArray();
        if (tables.Length == 0)
        {
            return Absent(EpubLargePublicationAuditKind.TableStructure, essential: true);
        }

        var resolved = tables.Count(static table => table.Bodies.Length > 0
            && table.Bodies.All(static body => body.Rows.Length > 0));
        return Present(EpubLargePublicationAuditKind.TableStructure, tables.Length, resolved, tables.Length - resolved, 0, true);
    }

    private static EpubLargePublicationReferenceAudit AuditTableCaptions(FlowDocument document)
    {
        var captions = document.Index.Locations.Select(static item => item.Node).OfType<TableCaption>().ToArray();
        return captions.Length == 0
            ? NotApplicable(EpubLargePublicationAuditKind.TableCaption, essential: false)
            : Present(EpubLargePublicationAuditKind.TableCaption, captions.Length, captions.Length, 0, 0, false);
    }

    private static EpubLargePublicationReferenceAudit AuditTableSpans(FlowDocument document)
    {
        var spans = document.Index.Locations
            .Select(static item => item.Node)
            .OfType<TableCellNode>()
            .Where(static cell => cell.ColumnSpan > 1 || cell.RowSpan > 1)
            .ToArray();
        return spans.Length == 0
            ? NotApplicable(EpubLargePublicationAuditKind.TableSpan, essential: false)
            : Present(EpubLargePublicationAuditKind.TableSpan, spans.Length, spans.Length, 0, 0, false);
    }

    private static EpubLargePublicationReferenceAudit AuditTableHeaders(FlowDocument document)
    {
        var cells = document.Index.Locations.Select(static item => item.Node).OfType<TableCellNode>().ToArray();
        var references = cells.SelectMany(static cell => cell.Headers).ToArray();
        if (references.Length == 0)
        {
            return NotApplicable(EpubLargePublicationAuditKind.TableHeaderReference, essential: true);
        }

        var resolved = 0;
        var ambiguous = 0;
        foreach (var reference in references)
        {
            var locations = document.Index.GetLocations(reference);
            if (locations.Length == 1 && locations[0].Node is TableHeaderCell)
            {
                resolved++;
            }
            else if (locations.Length > 1)
            {
                ambiguous++;
            }
        }

        return Present(
            EpubLargePublicationAuditKind.TableHeaderReference,
            references.Length,
            resolved,
            references.Length - resolved - ambiguous,
            ambiguous,
            essential: true);
    }

    private static EpubLargePublicationReferenceAudit AuditSourceMap(
        FlowDocument document,
        EpubSourceMap? sourceMap)
    {
        if (sourceMap is null || sourceMap.Locations.Length == 0)
        {
            return Absent(EpubLargePublicationAuditKind.SourceMapLocation, essential: true);
        }

        var resolved = 0;
        var ambiguous = 0;
        foreach (var location in sourceMap.Locations)
        {
            var semanticLocations = document.Index.GetLocations(location.NodeId);
            if (semanticLocations.Length == 1)
            {
                resolved++;
            }
            else if (semanticLocations.Length > 1)
            {
                ambiguous++;
            }
        }

        return Present(
            EpubLargePublicationAuditKind.SourceMapLocation,
            sourceMap.Locations.Length,
            resolved,
            sourceMap.Locations.Length - resolved - ambiguous,
            ambiguous,
            essential: true);
    }

    private static EpubLargePublicationReferenceAudit AuditLayoutIds(
        EpubLargePublicationAuditKind kind,
        FlowDocument document,
        LayoutDocument? layout) => layout is null
        ? Absent(kind, essential: true)
        : CompareIds(
            kind,
            document.Index.Locations.Select(static item => item.Node.Id.Value),
            EnumerateLayoutNodes(layout.Nodes).Select(static item => item.SemanticId.Value),
            essential: true);

    private static EpubLargePublicationReferenceAudit AuditHtmlIds(
        EpubLargePublicationAuditKind kind,
        FlowDocument document,
        HtmlBookPackageEvidence? package)
    {
        if (package is null)
        {
            return Absent(kind, essential: true);
        }

        var expected = document.Index.Locations.Select(static item => item.Node.Id.Value).ToArray();
        var resolved = 0;
        var broken = 0;
        var ambiguous = 0;
        foreach (var id in expected)
        {
            var paths = package.PathsById.GetValueOrDefault(id, []);
            if (paths.Length == 1)
            {
                resolved++;
            }
            else if (paths.Length > 1)
            {
                ambiguous++;
            }
            else
            {
                broken++;
            }
        }

        return Present(kind, expected.Length, resolved, broken, ambiguous, essential: true);
    }

    private static EpubLargePublicationReferenceAudit CompareOrdered(
        EpubLargePublicationAuditKind kind,
        int expected,
        int actual,
        bool essential)
    {
        if (expected == 0)
        {
            return Absent(kind, essential);
        }

        var resolved = Math.Min(expected, actual);
        return Present(
            kind,
            Math.Max(expected, actual),
            resolved,
            Math.Max(0, expected - actual),
            Math.Max(0, actual - expected),
            essential);
    }

    private static EpubLargePublicationReferenceAudit CompareSequences(
        EpubLargePublicationAuditKind kind,
        IReadOnlyList<Flow.Core.NodeId> expected,
        IReadOnlyList<Flow.Core.NodeId> actual,
        bool essential)
    {
        if (expected.Count == 0)
        {
            return Absent(kind, essential);
        }

        var resolved = expected.Zip(actual).Count(static pair => pair.First == pair.Second);
        var broken = Math.Max(expected.Count, actual.Count) - resolved;
        return Present(kind, Math.Max(expected.Count, actual.Count), resolved, broken, 0, essential);
    }

    private static EpubLargePublicationReferenceAudit CompareIds(
        EpubLargePublicationAuditKind kind,
        IEnumerable<string> expectedValues,
        IEnumerable<string> actualValues,
        bool essential)
    {
        var expected = expectedValues.ToArray();
        if (expected.Length == 0)
        {
            return Absent(kind, essential);
        }

        var actual = actualValues.ToArray();
        var duplicateCount = actual.Length - actual.Distinct(StringComparer.Ordinal).Count();
        var resolved = expected.Intersect(actual, StringComparer.Ordinal).Count();
        var broken = expected.Length - resolved;
        return Present(kind, expected.Length + duplicateCount, resolved, broken, duplicateCount, essential);
    }

    private static bool? IsSameResource(
        Flow.Core.NodeId source,
        Flow.Core.NodeId target,
        EpubSourceMap? sourceMap)
    {
        var sourcePaths = sourceMap?.GetLocations(source).Select(static item => item.ResourcePath).ToHashSet(StringComparer.Ordinal) ?? [];
        var targetPaths = sourceMap?.GetLocations(target).Select(static item => item.ResourcePath).ToHashSet(StringComparer.Ordinal) ?? [];
        return sourcePaths.Count == 0 || targetPaths.Count == 0
            ? null
            : sourcePaths.Overlaps(targetPaths);
    }

    private static bool IsPassiveSvg(ReadOnlySpan<byte> data)
    {
        var text = Encoding.UTF8.GetString(data);
        return !text.Contains("<script", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("javascript:", StringComparison.OrdinalIgnoreCase)
            && !System.Text.RegularExpressions.Regex.IsMatch(text, "\\son[a-z]+\\s*=", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static IEnumerable<NodeLink> EnumerateLinks(FlowDocument document)
    {
        foreach (var location in document.Index.Locations)
        {
            foreach (var link in EnumerateInline(location.Node).OfType<Link>())
            {
                yield return new NodeLink(location.Node, link);
            }
        }
    }

    private static IEnumerable<InlineNode> EnumerateInline(FlowDocument document) =>
        document.Index.Locations.SelectMany(static location => EnumerateInline(location.Node));

    private static IEnumerable<NodeFootnoteReference> EnumerateFootnoteReferences(FlowDocument document)
    {
        foreach (var location in document.Index.Locations)
        {
            foreach (var reference in EnumerateInline(location.Node).OfType<FootnoteReference>())
            {
                yield return new NodeFootnoteReference(location.Node, reference);
            }
        }
    }

    private static IEnumerable<InlineNode> EnumerateInline(DocumentNode node)
    {
        var roots = node switch
        {
            Heading value => value.Content,
            Paragraph value => value.Content,
            Caption value => value.Content,
            TableOfContents value => value.Title.Concat(value.Entries.SelectMany(static entry => entry.Label)),
            _ => [],
        };
        foreach (var root in roots)
        {
            yield return root;
            foreach (var child in EnumerateInlineChildren(root))
            {
                yield return child;
            }
        }
    }

    private static IEnumerable<InlineNode> EnumerateInlineChildren(InlineNode node)
    {
        var children = node switch
        {
            InlineContainerNode value => value.Children,
            FootnoteReference value => value.Label,
            _ => [],
        };
        foreach (var child in children)
        {
            yield return child;
            foreach (var descendant in EnumerateInlineChildren(child))
            {
                yield return descendant;
            }
        }
    }

    private static IEnumerable<LayoutNode> EnumerateLayoutNodes(IEnumerable<LayoutNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in EnumerateLayoutNodes(node.Children))
            {
                yield return child;
            }
        }
    }

    private static EpubLargePublicationReferenceAudit Present(
        EpubLargePublicationAuditKind kind,
        int found,
        int resolved,
        int broken,
        int ambiguous,
        bool essential,
        int approximated = 0,
        int skipped = 0) => new(
        kind,
        EpubLargePublicationAuditApplicability.Present,
        new EpubLargePublicationReferenceCounts(found, resolved, broken, ambiguous, approximated, skipped),
        essential);

    private static EpubLargePublicationReferenceAudit Absent(EpubLargePublicationAuditKind kind, bool essential) => new(
        kind,
        EpubLargePublicationAuditApplicability.Absent,
        new EpubLargePublicationReferenceCounts(0, 0, 0, 0, 0, 0),
        essential);

    private static EpubLargePublicationReferenceAudit NotApplicable(EpubLargePublicationAuditKind kind, bool essential) => new(
        kind,
        EpubLargePublicationAuditApplicability.NotApplicable,
        new EpubLargePublicationReferenceCounts(0, 0, 0, 0, 0, 0),
        essential);

    private sealed record NodeLink(DocumentNode Owner, Link Link);

    private sealed record NodeFootnoteReference(DocumentNode Owner, FootnoteReference Reference);
}
