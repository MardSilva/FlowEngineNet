using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Xml.Linq;
using Flow.Core;
using Flow.Documents;
using Flow.Layout;

namespace Flow.Rendering.Html;

/// <summary>Produces a deterministic, script-free HTML book with one logical file per chapter.</summary>
public sealed class HtmlBookPackageRenderer : IHtmlBookPackageRenderer
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <inheritdoc />
    public HtmlBookPackage Render(
        FlowDocument document,
        LayoutDocument layout,
        UserReadingPreferences userPreferences,
        HtmlBookIntegrity integrity)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(userPreferences);
        ArgumentNullException.ThrowIfNull(integrity);

        // Reuse the established renderer as the single semantic HTML mapping and validation authority.
        var standalone = new HtmlDocumentRenderer().RenderToString(document, layout, userPreferences);
        var source = XDocument.Parse(standalone, LoadOptions.PreserveWhitespace);
        var sourceArticle = source.Root?.Element("body")?.Element("article")
            ?? throw new InvalidOperationException("The standalone renderer did not produce its required article.");
        var sharedCss = source.Root?.Element("head")?.Element("style")?.Value
            ?? throw new InvalidOperationException("The standalone renderer did not produce its required typed CSS.");

        var assetPlan = CreateAssetPlan(document);
        var pagePlan = CreatePagePlan(layout);
        var elementsById = sourceArticle
            .DescendantsAndSelf()
            .Where(static element => element.Attribute("id") is not null)
            .ToDictionary(static element => (string)element.Attribute("id")!, StringComparer.Ordinal);
        var pageByNodeId = pagePlan.Pages
            .SelectMany(static page => page.NodeIds.Select(id => (id.Value, page.Path)))
            .ToDictionary(static item => item.Value, static item => item.Path, StringComparer.Ordinal);

        var files = new List<HtmlBookFile>();
        files.Add(new HtmlBookFile(
            "styles/book.css",
            "text/css; charset=utf-8",
            Utf8WithoutBom.GetBytes(CreateSharedCss(sharedCss))));
        foreach (var asset in assetPlan.Files)
        {
            files.Add(new HtmlBookFile(asset.Path, asset.MediaType, asset.Content));
        }

        foreach (var page in pagePlan.Pages)
        {
            var roots = GetPageRoots(page, pagePlan, elementsById);
            if (page.Kind == HtmlBookPageKind.Index)
            {
                roots.InsertRange(0, CreateIndexMatter(document, assetPlan, pageByNodeId));
            }
            else if (page.Kind == HtmlBookPageKind.TableOfContents && roots.Count == 0)
            {
                roots.Add(CreateGeneratedTableOfContents(document, pagePlan));
            }

            foreach (var root in roots)
            {
                PruneNodesOwnedByOtherPages(root, page.Path, pageByNodeId);
                RewriteLinks(root, page.Path, pageByNodeId);
                RewriteAssets(root, page.Path, document, assetPlan);
            }

            var pageBytes = CreateHtmlPage(document, integrity, page, pagePlan.Pages, roots);
            files.Add(new HtmlBookFile(page.Path, "text/html; charset=utf-8", pageBytes));
        }

        var payloadFiles = files.OrderBy(static file => file.Path, StringComparer.Ordinal).ToArray();
        files.Add(new HtmlBookFile(
            "manifest.json",
            "application/json; charset=utf-8",
            CreateManifest(document, integrity, pagePlan.Pages, payloadFiles)));
        return new HtmlBookPackage(files);
    }

    private static PagePlan CreatePagePlan(LayoutDocument layout)
    {
        var pages = new List<PageBuilder>
        {
            new("index.html", HtmlBookPageKind.Index, null),
            new("toc.html", HtmlBookPageKind.TableOfContents, null),
        };
        var owner = pages[0];
        var tableOfContentsOwner = pages[1];
        var chapterNumber = 0;
        foreach (var topLevel in layout.Nodes)
        {
            if (topLevel.SemanticNode is TableOfContents)
            {
                AssignTree(topLevel, tableOfContentsOwner, tableOfContentsOwner);
                continue;
            }

            if (topLevel.SemanticNode is Chapter)
            {
                chapterNumber++;
                owner = new PageBuilder(
                    $"chapters/chapter-{chapterNumber.ToString("000", CultureInfo.InvariantCulture)}.html",
                    HtmlBookPageKind.Chapter,
                    topLevel.SemanticId);
                pages.Add(owner);
            }

            owner.RootNodeIds.Add(topLevel.SemanticId);
            AssignTree(topLevel, owner, tableOfContentsOwner);
        }

        return new PagePlan(pages.Select(static page => page.Build()).ToArray());

        static void AssignTree(
            LayoutNode node,
            PageBuilder inheritedOwner,
            PageBuilder tableOfContentsOwner)
        {
            var actualOwner = node.SemanticNode is TableOfContents
                ? tableOfContentsOwner
                : inheritedOwner;
            if (node.SemanticNode is TableOfContents
                && !tableOfContentsOwner.RootNodeIds.Contains(node.SemanticId))
            {
                tableOfContentsOwner.RootNodeIds.Add(node.SemanticId);
            }

            actualOwner.NodeIds.Add(node.SemanticId);
            foreach (var child in node.Children)
            {
                AssignTree(child, actualOwner, tableOfContentsOwner);
            }
        }
    }

    private static List<XElement> GetPageRoots(
        HtmlBookPage page,
        PagePlan plan,
        IReadOnlyDictionary<string, XElement> elementsById)
    {
        var roots = new List<XElement>();
        if (page.Kind == HtmlBookPageKind.TableOfContents)
        {
            foreach (var tocId in page.RootNodeIds)
            {
                if (elementsById.TryGetValue(tocId.Value, out var toc))
                {
                    roots.Add(new XElement(toc));
                }
            }

            return roots;
        }

        foreach (var nodeId in page.RootNodeIds)
        {
            if (!elementsById.TryGetValue(nodeId.Value, out var element))
            {
                throw new InvalidOperationException($"Rendered semantic node '{nodeId}' could not be located.");
            }

            roots.Add(new XElement(element));
        }

        return roots;
    }

    private static IEnumerable<XElement> CreateIndexMatter(
        FlowDocument document,
        AssetPlan assets,
        IReadOnlyDictionary<string, string> pageByNodeId)
    {
        if (document.Presentation?.Cover is { } cover
            && document.Index.TryGetUniqueNode(cover.FigureId, out var coverNode)
            && coverNode is Figure figure
            && assets.PathsByAssetId.TryGetValue(figure.AssetId, out var assetPath)
            && pageByNodeId.TryGetValue(figure.Id.Value, out var coverPage)
            && !string.Equals(coverPage, "index.html", StringComparison.Ordinal))
        {
            yield return new XElement(
                "figure",
                new XAttribute("data-publication-role", "cover-preview"),
                new XElement(
                    "a",
                    new XAttribute("href", RelativeReference("index.html", coverPage) + "#" + figure.Id.Value),
                    new XElement(
                        "img",
                        new XAttribute("src", RelativeReference("index.html", assetPath)),
                        new XAttribute("alt", figure.AlternativeText ?? document.Metadata.Title))));
        }

        yield return new XElement(
            "header",
            new XAttribute("class", "book-title-page"),
            new XElement("h1", document.Metadata.Title),
            document.Metadata.Subtitle is null ? null : new XElement("p", new XAttribute("class", "subtitle"), document.Metadata.Subtitle),
            document.Metadata.Authors.IsEmpty
                ? null
                : new XElement("p", new XAttribute("class", "authors"), string.Join(", ", document.Metadata.Authors)));
    }

    private static XElement CreateGeneratedTableOfContents(FlowDocument document, PagePlan plan)
    {
        var list = new XElement("ol");
        foreach (var chapter in plan.Pages.Where(static page => page.Kind == HtmlBookPageKind.Chapter))
        {
            var label = chapter.ChapterId is not null
                && document.Index.TryGetUniqueNode(chapter.ChapterId, out var chapterNode)
                && chapterNode is Chapter chapterNodeValue
                    ? ChapterTitle(chapterNodeValue)
                    : null;
            list.Add(new XElement(
                "li",
                new XElement(
                    "a",
                    new XAttribute("href", chapter.Path + (chapter.ChapterId is null ? string.Empty : $"#{chapter.ChapterId.Value}")),
                    label ?? chapter.Path)));
        }

        return new XElement(
            "nav",
            new XAttribute("aria-label", "Table of contents"),
            new XAttribute("data-generated", "true"),
            new XElement("h1", "Table of contents"),
            list);
    }

    private static string? ChapterTitle(Chapter chapter)
    {
        var heading = chapter.Children.OfType<Heading>().FirstOrDefault();
        return heading is null ? null : InlineText(heading.Content);
    }

    private static string InlineText(IEnumerable<InlineNode> nodes) => string.Concat(nodes.Select(static node => node switch
    {
        Text text => text.Value,
        InlineCode code => code.Code,
        InlineContainerNode container => InlineText(container.Children),
        FootnoteReference reference => InlineText(reference.Label),
        _ => string.Empty,
    }));

    private static void PruneNodesOwnedByOtherPages(
        XElement root,
        string currentPage,
        IReadOnlyDictionary<string, string> pageByNodeId)
    {
        foreach (var element in root.Descendants().Reverse().ToArray())
        {
            var id = (string?)element.Attribute("id");
            if (id is not null
                && pageByNodeId.TryGetValue(id, out var owner)
                && !string.Equals(owner, currentPage, StringComparison.Ordinal))
            {
                element.Remove();
            }
        }
    }

    private static void RewriteLinks(
        XElement root,
        string currentPage,
        IReadOnlyDictionary<string, string> pageByNodeId)
    {
        foreach (var href in root.DescendantsAndSelf().Attributes("href"))
        {
            if (!href.Value.StartsWith('#') || href.Value.Length == 1)
            {
                continue;
            }

            var targetId = href.Value[1..];
            if (!pageByNodeId.TryGetValue(targetId, out var targetPage))
            {
                throw new InvalidOperationException($"Internal HTML target '{targetId}' has no package page.");
            }

            href.Value = string.Equals(currentPage, targetPage, StringComparison.Ordinal)
                ? $"#{targetId}"
                : $"{RelativeReference(currentPage, targetPage)}#{targetId}";
        }
    }

    private static void RewriteAssets(
        XElement root,
        string currentPage,
        FlowDocument document,
        AssetPlan assets)
    {
        foreach (var image in root.DescendantsAndSelf().Where(static element => element.Name.LocalName == "img"))
        {
            var figureId = image.AncestorsAndSelf()
                .Where(static element => element.Name.LocalName == "figure")
                .Select(static element => (string?)element.Attribute("id"))
                .FirstOrDefault(static id => id is not null);
            if (figureId is null
                || !NodeId.TryParse(figureId, out var nodeId)
                || !document.Index.TryGetUniqueNode(nodeId, out var node)
                || node is not Figure figure
                || !assets.PathsByAssetId.TryGetValue(figure.AssetId, out var assetPath))
            {
                continue;
            }

            image.SetAttributeValue("src", RelativeReference(currentPage, assetPath));
        }
    }

    private static byte[] CreateHtmlPage(
        FlowDocument document,
        HtmlBookIntegrity integrity,
        HtmlBookPage page,
        IReadOnlyList<HtmlBookPage> pages,
        IEnumerable<XElement> roots)
    {
        var title = page.Kind switch
        {
            HtmlBookPageKind.Index => document.Metadata.Title,
            HtmlBookPageKind.TableOfContents => $"Table of contents — {document.Metadata.Title}",
            HtmlBookPageKind.Chapter => $"{document.Metadata.Title} — {page.Path}",
            _ => throw new ArgumentOutOfRangeException(nameof(page)),
        };
        var article = new XElement(
            "article",
            new XAttribute("data-document-id", document.Identity.Id.Value),
            new XAttribute("data-document-version", document.Identity.Version ?? string.Empty),
            new XAttribute("data-canonical-hash", integrity.Hash),
            new XAttribute("data-page-kind", PageKind(page.Kind)),
            roots);
        var navigation = CreatePageNavigation(page, pages);
        var html = new XElement(
            "html",
            document.Metadata.Language is null ? null : new XAttribute("lang", document.Metadata.Language),
            new XElement(
                "head",
                new XElement("meta", new XAttribute("charset", "utf-8")),
                new XElement("meta", new XAttribute("name", "viewport"), new XAttribute("content", "width=device-width, initial-scale=1")),
                new XElement(
                    "meta",
                    new XAttribute("http-equiv", "Content-Security-Policy"),
                    new XAttribute("content", "default-src 'none'; img-src 'self'; style-src 'self'")),
                new XElement("title", title),
                new XElement(
                    "link",
                    new XAttribute("rel", "stylesheet"),
                    new XAttribute("href", RelativeReference(page.Path, "styles/book.css")))),
            new XElement("body", navigation, article, new XElement(navigation)));
        var htmlDocument = new XDocument(new XDocumentType("html", null, null, null), html);
        return Utf8WithoutBom.GetBytes(NormalizeLineEndings(htmlDocument.ToString(SaveOptions.DisableFormatting)) + "\n");
    }

    private static XElement CreatePageNavigation(HtmlBookPage page, IReadOnlyList<HtmlBookPage> pages)
    {
        var index = Enumerable.Range(0, pages.Count)
            .Single(position => ReferenceEquals(pages[position], page));
        var navigation = new XElement("nav", new XAttribute("class", "book-navigation"), new XAttribute("aria-label", "Book navigation"));
        if (index > 0)
        {
            navigation.Add(new XElement(
                "a",
                new XAttribute("rel", "prev"),
                new XAttribute("href", RelativeReference(page.Path, pages[index - 1].Path)),
                "Previous"));
        }

        navigation.Add(new XElement(
            "a",
            new XAttribute("rel", "contents"),
            new XAttribute("href", RelativeReference(page.Path, "toc.html")),
            "Contents"));
        if (index + 1 < pages.Count)
        {
            navigation.Add(new XElement(
                "a",
                new XAttribute("rel", "next"),
                new XAttribute("href", RelativeReference(page.Path, pages[index + 1].Path)),
                "Next"));
        }

        return navigation;
    }

    private static string CreateSharedCss(string rendererCss) => NormalizeLineEndings(rendererCss).TrimEnd() + "\n" + """
        .book-navigation { display: flex; gap: 1rem; justify-content: space-between; margin: 0 auto; max-width: 72rem; padding: 0.75rem 1rem; }
        .book-title-page { margin: 2rem auto; max-width: 42rem; padding: 1rem; text-align: center; }
        figure[data-publication-role="cover-preview"] { margin: 1rem auto; max-width: 32rem; }
        figure[data-publication-role="cover-preview"] img { display: block; height: auto; max-width: 100%; width: 100%; }
        """ + "\n";

    private static AssetPlan CreateAssetPlan(FlowDocument document)
    {
        var paths = new Dictionary<AssetId, string>();
        var files = new List<AssetFile>();
        foreach (var group in document.Assets.Values
                     .OrderBy(static asset => asset.Id.Value, StringComparer.Ordinal)
                     .GroupBy(static asset => Sha256(asset.Data.AsSpan()), StringComparer.Ordinal))
        {
            var mediaType = group.Select(static asset => asset.MediaType).Order(StringComparer.Ordinal).First();
            var path = $"assets/{group.Key.ToLowerInvariant()}{Extension(mediaType)}";
            var first = group.First();
            files.Add(new AssetFile(path, mediaType, first.Data.ToArray()));
            foreach (var asset in group)
            {
                paths.Add(asset.Id, path);
            }
        }

        return new AssetPlan(paths, files);
    }

    private static byte[] CreateManifest(
        FlowDocument document,
        HtmlBookIntegrity integrity,
        IReadOnlyList<HtmlBookPage> readingOrder,
        IEnumerable<HtmlBookFile> payloadFiles)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(
                   buffer,
                   new JsonWriterOptions
                   {
                       Indented = true,
                       Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
                   }))
        {
            writer.WriteStartObject();
            writer.WriteString("format", HtmlBookPackage.Format);
            writer.WritePropertyName("identity");
            writer.WriteStartObject();
            writer.WriteString("id", document.Identity.Id.Value);
            WriteNullableString(writer, "version", document.Identity.Version);
            writer.WriteString("title", document.Metadata.Title);
            writer.WriteEndObject();
            writer.WritePropertyName("canonicalIntegrity");
            writer.WriteStartObject();
            writer.WriteString("algorithm", integrity.Algorithm);
            writer.WriteString("hash", integrity.Hash);
            writer.WriteString("canonicalizationVersion", integrity.CanonicalizationVersion);
            writer.WriteEndObject();
            writer.WritePropertyName("readingOrder");
            writer.WriteStartArray();
            foreach (var page in readingOrder)
            {
                writer.WriteStartObject();
                writer.WriteString("path", page.Path);
                writer.WriteString("kind", PageKind(page.Kind));
                WriteNullableString(writer, "chapterId", page.ChapterId?.Value);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WritePropertyName("files");
            writer.WriteStartArray();
            foreach (var file in payloadFiles.OrderBy(static file => file.Path, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("path", file.Path);
                writer.WriteString("mediaType", file.MediaType);
                writer.WriteNumber("bytes", file.Content.Length);
                writer.WriteString("sha256", Sha256(file.Content.AsSpan()).ToLowerInvariant());
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteString("manifestPath", "manifest.json");
            writer.WriteBoolean("manifestSelfHashExcluded", true);
            writer.WriteEndObject();
            writer.Flush();
        }

        return Utf8WithoutBom.GetBytes(NormalizeLineEndings(Utf8WithoutBom.GetString(buffer.ToArray())));
    }

    private static void WriteNullableString(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, value);
        }
    }

    private static string RelativeReference(string sourcePath, string targetPath)
    {
        var sourceSegments = sourcePath.Split('/');
        var targetSegments = targetPath.Split('/');
        var sourceDirectoryLength = sourceSegments.Length - 1;
        var common = 0;
        while (common < sourceDirectoryLength
               && common < targetSegments.Length - 1
               && string.Equals(sourceSegments[common], targetSegments[common], StringComparison.Ordinal))
        {
            common++;
        }

        return string.Concat(Enumerable.Repeat("../", sourceDirectoryLength - common))
               + string.Join('/', targetSegments.Skip(common));
    }

    private static string Extension(string mediaType) => mediaType.ToLowerInvariant() switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        "image/svg+xml" => ".svg",
        _ => ".bin",
    };

    private static string Sha256(ReadOnlySpan<byte> content) => Convert.ToHexString(SHA256.HashData(content));

    private static string NormalizeLineEndings(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal)
        .Replace('\r', '\n');

    private static string PageKind(HtmlBookPageKind kind) => kind switch
    {
        HtmlBookPageKind.Index => "index",
        HtmlBookPageKind.TableOfContents => "tableOfContents",
        HtmlBookPageKind.Chapter => "chapter",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private sealed record AssetFile(string Path, string MediaType, byte[] Content);

    private sealed record AssetPlan(
        IReadOnlyDictionary<AssetId, string> PathsByAssetId,
        IReadOnlyList<AssetFile> Files);

    private sealed record PagePlan(IReadOnlyList<HtmlBookPage> Pages);

    private sealed record HtmlBookPage(
        string Path,
        HtmlBookPageKind Kind,
        NodeId? ChapterId,
        IReadOnlyList<NodeId> RootNodeIds,
        IReadOnlyList<NodeId> NodeIds);

    private sealed class PageBuilder(string path, HtmlBookPageKind kind, NodeId? chapterId)
    {
        internal string Path { get; } = path;

        internal HtmlBookPageKind Kind { get; } = kind;

        internal NodeId? ChapterId { get; } = chapterId;

        internal List<NodeId> RootNodeIds { get; } = [];

        internal List<NodeId> NodeIds { get; } = [];

        internal HtmlBookPage Build() => new(Path, Kind, ChapterId, RootNodeIds.ToArray(), NodeIds.ToArray());
    }

    private enum HtmlBookPageKind
    {
        Index,
        TableOfContents,
        Chapter,
    }
}
