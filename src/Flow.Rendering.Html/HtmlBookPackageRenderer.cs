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
        HtmlBookIntegrity integrity) =>
        Render(document, layout, userPreferences, integrity, new HtmlBookPackageOptions());

    /// <inheritdoc />
    public HtmlBookPackage Render(
        FlowDocument document,
        LayoutDocument layout,
        UserReadingPreferences userPreferences,
        HtmlBookIntegrity integrity,
        HtmlBookPackageOptions options) =>
        Render(document, layout, userPreferences, integrity, options, default);

    /// <inheritdoc />
    public HtmlBookPackage Render(
        FlowDocument document,
        LayoutDocument layout,
        UserReadingPreferences userPreferences,
        HtmlBookIntegrity integrity,
        HtmlBookPackageOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(userPreferences);
        ArgumentNullException.ThrowIfNull(integrity);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        // Reuse the established renderer as the single semantic HTML mapping and validation authority.
        var standalone = new HtmlDocumentRenderer().RenderToString(document, layout, userPreferences);
        var source = XDocument.Parse(standalone, LoadOptions.PreserveWhitespace);
        var sourceArticle = source.Root?.Element("body")?.Element("article")
            ?? throw new InvalidOperationException("The standalone renderer did not produce its required article.");
        var sharedCss = source.Root?.Element("head")?.Element("style")?.Value
            ?? throw new InvalidOperationException("The standalone renderer did not produce its required typed CSS.");
        var ui = BookUiText.For(options.UiLanguage, document.Metadata.Language);

        var assetPlan = CreateAssetPlan(document, cancellationToken);
        var pagePlan = CreatePagePlan(layout, cancellationToken);
        var elementsById = sourceArticle
            .DescendantsAndSelf()
            .Where(static element => element.Attribute("id") is not null)
            .ToDictionary(static element => (string)element.Attribute("id")!, StringComparer.Ordinal);
        var originalPageByNodeId = pagePlan.Pages
            .SelectMany(static page => page.NodeIds.Select(id => (id.Value, page.Path)))
            .ToDictionary(static item => item.Value, static item => item.Path, StringComparer.Ordinal);
        var footnotePlan = CreateFootnotePlan(sourceArticle, pagePlan, originalPageByNodeId, cancellationToken);
        pagePlan = AddFootnoteBackMatterPage(pagePlan, footnotePlan);
        var pageByNodeId = new Dictionary<string, string>(originalPageByNodeId, StringComparer.Ordinal);
        foreach (var ownership in footnotePlan.OwnershipByNodeId)
        {
            cancellationToken.ThrowIfCancellationRequested();
            pageByNodeId[ownership.Key] = ownership.Value;
        }

        var files = new List<HtmlBookFile>();
        files.Add(HtmlBookFile.FromOwnedBytes(
            "styles/book.css",
            "text/css; charset=utf-8",
            Utf8WithoutBom.GetBytes(CreateSharedCss(sharedCss))));
        foreach (var asset in assetPlan.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            files.Add(HtmlBookFile.FromOwnedBytes(asset.Path, asset.MediaType, asset.Content));
        }

        foreach (var page in pagePlan.Pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var roots = GetPageRoots(page, pagePlan, elementsById);
            if (page.Kind == HtmlBookPageKind.Index)
            {
                roots.InsertRange(0, CreateIndexMatter(document, assetPlan, pageByNodeId, ui));
            }
            else if (page.Kind == HtmlBookPageKind.TableOfContents && roots.Count == 0)
            {
                roots.Add(CreateGeneratedTableOfContents(document, pagePlan, ui));
            }

            if (page.Kind == HtmlBookPageKind.TableOfContents)
            {
                PrepareTableOfContents(roots, ui);
            }

            RemoveOriginalFootnotes(roots);
            var assignedFootnotes = footnotePlan.FootnoteIdsByPage.GetValueOrDefault(page.Path, []);
            if (assignedFootnotes.Count > 0)
            {
                roots.Add(CreateFootnoteCollection(
                    assignedFootnotes,
                    elementsById,
                    ui,
                    page.Kind == HtmlBookPageKind.Notes));
            }

            foreach (var root in roots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PruneNodesOwnedByOtherPages(root, page.Path, pageByNodeId);
                RewriteLinks(root, page.Path, pageByNodeId);
                RewriteAssets(root, page.Path, document, assetPlan);
            }

            var pageBytes = CreateHtmlPage(
                document,
                integrity,
                page,
                pagePlan.Pages,
                roots,
                layout.ReadingStyle.Theme,
                ui);
            files.Add(HtmlBookFile.FromOwnedBytes(page.Path, "text/html; charset=utf-8", pageBytes));
        }

        var payloadFiles = files.OrderBy(static file => file.Path, StringComparer.Ordinal).ToArray();
        files.Add(HtmlBookFile.FromOwnedBytes(
            "manifest.json",
            "application/json; charset=utf-8",
            CreateManifest(document, integrity, pagePlan.Pages, payloadFiles, ui, cancellationToken)));
        return new HtmlBookPackage(files);
    }

    private static PagePlan CreatePagePlan(LayoutDocument layout, CancellationToken cancellationToken)
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
            cancellationToken.ThrowIfCancellationRequested();
            if (topLevel.SemanticNode is TableOfContents)
            {
                AssignTree(topLevel, tableOfContentsOwner, tableOfContentsOwner, cancellationToken);
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
            AssignTree(topLevel, owner, tableOfContentsOwner, cancellationToken);
        }

        return new PagePlan(pages.Select(static page => page.Build()).ToArray());

        static void AssignTree(
            LayoutNode node,
            PageBuilder inheritedOwner,
            PageBuilder tableOfContentsOwner,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
                AssignTree(child, actualOwner, tableOfContentsOwner, cancellationToken);
            }
        }
    }

    private static FootnotePlan CreateFootnotePlan(
        XElement sourceArticle,
        PagePlan pagePlan,
        IReadOnlyDictionary<string, string> originalPageByNodeId,
        CancellationToken cancellationToken)
    {
        const string backMatterPath = "backmatter/notes.html";
        var chapterPaths = pagePlan.Pages
            .Where(static page => page.Kind == HtmlBookPageKind.Chapter)
            .Select(static page => page.Path)
            .ToHashSet(StringComparer.Ordinal);
        var referencePagesByFootnoteId = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var reference in sourceArticle
                     .Descendants("a")
                     .Where(static element => (string?)element.Attribute("role") == "doc-noteref"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var href = (string?)reference.Attribute("href");
            if (href is null || !href.StartsWith('#') || href.Length == 1)
            {
                continue;
            }

            var ownerPage = reference.AncestorsAndSelf()
                .Select(static element => (string?)element.Attribute("id"))
                .Where(static id => id is not null)
                .Select(id => originalPageByNodeId.GetValueOrDefault(id!))
                .FirstOrDefault(static path => path is not null);
            if (ownerPage is null)
            {
                continue;
            }

            if (!referencePagesByFootnoteId.TryGetValue(href[1..], out var referencePages))
            {
                referencePages = new HashSet<string>(StringComparer.Ordinal);
                referencePagesByFootnoteId.Add(href[1..], referencePages);
            }

            referencePages.Add(ownerPage);
        }

        var footnoteIdsByPage = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var ownershipByNodeId = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var footnote in sourceArticle
                     .DescendantsAndSelf()
                     .Where(static element => (string?)element.Attribute("role") == "doc-footnote"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var footnoteId = (string?)footnote.Attribute("id")
                ?? throw new InvalidOperationException("A rendered footnote did not preserve its semantic ID.");
            var targetPage = referencePagesByFootnoteId.TryGetValue(footnoteId, out var referencePages)
                             && referencePages.Count == 1
                             && chapterPaths.Contains(referencePages.Single())
                ? referencePages.Single()
                : backMatterPath;
            if (!footnoteIdsByPage.TryGetValue(targetPage, out var assignedFootnotes))
            {
                assignedFootnotes = [];
                footnoteIdsByPage.Add(targetPage, assignedFootnotes);
            }

            assignedFootnotes.Add(footnoteId);
            foreach (var semanticId in footnote.DescendantsAndSelf().Attributes("id"))
            {
                ownershipByNodeId[(string)semanticId] = targetPage;
            }
        }

        return new FootnotePlan(footnoteIdsByPage, ownershipByNodeId, backMatterPath);
    }

    private static PagePlan AddFootnoteBackMatterPage(PagePlan pagePlan, FootnotePlan footnotePlan)
    {
        if (!footnotePlan.FootnoteIdsByPage.TryGetValue(footnotePlan.BackMatterPath, out var footnoteIds))
        {
            return pagePlan;
        }

        var semanticIds = footnoteIds
            .Select(NodeId.Parse)
            .ToArray();
        return new PagePlan(
            [
                .. pagePlan.Pages,
                new HtmlBookPage(
                    footnotePlan.BackMatterPath,
                    HtmlBookPageKind.Notes,
                    null,
                    [],
                    semanticIds),
            ]);
    }

    private static void RemoveOriginalFootnotes(List<XElement> roots)
    {
        roots.RemoveAll(static root => (string?)root.Attribute("role") == "doc-footnote");
        foreach (var footnote in roots
                     .SelectMany(static root => root.Descendants())
                     .Where(static element => (string?)element.Attribute("role") == "doc-footnote")
                     .ToArray())
        {
            footnote.Remove();
        }
    }

    private static XElement CreateFootnoteCollection(
        IReadOnlyList<string> footnoteIds,
        IReadOnlyDictionary<string, XElement> elementsById,
        BookUiText ui,
        bool isBackMatter)
    {
        var collection = new XElement(
            "section",
            new XAttribute("class", "page-notes"),
            new XAttribute("data-notes-placement", isBackMatter ? "backMatter" : "chapter"),
            isBackMatter ? new XAttribute("role", "doc-endnotes") : null,
            new XAttribute("aria-label", ui.Notes),
            new XElement("h2", ui.Notes));
        foreach (var footnoteId in footnoteIds)
        {
            if (!elementsById.TryGetValue(footnoteId, out var footnote))
            {
                throw new InvalidOperationException($"Rendered footnote '{footnoteId}' could not be located.");
            }

            collection.Add(new XElement(footnote));
        }

        return collection;
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
        IReadOnlyDictionary<string, string> pageByNodeId,
        BookUiText ui)
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
                : new XElement("p", new XAttribute("class", "authors"), string.Join(", ", document.Metadata.Authors)),
            new XElement(
                "p",
                new XAttribute("class", "start-reading"),
                new XElement("a", new XAttribute("href", "toc.html"), ui.OpenContents)));

        if (!document.Metadata.Authors.IsEmpty)
        {
            yield return new XElement(
                "section",
                new XAttribute("class", "book-credits"),
                new XAttribute("aria-label", ui.Credits),
                new XElement("h2", ui.Credits),
                new XElement(
                    "dl",
                    new XElement("dt", document.Metadata.Authors.Length == 1 ? ui.Author : ui.Authors),
                    document.Metadata.Authors.Select(static author => new XElement("dd", author))));
        }
    }

    private static XElement CreateGeneratedTableOfContents(
        FlowDocument document,
        PagePlan plan,
        BookUiText ui)
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
                new XAttribute("data-level", "1"),
                new XElement(
                    "a",
                    new XAttribute("href", chapter.Path + (chapter.ChapterId is null ? string.Empty : $"#{chapter.ChapterId.Value}")),
                    label ?? chapter.Path)));
        }

        return new XElement(
            "nav",
            new XAttribute("aria-label", ui.TableOfContents),
            new XAttribute("data-generated", "true"),
            new XElement("h1", ui.TableOfContents),
            list);
    }

    private static void PrepareTableOfContents(IEnumerable<XElement> roots, BookUiText ui)
    {
        foreach (var navigation in roots
                     .SelectMany(static root => root.DescendantsAndSelf())
                     .Where(static element => element.Name.LocalName == "nav"))
        {
            navigation.SetAttributeValue("aria-label", ui.TableOfContents);
            var sourceList = navigation.Elements("ol").SingleOrDefault();
            if (sourceList is null)
            {
                continue;
            }

            var items = sourceList.Elements("li").ToArray();
            sourceList.RemoveNodes();
            var parentAtLevel = new List<XElement>();
            foreach (var item in items)
            {
                if (!int.TryParse(
                        (string?)item.Attribute("data-level"),
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var level)
                    || level < 1
                    || level > parentAtLevel.Count + 1)
                {
                    throw new InvalidOperationException("The rendered table of contents has an invalid level sequence.");
                }

                if (level == 1)
                {
                    sourceList.Add(item);
                }
                else
                {
                    var parent = parentAtLevel[level - 2];
                    var nestedList = parent.Elements("ol").LastOrDefault();
                    if (nestedList is null)
                    {
                        nestedList = new XElement("ol");
                        parent.Add(nestedList);
                    }

                    nestedList.Add(item);
                }

                if (parentAtLevel.Count >= level)
                {
                    parentAtLevel.RemoveRange(level - 1, parentAtLevel.Count - level + 1);
                }

                parentAtLevel.Add(item);
            }
        }
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
        IEnumerable<XElement> roots,
        ReadingTheme defaultTheme,
        BookUiText ui)
    {
        var rootElements = roots.ToArray();
        var usedIds = rootElements
            .SelectMany(static root => root.DescendantsAndSelf())
            .Attributes("id")
            .Select(static attribute => attribute.Value)
            .ToHashSet(StringComparer.Ordinal);
        var mainId = AllocateUserInterfaceId(usedIds, "flow-reader-main");
        var preferenceIds = Enumerable.Range(1, 10)
            .Select(index => AllocateUserInterfaceId(usedIds, $"flow-reader-option-{index}"))
            .ToArray();
        var title = page.Kind switch
        {
            HtmlBookPageKind.Index => document.Metadata.Title,
            HtmlBookPageKind.TableOfContents => $"{ui.TableOfContents} — {document.Metadata.Title}",
            HtmlBookPageKind.Chapter => $"{ChapterPageTitle(document, page, ui)} — {document.Metadata.Title}",
            HtmlBookPageKind.Notes => $"{ui.Notes} — {document.Metadata.Title}",
            _ => throw new ArgumentOutOfRangeException(nameof(page)),
        };
        var article = new XElement(
            "article",
            new XAttribute("data-document-id", document.Identity.Id.Value),
            new XAttribute("data-document-version", document.Identity.Version ?? string.Empty),
            new XAttribute("data-canonical-hash", integrity.Hash),
            new XAttribute("data-page-kind", PageKind(page.Kind)),
            new XAttribute("data-publication-role", PublicationRole(document, page, pages, ui)),
            document.Metadata.Language is null ? null : new XAttribute("lang", document.Metadata.Language),
            rootElements);
        var progress = LogicalProgress(document, page, pages, ui);
        var shell = new XElement(
            "div",
            new XAttribute("class", "book-shell"),
            new XAttribute("lang", ui.LanguageTag),
            new XElement(
                "header",
                new XAttribute("class", "book-masthead"),
                new XElement(
                    "a",
                    new XAttribute("class", "book-home-link"),
                    new XAttribute("href", RelativeReference(page.Path, "index.html")),
                    document.Metadata.Language is null ? null : new XAttribute("lang", document.Metadata.Language),
                    document.Metadata.Title),
                new XElement(
                    "p",
                    new XAttribute("class", "logical-progress"),
                    new XAttribute("aria-label", ui.LogicalReadingPosition),
                    progress)),
            CreateReadingPreferences(preferenceIds, defaultTheme != ReadingTheme.HighContrast, ui),
            CreatePageNavigation(page, pages, primary: true, ui),
            new XElement(
                "main",
                new XAttribute("id", mainId),
                new XAttribute("class", "reading-surface"),
                new XAttribute("tabindex", "-1"),
                article),
            CreatePageNavigation(page, pages, primary: false, ui),
            new XElement(
                "footer",
                new XAttribute("class", "book-footer"),
                new XElement("p", progress)));
        var html = new XElement(
            "html",
            new XAttribute("data-default-theme", ThemeName(defaultTheme)),
            new XAttribute("lang", document.Metadata.Language ?? ui.LanguageTag),
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
            new XElement(
                "body",
                new XElement(
                    "a",
                    new XAttribute("class", "skip-link"),
                    new XAttribute("href", $"#{mainId}"),
                    new XAttribute("lang", ui.LanguageTag),
                    ui.SkipToBookContent),
                shell));
        var htmlDocument = new XDocument(new XDocumentType("html", null, null, null), html);
        return Utf8WithoutBom.GetBytes(NormalizeLineEndings(htmlDocument.ToString(SaveOptions.DisableFormatting)) + "\n");
    }

    private static XElement CreatePageNavigation(
        HtmlBookPage page,
        IReadOnlyList<HtmlBookPage> pages,
        bool primary,
        BookUiText ui)
    {
        var index = Enumerable.Range(0, pages.Count)
            .Single(position => ReferenceEquals(pages[position], page));
        var navigation = new XElement(
            "nav",
            new XAttribute("class", "book-navigation"),
            new XAttribute("aria-label", primary ? ui.PrimaryBookNavigation : ui.SecondaryBookNavigation));
        if (index > 0)
        {
            navigation.Add(new XElement(
                "a",
                new XAttribute("rel", "prev"),
                new XAttribute("href", RelativeReference(page.Path, pages[index - 1].Path)),
                new XAttribute("aria-label", ui.PreviousLogicalReadingPosition),
                ui.Previous));
        }

        navigation.Add(new XElement(
            "a",
            new XAttribute("rel", "contents"),
            new XAttribute("href", RelativeReference(page.Path, "toc.html")),
            page.Kind == HtmlBookPageKind.TableOfContents ? new XAttribute("aria-current", "page") : null,
            ui.Contents));
        if (index + 1 < pages.Count)
        {
            navigation.Add(new XElement(
                "a",
                new XAttribute("rel", "next"),
                new XAttribute("href", RelativeReference(page.Path, pages[index + 1].Path)),
                new XAttribute("aria-label", ui.NextLogicalReadingPosition),
                ui.Next));
        }

        return navigation;
    }

    private static XElement CreateReadingPreferences(
        IReadOnlyList<string> ids,
        bool allowThemeOverrides,
        BookUiText ui)
    {
        var controls = new List<(string Group, string Value, string Label, bool Checked)>
        {
            ("flow-reader-theme", "default", ui.Book, true),
            ("flow-reader-theme", "light", ui.Light, false),
            ("flow-reader-theme", "dark", ui.Dark, false),
            ("flow-reader-theme", "sepia", ui.Sepia, false),
            ("flow-reader-font", "book", ui.BookFont, true),
            ("flow-reader-font", "serif", ui.Serif, false),
            ("flow-reader-font", "sans", ui.SansSerif, false),
            ("flow-reader-scale", "book", ui.BookSize, true),
            ("flow-reader-scale", "large", ui.Large, false),
            ("flow-reader-scale", "larger", ui.Larger, false),
        };
        if (!allowThemeOverrides)
        {
            controls.RemoveAll(static control => control.Group == "flow-reader-theme" && control.Value != "default");
        }

        var fieldsets = controls
            .Select((control, index) => (control, index))
            .GroupBy(static item => item.control.Group, StringComparer.Ordinal)
            .Select(group => new XElement(
                "fieldset",
                new XElement("legend", group.Key switch
                {
                    "flow-reader-theme" => ui.Theme,
                    "flow-reader-font" => ui.Font,
                    _ => ui.TextSize,
                }),
                group.Select(item => new object[]
                {
                    new XElement(
                        "input",
                        new XAttribute("type", "radio"),
                        new XAttribute("id", ids[item.index]),
                        new XAttribute("name", item.control.Group),
                        new XAttribute("value", item.control.Value),
                        item.control.Checked ? new XAttribute("checked", "checked") : null),
                    new XElement("label", new XAttribute("for", ids[item.index]), item.control.Label),
                })));

        return new XElement(
            "aside",
            new XAttribute("class", "reading-preferences"),
            new XAttribute("aria-label", ui.ReadingPreferences),
            new XElement("details", new XElement("summary", ui.ReadingAppearance), fieldsets));
    }

    private static string CreateSharedCss(string rendererCss) => NormalizeLineEndings(rendererCss).TrimEnd() + "\n" + """
        :root {
          --book-background: Canvas;
          --book-foreground: CanvasText;
          --book-muted: GrayText;
          --book-surface: Canvas;
          --book-border: GrayText;
          --book-accent: LinkText;
          --book-reader-font: inherit;
          --book-reader-scale: 1;
        }
        html[data-default-theme="light"] { color-scheme: light; --book-background: #ffffff; --book-foreground: #1a1a1a; --book-muted: #555555; --book-surface: #f5f5f5; --book-border: #767676; --book-accent: #174ea6; }
        html[data-default-theme="dark"] { color-scheme: dark; --book-background: #171717; --book-foreground: #f2f2f2; --book-muted: #c3c3c3; --book-surface: #242424; --book-border: #a3a3a3; --book-accent: #8ab4f8; }
        html[data-default-theme="sepia"] { color-scheme: light; --book-background: #f4ecd8; --book-foreground: #3b2f24; --book-muted: #675849; --book-surface: #e9ddc2; --book-border: #77654f; --book-accent: #714b20; }
        html[data-default-theme="high-contrast"] { color-scheme: light dark; --book-background: Canvas; --book-foreground: CanvasText; --book-muted: CanvasText; --book-surface: Canvas; --book-border: CanvasText; --book-accent: LinkText; }
        @media (prefers-color-scheme: dark) {
          html[data-default-theme="system"] { color-scheme: dark; --book-background: #171717; --book-foreground: #f2f2f2; --book-muted: #c3c3c3; --book-surface: #242424; --book-border: #a3a3a3; --book-accent: #8ab4f8; }
        }
        body:has(input[name="flow-reader-theme"][value="light"]:checked) { color-scheme: light; --book-background: #ffffff; --book-foreground: #1a1a1a; --book-muted: #555555; --book-surface: #f5f5f5; --book-border: #767676; --book-accent: #174ea6; }
        body:has(input[name="flow-reader-theme"][value="dark"]:checked) { color-scheme: dark; --book-background: #171717; --book-foreground: #f2f2f2; --book-muted: #c3c3c3; --book-surface: #242424; --book-border: #a3a3a3; --book-accent: #8ab4f8; }
        body:has(input[name="flow-reader-theme"][value="sepia"]:checked) { color-scheme: light; --book-background: #f4ecd8; --book-foreground: #3b2f24; --book-muted: #675849; --book-surface: #e9ddc2; --book-border: #77654f; --book-accent: #714b20; }
        body:has(input[name="flow-reader-font"][value="serif"]:checked) { --book-reader-font: Georgia, "Times New Roman", serif; }
        body:has(input[name="flow-reader-font"][value="sans"]:checked) { --book-reader-font: system-ui, -apple-system, "Segoe UI", sans-serif; }
        body:has(input[name="flow-reader-scale"][value="large"]:checked) { --book-reader-scale: 1.15; }
        body:has(input[name="flow-reader-scale"][value="larger"]:checked) { --book-reader-scale: 1.3; }
        html, body { background: var(--book-background); color: var(--book-foreground); }
        body { overflow-wrap: anywhere; }
        a { color: var(--book-accent); text-underline-offset: 0.16em; }
        a:focus-visible, summary:focus-visible, input:focus-visible + label { outline: 0.2rem solid var(--book-accent); outline-offset: 0.2rem; }
        .skip-link { background: var(--book-foreground); color: var(--book-background); inset-block-start: 0.5rem; inset-inline-start: 0.5rem; padding: 0.75rem 1rem; position: fixed; transform: translateY(-200%); z-index: 10; }
        .skip-link:focus { transform: translateY(0); }
        .book-shell { background: var(--book-background); color: var(--book-foreground); min-height: 100vh; transition: background-color 150ms ease, color 150ms ease; }
        .book-masthead, .book-footer { align-items: baseline; border-color: var(--book-border); display: flex; gap: 1rem; justify-content: space-between; margin: 0 auto; max-width: 72rem; padding: 0.8rem 1rem; }
        .book-masthead { border-block-end: 1px solid var(--book-border); }
        .book-footer { border-block-start: 1px solid var(--book-border); color: var(--book-muted); }
        .book-home-link { font-weight: 700; }
        .logical-progress, .book-footer p { margin: 0; }
        .reading-preferences { margin: 0 auto; max-width: 72rem; padding: 0.5rem 1rem 0; }
        .reading-preferences details { background: var(--book-surface); border: 1px solid var(--book-border); border-radius: 0.35rem; padding: 0.5rem 0.75rem; }
        .reading-preferences summary { cursor: pointer; font-weight: 700; }
        .reading-preferences fieldset { border: 0; display: flex; flex-wrap: wrap; gap: 0.35rem; margin: 0.75rem 0 0; padding: 0; }
        .reading-preferences legend { float: inline-start; font-weight: 700; margin-inline-end: 0.75rem; padding: 0.35rem 0; }
        .reading-preferences input { block-size: 1px; inline-size: 1px; opacity: 0; position: absolute; }
        .reading-preferences label { border: 1px solid var(--book-border); border-radius: 999px; cursor: pointer; padding: 0.3rem 0.65rem; }
        .reading-preferences input:checked + label { background: var(--book-foreground); color: var(--book-background); }
        .book-navigation { display: flex; gap: 1rem; justify-content: space-between; margin: 0 auto; max-width: 72rem; min-height: 3rem; padding: 0.75rem 1rem; }
        .reading-surface { font-family: var(--book-reader-font); margin: 0 auto; max-width: 72rem; zoom: var(--book-reader-scale); }
        .reading-surface article { max-width: 46rem; overflow-x: auto; }
        .reading-surface article:is([data-publication-role="chapter"], [data-publication-role="section"]) > section:first-child { border-block-start: 0.35rem solid var(--book-border); margin-block-start: clamp(1rem, 8vh, 5rem); padding-block-start: clamp(1.5rem, 5vh, 3.5rem); }
        .reading-surface article:is([data-publication-role="chapter"], [data-publication-role="section"]) > section:first-child > [data-typography="chapter-title"]:first-child { text-wrap: balance; }
        .reading-surface nav li[data-level] { padding-inline-start: 0; }
        .reading-surface nav ol ol { padding-inline-start: 1.5rem; }
        .page-notes { border-block-start: 1px solid var(--book-border); margin: 4rem auto 1rem; padding-block-start: 1.5rem; }
        .page-notes > [role="doc-footnote"] { margin-block: 1rem; }
        .page-notes > [role="doc-footnote"]:target { outline: 0.2rem solid var(--book-accent); outline-offset: 0.35rem; }
        .book-title-page { margin: clamp(2rem, 10vh, 7rem) auto 2rem; max-width: 42rem; padding: 1rem; text-align: center; }
        .book-title-page h1 { text-wrap: balance; }
        .start-reading a { border: 1px solid var(--book-border); border-radius: 999px; display: inline-block; padding: 0.6rem 1rem; }
        .book-credits { border-block-start: 1px solid var(--book-border); margin: 3rem auto; max-width: 34rem; padding: 1.5rem 1rem; }
        .book-credits dl { display: grid; gap: 0.4rem 1rem; grid-template-columns: max-content 1fr; }
        .book-credits dt { font-weight: 700; }
        .book-credits dd { margin: 0; }
        figure[data-publication-role="cover-preview"] { margin: 1rem auto; max-width: 28rem; }
        figure[data-publication-role="cover-preview"] img { display: block; height: auto; max-width: 100%; width: 100%; }
        table { max-width: 100%; }
        ruby { ruby-position: over; }
        @media (max-width: 42rem) {
          .book-masthead { align-items: flex-start; flex-direction: column; gap: 0.25rem; }
          .reading-preferences legend { float: none; inline-size: 100%; }
          .book-navigation { flex-wrap: wrap; }
          .reading-surface article { padding-inline: clamp(1rem, 5vw, 1.5rem); }
          .book-title-page { margin-block-start: 2rem; }
        }
        @media (prefers-reduced-motion: reduce) {
          *, *::before, *::after { animation-duration: 0.01ms !important; animation-iteration-count: 1 !important; scroll-behavior: auto !important; transition-duration: 0.01ms !important; }
        }
        @media print {
          :root { color-scheme: light; --book-background: #ffffff; --book-foreground: #000000; --book-accent: #000000; }
          .skip-link, .book-masthead, .reading-preferences, .book-navigation, .book-footer { display: none !important; }
          .reading-surface, .reading-surface article { max-width: none; zoom: 1; }
          .reading-surface article { padding: 0; }
          a { color: inherit; text-decoration: underline; }
        }
        """ + "\n";

    private static string AllocateUserInterfaceId(ISet<string> usedIds, string preferred)
    {
        var candidate = preferred;
        var suffix = 2;
        while (!usedIds.Add(candidate))
        {
            candidate = $"{preferred}-{suffix.ToString(CultureInfo.InvariantCulture)}";
            suffix++;
        }

        return candidate;
    }

    private static string ChapterPageTitle(
        FlowDocument document,
        HtmlBookPage page,
        BookUiText ui)
    {
        if (page.ChapterId is not null
            && document.Index.TryGetUniqueNode(page.ChapterId, out var node)
            && node is Chapter chapter)
        {
            return ChapterTitle(chapter)
                   ?? (IsCoverPage(document, page) ? ui.Cover : ui.FrontMatter);
        }

        return ui.FrontMatter;
    }

    private static string LogicalProgress(
        FlowDocument document,
        HtmlBookPage page,
        IReadOnlyList<HtmlBookPage> pages,
        BookUiText ui)
    {
        if (page.Kind == HtmlBookPageKind.Index)
        {
            return ui.BookOpening;
        }

        if (page.Kind == HtmlBookPageKind.TableOfContents)
        {
            return ui.Contents;
        }

        if (page.Kind == HtmlBookPageKind.Notes)
        {
            return ui.Notes;
        }

        if (IsCoverPage(document, page))
        {
            return ui.Cover;
        }

        var pageTitle = ChapterPageTitle(document, page, ui);
        var numberedChapters = pages
            .Where(static candidate => candidate.Kind == HtmlBookPageKind.Chapter)
            .Where(candidate => IsNumberedChapterTitle(ChapterPageTitle(document, candidate, ui)))
            .ToArray();
        if (numberedChapters.Length > 0 && !IsNumberedChapterTitle(pageTitle))
        {
            return pageTitle;
        }

        var editorialChapters = numberedChapters.Length > 0
            ? numberedChapters
            : pages
                .Where(static candidate => candidate.Kind == HtmlBookPageKind.Chapter)
                .Where(candidate => !IsCoverPage(document, candidate))
                .Where(candidate => ChapterPageTitle(document, candidate, ui) != ui.FrontMatter)
                .ToArray();
        var chapterIndex = Array.FindIndex(editorialChapters, candidate => ReferenceEquals(candidate, page));
        return chapterIndex < 0
            ? pageTitle
            : ui.ChapterProgress(chapterIndex + 1, editorialChapters.Length);
    }

    private static bool IsCoverPage(FlowDocument document, HtmlBookPage page) =>
        document.Presentation?.Cover is { } cover
        && page.NodeIds.Contains(cover.FigureId);

    private static bool IsNumberedChapterTitle(string title)
    {
        var trimmed = title.AsSpan().TrimStart();
        var digitCount = 0;
        while (digitCount < trimmed.Length && char.IsDigit(trimmed[digitCount]))
        {
            digitCount++;
        }

        return digitCount > 0
               && digitCount < trimmed.Length
               && (char.IsWhiteSpace(trimmed[digitCount]) || char.IsPunctuation(trimmed[digitCount]));
    }

    private static string ThemeName(ReadingTheme theme) => theme switch
    {
        ReadingTheme.System => "system",
        ReadingTheme.Light => "light",
        ReadingTheme.Dark => "dark",
        ReadingTheme.Sepia => "sepia",
        ReadingTheme.HighContrast => "high-contrast",
        _ => throw new ArgumentOutOfRangeException(nameof(theme)),
    };

    private static AssetPlan CreateAssetPlan(FlowDocument document, CancellationToken cancellationToken)
    {
        var paths = new Dictionary<AssetId, string>();
        var files = new List<AssetFile>();
        foreach (var group in document.Assets.Values
                     .OrderBy(static asset => asset.Id.Value, StringComparer.Ordinal)
                     .GroupBy(static asset => Sha256(asset.Data.AsSpan()), StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var mediaType = group.Select(static asset => asset.MediaType).Order(StringComparer.Ordinal).First();
            var path = $"assets/{group.Key.ToLowerInvariant()}{Extension(mediaType)}";
            var first = group.First();
            files.Add(new AssetFile(path, mediaType, first.Data.ToArray()));
            foreach (var asset in group)
            {
                cancellationToken.ThrowIfCancellationRequested();
                paths.Add(asset.Id, path);
            }
        }

        return new AssetPlan(paths, files);
    }

    private static byte[] CreateManifest(
        FlowDocument document,
        HtmlBookIntegrity integrity,
        IReadOnlyList<HtmlBookPage> readingOrder,
        IEnumerable<HtmlBookFile> payloadFiles,
        BookUiText ui,
        CancellationToken cancellationToken)
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
            writer.WriteString("uiLanguage", ui.LanguageTag);
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
                cancellationToken.ThrowIfCancellationRequested();
                writer.WriteStartObject();
                writer.WriteString("path", page.Path);
                writer.WriteString("kind", PageKind(page.Kind));
                writer.WriteString("publicationRole", PublicationRole(document, page, readingOrder, ui));
                WriteNullableString(writer, "chapterId", page.ChapterId?.Value);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WritePropertyName("files");
            writer.WriteStartArray();
            foreach (var file in payloadFiles.OrderBy(static file => file.Path, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
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
        HtmlBookPageKind.Notes => "notes",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string PublicationRole(
        FlowDocument document,
        HtmlBookPage page,
        IReadOnlyList<HtmlBookPage> pages,
        BookUiText ui)
    {
        if (page.Kind == HtmlBookPageKind.Index)
        {
            return "titlePage";
        }

        if (page.Kind == HtmlBookPageKind.TableOfContents)
        {
            return "contents";
        }

        if (page.Kind == HtmlBookPageKind.Notes)
        {
            return "notes";
        }

        if (IsCoverPage(document, page))
        {
            return "cover";
        }

        var title = ChapterPageTitle(document, page, ui);
        if (title == ui.FrontMatter)
        {
            return "frontMatter";
        }

        var hasNumberedChapters = pages
            .Where(static candidate => candidate.Kind == HtmlBookPageKind.Chapter)
            .Select(candidate => ChapterPageTitle(document, candidate, ui))
            .Any(IsNumberedChapterTitle);
        return !hasNumberedChapters || IsNumberedChapterTitle(title)
            ? "chapter"
            : "section";
    }

    private sealed record BookUiText
    {
        private static readonly BookUiText English = new()
        {
            LanguageTag = "en",
            ChapterWord = "Chapter",
            OfWord = "of",
        };
        private static readonly BookUiText PortuguesePortugal = new()
        {
            LanguageTag = "pt-PT",
            ChapterWord = "Capítulo",
            OfWord = "de",
            TableOfContents = "Índice",
            OpenContents = "Abrir índice",
            Contents = "Índice",
            Credits = "Créditos",
            Author = "Autor",
            Authors = "Autores",
            BookOpening = "Início do livro",
            Cover = "Capa",
            FrontMatter = "Elementos pré-textuais",
            Notes = "Notas",
            LogicalReadingPosition = "Posição lógica de leitura",
            PrimaryBookNavigation = "Navegação principal do livro",
            SecondaryBookNavigation = "Navegação secundária do livro",
            PreviousLogicalReadingPosition = "Posição lógica anterior",
            NextLogicalReadingPosition = "Próxima posição lógica",
            Previous = "Anterior",
            Next = "Seguinte",
            SkipToBookContent = "Ir para o conteúdo do livro",
            ReadingPreferences = "Preferências de leitura",
            ReadingAppearance = "Aspeto da leitura",
            Theme = "Tema",
            Font = "Tipo de letra",
            TextSize = "Tamanho do texto",
            Book = "Original",
            Light = "Claro",
            Dark = "Escuro",
            Sepia = "Sépia",
            BookFont = "Do livro",
            Serif = "Com serifas",
            SansSerif = "Sem serifas",
            BookSize = "Original",
            Large = "Grande",
            Larger = "Maior",
        };
        private static readonly BookUiText PortugueseBrazil = PortuguesePortugal with
        {
            LanguageTag = "pt-BR",
            TableOfContents = "Sumário",
            OpenContents = "Abrir sumário",
            Contents = "Sumário",
            FrontMatter = "Parte pré-textual",
            Next = "Próximo",
            ReadingAppearance = "Aparência da leitura",
            Font = "Fonte",
            BookFont = "Do livro",
            Serif = "Serifada",
            SansSerif = "Sem serifa",
        };

        internal string LanguageTag { get; init; } = "en";

        internal string ChapterWord { get; init; } = "Chapter";

        internal string OfWord { get; init; } = "of";

        internal string TableOfContents { get; init; } = "Table of contents";

        internal string OpenContents { get; init; } = "Open contents";

        internal string Contents { get; init; } = "Contents";

        internal string Credits { get; init; } = "Credits";

        internal string Author { get; init; } = "Author";

        internal string Authors { get; init; } = "Authors";

        internal string BookOpening { get; init; } = "Book opening";

        internal string Cover { get; init; } = "Cover";

        internal string FrontMatter { get; init; } = "Front matter";

        internal string Notes { get; init; } = "Notes";

        internal string LogicalReadingPosition { get; init; } = "Logical reading position";

        internal string PrimaryBookNavigation { get; init; } = "Primary book navigation";

        internal string SecondaryBookNavigation { get; init; } = "Secondary book navigation";

        internal string PreviousLogicalReadingPosition { get; init; } = "Previous logical reading position";

        internal string NextLogicalReadingPosition { get; init; } = "Next logical reading position";

        internal string Previous { get; init; } = "Previous";

        internal string Next { get; init; } = "Next";

        internal string SkipToBookContent { get; init; } = "Skip to book content";

        internal string ReadingPreferences { get; init; } = "Reading preferences";

        internal string ReadingAppearance { get; init; } = "Reading appearance";

        internal string Theme { get; init; } = "Theme";

        internal string Font { get; init; } = "Font";

        internal string TextSize { get; init; } = "Text size";

        internal string Book { get; init; } = "Book";

        internal string Light { get; init; } = "Light";

        internal string Dark { get; init; } = "Dark";

        internal string Sepia { get; init; } = "Sepia";

        internal string BookFont { get; init; } = "Book font";

        internal string Serif { get; init; } = "Serif";

        internal string SansSerif { get; init; } = "Sans serif";

        internal string BookSize { get; init; } = "Book size";

        internal string Large { get; init; } = "Large";

        internal string Larger { get; init; } = "Larger";

        internal static BookUiText For(HtmlBookUiLanguage requested, string? publicationLanguage) => requested switch
        {
            HtmlBookUiLanguage.English => English,
            HtmlBookUiLanguage.PortuguesePortugal => PortuguesePortugal,
            HtmlBookUiLanguage.PortugueseBrazil => PortugueseBrazil,
            HtmlBookUiLanguage.Automatic => Automatic(publicationLanguage),
            _ => throw new ArgumentOutOfRangeException(nameof(requested), requested, "The HTML book UI language is not defined."),
        };

        internal string ChapterProgress(int current, int total) =>
            $"{ChapterWord} {current.ToString(CultureInfo.InvariantCulture)} {OfWord} {total.ToString(CultureInfo.InvariantCulture)}";

        private static BookUiText Automatic(string? publicationLanguage)
        {
            if (publicationLanguage is null)
            {
                return English;
            }

            var normalized = publicationLanguage.Replace('_', '-');
            if (normalized.Equals("pt-BR", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("pt-BR-", StringComparison.OrdinalIgnoreCase))
            {
                return PortugueseBrazil;
            }

            return normalized.Equals("pt", StringComparison.OrdinalIgnoreCase)
                   || normalized.StartsWith("pt-", StringComparison.OrdinalIgnoreCase)
                ? PortuguesePortugal
                : English;
        }
    }

    private sealed record AssetFile(string Path, string MediaType, byte[] Content);

    private sealed record AssetPlan(
        IReadOnlyDictionary<AssetId, string> PathsByAssetId,
        IReadOnlyList<AssetFile> Files);

    private sealed record PagePlan(IReadOnlyList<HtmlBookPage> Pages);

    private sealed record FootnotePlan(
        IReadOnlyDictionary<string, List<string>> FootnoteIdsByPage,
        IReadOnlyDictionary<string, string> OwnershipByNodeId,
        string BackMatterPath);

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
        Notes,
    }
}
