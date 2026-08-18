using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Flow.Core;
using Flow.Documents;
using Flow.Layout;
using Flow.Rendering.Html;
using Flow.Security;

namespace Flow.Rendering.Tests;

public sealed class HtmlBookPackageRendererTests
{
    private static readonly HtmlBookIntegrity Integrity = new("SHA-256", new string('A', 64), "flow-c14n-0.1");
    private readonly HtmlBookPackageRenderer renderer = new();

    [Fact]
    public void Render_SeparatesTocAndChaptersAndResolvesEveryInternalLink()
    {
        var document = CreateTwoChapterDocument();

        var package = Render(document, 1024, 768);

        Assert.Equal(
            ["chapters/chapter-001.html", "chapters/chapter-002.html", "index.html", "manifest.json", "styles/book.css", "toc.html"],
            package.Files.Select(static file => file.Path).ToArray());
        var toc = Parse(package, "toc.html");
        Assert.Equal(
            "chapters/chapter-001.html#chapter-one-heading",
            (string?)toc.Descendants("a").Single(element => element.Value == "One").Attribute("href"));
        Assert.Equal(
            "chapters/chapter-002.html#chapter-two-heading",
            (string?)toc.Descendants("a").Single(element => element.Value == "Two").Attribute("href"));

        var first = Parse(package, "chapters/chapter-001.html");
        var second = Parse(package, "chapters/chapter-002.html");
        Assert.Equal("#footnote-two", (string?)first.Descendants("a").Single(element => (string?)element.Attribute("role") == "doc-noteref").Attribute("href"));
        Assert.Equal("#paragraph-one", (string?)first.Descendants("a").Single(element => element.Value == "back").Attribute("href"));
        Assert.Single(first.Descendants("section"), element => (string?)element.Attribute("role") == "doc-footnote");
        Assert.DoesNotContain(second.Descendants("section"), element => (string?)element.Attribute("role") == "doc-footnote");
        Assert.DoesNotContain(first.Descendants("nav"), element => (string?)element.Attribute("aria-label") == "Table of contents");
        AssertAllInternalLinksResolve(package);
        AssertAllLocalResourcesResolveWithoutScripts(package);
    }

    [Fact]
    public void Render_ZeroAndOneChapterFollowTheDocumentedPlacementPolicy()
    {
        var zero = CreateDocument(
            [new Paragraph(new NodeId("only-content"), [new Text("Without chapter")])],
            "urn:flow:html-book:zero");
        var one = CreateDocument(
            [
                new Paragraph(new NodeId("front"), [new Text("Front")]),
                new Chapter(
                    new NodeId("only-chapter"),
                    [new Heading(new NodeId("only-heading"), 1, [new Text("Only")])]),
                new Paragraph(new NodeId("after"), [new Text("After")]),
            ],
            "urn:flow:html-book:one");

        var zeroPackage = Render(zero, 390, 844);
        var onePackage = Render(one, 1600, 1000);

        Assert.DoesNotContain(zeroPackage.Files, static file => file.Path.StartsWith("chapters/", StringComparison.Ordinal));
        Assert.Single(Parse(zeroPackage, "index.html").Descendants("p"), element => (string?)element.Attribute("id") == "only-content");
        Assert.Single(Parse(onePackage, "index.html").Descendants("p"), element => (string?)element.Attribute("id") == "front");
        var chapter = Parse(onePackage, "chapters/chapter-001.html");
        Assert.Single(chapter.Descendants("section"), element => (string?)element.Attribute("id") == "only-chapter");
        Assert.Single(chapter.Descendants("p"), element => (string?)element.Attribute("id") == "after");
    }

    [Fact]
    public void Render_ExternalizesAndDeduplicatesAssetsAndPreservesComplexSemantics()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var firstAsset = new AssetId("first.png");
        var secondAsset = new AssetId("second.png");
        var math = new MathExpression(
            new NodeId("math"),
            new MathElement("math", [new MathElement("mi", [new MathText("x")])]));
        var table = new Table(
            new NodeId("table"),
            [
                new TableBody(
                    new NodeId("body"),
                    [
                        new TableRow(
                            new NodeId("row"),
                            [new TableCell(new NodeId("cell"), [new Paragraph(new NodeId("cell-p"), [new Text("Cell")])])]),
                    ]),
            ]);
        var paragraph = new Paragraph(
            new NodeId("international"),
            [
                new BidirectionalSpan(TextDirection.RightToLeft, BidirectionalMode.Isolation, [new Text("مرحبا")]),
                new Ruby([new Text("日"), new RubyAnnotation([new Text("にち")])]),
            ]);
        var chapter = new Chapter(
            new NodeId("complex-chapter"),
            [
                new Heading(new NodeId("complex-heading"), 1, [new Text("Complex")]),
                new Figure(new NodeId("figure-one"), firstAsset, alternativeText: "One"),
                new Figure(new NodeId("figure-two"), secondAsset, alternativeText: "Two"),
                table,
                math,
                paragraph,
            ]);
        var document = new FlowDocument(
            new DocumentIdentity(new DocumentId("urn:flow:html-book:complex")),
            new DocumentMetadata("Complex"),
            new DocumentContent([chapter]),
            [
                new FlowAsset(firstAsset, "image/png", "../unsafe-one.png", bytes),
                new FlowAsset(secondAsset, "image/png", "unsafe-two.png", bytes),
            ],
            new DocumentPresentation(cover: new CoverPresentation(new NodeId("figure-one"))));

        var package = Render(document, 1024, 768);

        var asset = Assert.Single(package.Files, static file => file.Path.StartsWith("assets/", StringComparison.Ordinal));
        Assert.Matches("^assets/[0-9a-f]{64}\\.png$", asset.Path);
        var index = Parse(package, "index.html");
        Assert.Single(index.Descendants("figure"), element => (string?)element.Attribute("data-publication-role") == "cover-preview");
        var chapterHtml = Parse(package, "chapters/chapter-001.html");
        Assert.Equal(2, chapterHtml.Descendants("img").Count());
        Assert.All(chapterHtml.Descendants("img"), image => Assert.StartsWith("../assets/", (string?)image.Attribute("src"), StringComparison.Ordinal));
        Assert.NotEmpty(chapterHtml.Descendants("table"));
        Assert.Contains(chapterHtml.Descendants(), static element => element.Name.LocalName == "math");
        Assert.NotEmpty(chapterHtml.Descendants("bdi"));
        Assert.NotEmpty(chapterHtml.Descendants("ruby"));
        Assert.DoesNotContain(package.Files, static file => file.Path.Contains("..", StringComparison.Ordinal));
    }

    [Fact]
    public void Render_IsByteDeterministicAndManifestHashesEveryPayloadFile()
    {
        var document = CreateTwoChapterDocument();

        var first = Render(document, 1024, 768);
        var second = Render(document, 1024, 768);

        Assert.Equal(first.Files.Select(static file => file.Path), second.Files.Select(static file => file.Path));
        foreach (var firstFile in first.Files)
        {
            Assert.True(firstFile.Content.AsSpan().SequenceEqual(second.GetFile(firstFile.Path).Content.AsSpan()));
        }

        using var manifest = JsonDocument.Parse(first.GetFile("manifest.json").Content.ToArray());
        Assert.Equal(HtmlBookPackage.Format, manifest.RootElement.GetProperty("format").GetString());
        Assert.Equal(Integrity.Hash, manifest.RootElement.GetProperty("canonicalIntegrity").GetProperty("hash").GetString());
        Assert.True(manifest.RootElement.GetProperty("manifestSelfHashExcluded").GetBoolean());
        foreach (var listed in manifest.RootElement.GetProperty("files").EnumerateArray())
        {
            var file = first.GetFile(listed.GetProperty("path").GetString()!);
            Assert.Equal(file.Content.Length, listed.GetProperty("bytes").GetInt32());
            Assert.Equal(
                Convert.ToHexString(SHA256.HashData(file.Content.AsSpan())).ToLowerInvariant(),
                listed.GetProperty("sha256").GetString());
        }
    }

    [Fact]
    public void Render_MobileAndDesktopKeepIdentityCanonicalHashAndSemanticIds()
    {
        var document = CreateTwoChapterDocument();

        var mobile = Render(document, 390, 844);
        var desktop = Render(document, 1600, 1000);

        Assert.Equal(AllSemanticIds(mobile), AllSemanticIds(desktop));
        foreach (var package in new[] { mobile, desktop })
        {
            foreach (var html in package.Files.Where(static file => file.Path.EndsWith(".html", StringComparison.Ordinal)))
            {
                var parsed = XDocument.Parse(Encoding.UTF8.GetString(html.Content.AsSpan()));
                var article = parsed.Descendants("article").Single();
                Assert.Equal(document.Identity.Id.Value, (string?)article.Attribute("data-document-id"));
                Assert.Equal(Integrity.Hash, (string?)article.Attribute("data-canonical-hash"));
            }

            AssertAllInternalLinksResolve(package);
            AssertAllLocalResourcesResolveWithoutScripts(package);
        }
    }

    [Fact]
    public void Render_ProvidesBookLandmarksKeyboardNavigationCreditsAndLogicalProgress()
    {
        var package = Render(CreateTwoChapterDocument(), 1024, 768);

        foreach (var htmlFile in package.Files.Where(static file => file.Path.EndsWith(".html", StringComparison.Ordinal)))
        {
            var html = Parse(package, htmlFile.Path);
            var body = html.Root!.Element("body")!;
            var skipLink = Assert.Single(body.Elements("a"), element => (string?)element.Attribute("class") == "skip-link");
            var main = Assert.Single(body.Descendants("main"));
            Assert.Equal($"#{(string?)main.Attribute("id")}", (string?)skipLink.Attribute("href"));
            Assert.Equal("-1", (string?)main.Attribute("tabindex"));
            Assert.Single(body.Descendants("header"), element => (string?)element.Attribute("class") == "book-masthead");
            Assert.Single(body.Descendants("aside"), element => (string?)element.Attribute("aria-label") == "Reading preferences");
            Assert.Single(body.Descendants("footer"), element => (string?)element.Attribute("class") == "book-footer");
            Assert.Single(body.Descendants("nav"), element => (string?)element.Attribute("aria-label") == "Primary book navigation");
            Assert.Single(body.Descendants("nav"), element => (string?)element.Attribute("aria-label") == "Secondary book navigation");
            Assert.DoesNotContain(body.Descendants(), static element => element.Name.LocalName == "script");

            foreach (var input in body.Descendants("input"))
            {
                var id = Assert.IsType<string>((string?)input.Attribute("id"));
                Assert.Contains(body.Descendants("label"), label => (string?)label.Attribute("for") == id);
            }

            var progress = Assert.Single(body.Descendants("p"), element => (string?)element.Attribute("aria-label") == "Logical reading position");
            Assert.DoesNotContain("page", progress.Value, StringComparison.OrdinalIgnoreCase);
        }

        var index = Parse(package, "index.html");
        var credits = Assert.Single(index.Descendants("section"), element => (string?)element.Attribute("class") == "book-credits");
        Assert.Equal("Flow", Assert.Single(credits.Descendants("dd")).Value);
        Assert.Equal(
            "Open contents",
            Assert.Single(
                index.Descendants("p").Where(element => (string?)element.Attribute("class") == "start-reading")
                    .Descendants("a")).Value);
        Assert.Equal("Chapter 1 of 2", Parse(package, "chapters/chapter-001.html").Descendants("p")
            .Single(element => (string?)element.Attribute("aria-label") == "Logical reading position").Value);
    }

    [Fact]
    public void Render_DoesNotInventCreditsWhenNoAuthorMetadataExists()
    {
        var document = CreateDocument(
            [new Paragraph(new NodeId("content"), [new Text("Content")])],
            "urn:flow:html-book:no-credits",
            authors: []);

        var index = Parse(Render(document, 1024, 768), "index.html");

        Assert.DoesNotContain(index.Descendants("section"), element => (string?)element.Attribute("class") == "book-credits");
    }

    [Fact]
    public void Render_StylesProvideResponsiveThemesFocusPreferencesReducedMotionAndPrintFallbacks()
    {
        var preferences = new UserReadingPreferences(
            preferredBodyFont: "Reader Serif",
            fontScale: 1.2,
            preferredHeadingFont: "Reader Sans",
            theme: ReadingTheme.Dark);

        var package = Render(CreateTwoChapterDocument(), 390, 844, preferences);
        var css = Encoding.UTF8.GetString(package.GetFile("styles/book.css").Content.AsSpan());
        var chapter = Parse(package, "chapters/chapter-001.html");

        Assert.Equal("dark", (string?)chapter.Root!.Attribute("data-default-theme"));
        Assert.Contains("Reader Serif", css, StringComparison.Ordinal);
        Assert.Contains("Reader Sans", css, StringComparison.Ordinal);
        Assert.Contains("@media (prefers-color-scheme: dark)", css, StringComparison.Ordinal);
        Assert.Contains("@media (prefers-reduced-motion: reduce)", css, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 42rem)", css, StringComparison.Ordinal);
        Assert.Contains("@media print", css, StringComparison.Ordinal);
        Assert.Contains(":focus-visible", css, StringComparison.Ordinal);
        Assert.Contains("#ffffff", css, StringComparison.Ordinal);
        Assert.Contains("#171717", css, StringComparison.Ordinal);
        Assert.Contains("#f4ecd8", css, StringComparison.Ordinal);
        Assert.Contains("max-width: 46rem", css, StringComparison.Ordinal);
        Assert.Contains("--book-reader-scale: 1.3", css, StringComparison.Ordinal);
        Assert.DoesNotContain("javascript", css, StringComparison.OrdinalIgnoreCase);

        var controls = chapter.Descendants("input").ToArray();
        Assert.Contains(controls, input => (string?)input.Attribute("name") == "flow-reader-theme" && (string?)input.Attribute("value") == "sepia");
        Assert.Contains(controls, input => (string?)input.Attribute("name") == "flow-reader-font" && (string?)input.Attribute("value") == "sans");
        Assert.Contains(controls, input => (string?)input.Attribute("name") == "flow-reader-scale" && (string?)input.Attribute("value") == "larger");
    }

    [Fact]
    public void Render_PresentationAndReadingPreferencesDoNotChangeCanonicalHash()
    {
        var source = CreateTwoChapterDocument();
        var styled = new FlowDocument(
            source.Identity,
            source.Metadata,
            source.Content,
            source.Assets.Values,
            new DocumentPresentation(theme: ReadingTheme.Sepia));
        var integrityService = new Sha256DocumentIntegrityService(new FlowDocumentCanonicalizer());
        var before = integrityService.ComputeHash(source);

        _ = Render(
            styled,
            1600,
            1000,
            new UserReadingPreferences(preferredBodyFont: "Reader Serif", fontScale: 1.3, theme: ReadingTheme.Dark));

        var after = integrityService.ComputeHash(styled);
        Assert.Equal(before.Hash, after.Hash);
        Assert.Equal(before.CanonicalizationVersion, after.CanonicalizationVersion);
    }

    [Fact]
    public void Render_SharedFootnoteUsesOneBackMatterDefinition()
    {
        var noteId = new NodeId("shared-note");
        var first = new Chapter(
            new NodeId("shared-first"),
            [
                new Heading(new NodeId("shared-first-heading"), 1, [new Text("One")]),
                new Paragraph(new NodeId("shared-first-p"), [new FootnoteReference(noteId, [new Text("1")])]),
            ]);
        var second = new Chapter(
            new NodeId("shared-second"),
            [
                new Heading(new NodeId("shared-second-heading"), 1, [new Text("Two")]),
                new Paragraph(new NodeId("shared-second-p"), [new FootnoteReference(noteId, [new Text("1")])]),
                new Footnote(noteId, [new Paragraph(new NodeId("shared-note-p"), [new Text("Shared")])]),
            ]);
        var package = Render(CreateDocument([first, second], "urn:flow:html-book:shared-note"), 1024, 768);

        var firstHtml = Parse(package, "chapters/chapter-001.html");
        var secondHtml = Parse(package, "chapters/chapter-002.html");
        var notes = Parse(package, "backmatter/notes.html");

        Assert.Equal(
            "../backmatter/notes.html#shared-note",
            (string?)firstHtml.Descendants("a").Single(element => (string?)element.Attribute("role") == "doc-noteref").Attribute("href"));
        Assert.Equal(
            "../backmatter/notes.html#shared-note",
            (string?)secondHtml.Descendants("a").Single(element => (string?)element.Attribute("role") == "doc-noteref").Attribute("href"));
        Assert.DoesNotContain(firstHtml.Descendants(), element => (string?)element.Attribute("id") == noteId.Value);
        Assert.DoesNotContain(secondHtml.Descendants(), element => (string?)element.Attribute("id") == noteId.Value);
        Assert.Single(notes.Descendants(), element => (string?)element.Attribute("id") == noteId.Value);
        Assert.Equal("doc-endnotes", (string?)notes.Descendants("section").First().Attribute("role"));

        using var manifest = JsonDocument.Parse(package.GetFile("manifest.json").Content.ToArray());
        var notePosition = manifest.RootElement.GetProperty("readingOrder").EnumerateArray()
            .Single(item => item.GetProperty("path").GetString() == "backmatter/notes.html");
        Assert.Equal("notes", notePosition.GetProperty("kind").GetString());
        Assert.Equal("notes", notePosition.GetProperty("publicationRole").GetString());
        AssertAllInternalLinksResolve(package);
    }

    [Fact]
    public void Render_TocUsesNestedOrderedListsForItsSemanticLevels()
    {
        var firstHeading = new NodeId("nested-first");
        var firstChild = new NodeId("nested-first-child");
        var secondHeading = new NodeId("nested-second");
        var toc = new TableOfContents(
            new NodeId("nested-toc"),
            [new Text("Contents")],
            [
                new TableOfContentsEntry([new Text("First")], DocumentAnchor.Parse($"flow:nested-chapter/{firstHeading}"), 1),
                new TableOfContentsEntry([new Text("Child")], DocumentAnchor.Parse($"flow:nested-chapter/{firstChild}"), 2),
                new TableOfContentsEntry([new Text("Second")], DocumentAnchor.Parse($"flow:nested-chapter/{secondHeading}"), 1),
            ]);
        var chapter = new Chapter(
            new NodeId("nested-chapter"),
            [
                new Heading(firstHeading, 1, [new Text("First")]),
                new Heading(firstChild, 2, [new Text("Child")]),
                new Heading(secondHeading, 1, [new Text("Second")]),
            ]);

        var html = Parse(Render(CreateDocument([toc, chapter], "urn:flow:html-book:nested-toc"), 1024, 768), "toc.html");
        var rootList = html.Descendants("nav").Single(element => (string?)element.Attribute("id") == "nested-toc")
            .Elements("ol").Single();
        var topLevel = rootList.Elements("li").ToArray();

        Assert.Equal(2, topLevel.Length);
        Assert.Equal("1", (string?)topLevel[0].Attribute("data-level"));
        Assert.Equal("Child", topLevel[0].Elements("ol").Single().Elements("li").Single().Elements("a").Single().Value);
        Assert.Equal("1", (string?)topLevel[1].Attribute("data-level"));
    }

    [Fact]
    public void Render_PortugueseBookLocalizesUiAndCountsOnlyNumberedChapters()
    {
        var coverAsset = new AssetId("cover.png");
        var coverFigure = new NodeId("cover-figure");
        var document = new FlowDocument(
            new DocumentIdentity(new DocumentId("urn:flow:html-book:pt")),
            new DocumentMetadata("Livro", "pt-BR", ["Autora"]),
            new DocumentContent(
                [
                    new Chapter(new NodeId("cover-page"), [new Figure(coverFigure, coverAsset, alternativeText: "Capa")]),
                    new Chapter(new NodeId("introduction"), [new Heading(new NodeId("introduction-heading"), 1, [new Text("Introdução")])]),
                    new Chapter(new NodeId("chapter-one"), [new Heading(new NodeId("numbered-one"), 1, [new Text("1. Começo")])]),
                    new Chapter(new NodeId("chapter-two"), [new Heading(new NodeId("numbered-two"), 1, [new Text("2. Fim")])]),
                ]),
            [new FlowAsset(coverAsset, "image/png", "cover.png", new byte[] { 1, 2, 3 })],
            new DocumentPresentation(cover: new CoverPresentation(coverFigure)));
        var package = Render(document, 390, 844);

        AssertProgress(package, "chapters/chapter-001.html", "Capa", "Posição lógica de leitura");
        AssertProgress(package, "chapters/chapter-002.html", "Introdução", "Posição lógica de leitura");
        AssertProgress(package, "chapters/chapter-003.html", "Capítulo 1 de 2", "Posição lógica de leitura");
        AssertProgress(package, "chapters/chapter-004.html", "Capítulo 2 de 2", "Posição lógica de leitura");
        var chapter = Parse(package, "chapters/chapter-003.html");
        Assert.Contains(chapter.Descendants("a"), element => element.Value == "Anterior");
        Assert.Contains(chapter.Descendants("a"), element => element.Value == "Sumário");
        Assert.Contains(chapter.Descendants("a"), element => element.Value == "Próximo");
        Assert.Equal("Aparência da leitura", chapter.Descendants("summary").Single().Value);

        using var manifest = JsonDocument.Parse(package.GetFile("manifest.json").Content.ToArray());
        var roles = manifest.RootElement.GetProperty("readingOrder").EnumerateArray()
            .Where(item => item.GetProperty("kind").GetString() == "chapter")
            .Select(item => item.GetProperty("publicationRole").GetString() ?? string.Empty)
            .ToArray();
        Assert.Equal(["cover", "section", "chapter", "chapter"], roles);
    }

    [Fact]
    public void Render_PreservesLongTextAndDoesNotAllowThemeOverrideOfHighContrastSafety()
    {
        var longText = string.Concat(Enumerable.Repeat("Texto longo sem perda. ", 600));
        var document = CreateDocument(
            [new Chapter(new NodeId("long-chapter"), [new Paragraph(new NodeId("long-text"), [new Text(longText)])])],
            "urn:flow:html-book:long");
        var preferences = new UserReadingPreferences(theme: ReadingTheme.HighContrast);

        var package = Render(document, 390, 844, preferences);
        var chapter = Parse(package, "chapters/chapter-001.html");

        Assert.Equal(longText, chapter.Descendants("p").Single(element => (string?)element.Attribute("id") == "long-text").Value);
        Assert.Equal("high-contrast", (string?)chapter.Root!.Attribute("data-default-theme"));
        Assert.DoesNotContain(
            chapter.Descendants("input"),
            input => (string?)input.Attribute("name") == "flow-reader-theme"
                     && (string?)input.Attribute("value") != "default");
    }

    [Theory]
    [InlineData("../escape.html")]
    [InlineData("chapters/../../escape.html")]
    [InlineData("/absolute.html")]
    [InlineData("C:\\escape.html")]
    [InlineData("chapters\\escape.html")]
    [InlineData("chapters/unsafe:name.html")]
    public void HtmlBookFile_RejectsUnsafePackagePaths(string path)
    {
        Assert.Throws<ArgumentException>(() => new HtmlBookFile(path, "text/html", ReadOnlyMemory<byte>.Empty));
    }

    private HtmlBookPackage Render(
        FlowDocument document,
        double width,
        double height,
        UserReadingPreferences? preferences = null)
    {
        preferences ??= new UserReadingPreferences();
        var layout = new AdaptiveLayoutEngine().Layout(document, new LayoutContext(width, height, userPreferences: preferences));
        return renderer.Render(document, layout, preferences, Integrity);
    }

    private static FlowDocument CreateTwoChapterDocument()
    {
        var firstHeading = new NodeId("chapter-one-heading");
        var secondHeading = new NodeId("chapter-two-heading");
        var footnote = new NodeId("footnote-two");
        var toc = new TableOfContents(
            new NodeId("toc"),
            [new Text("Contents")],
            [
                new TableOfContentsEntry([new Text("One")], DocumentAnchor.Parse($"flow:chapter-one/{firstHeading}"), 1),
                new TableOfContentsEntry([new Text("Two")], DocumentAnchor.Parse($"flow:chapter-two/{secondHeading}"), 1),
            ]);
        var first = new Chapter(
            new NodeId("chapter-one"),
            [
                new Heading(firstHeading, 1, [new Text("One")]),
                new Paragraph(
                    new NodeId("paragraph-one"),
                    [new Text("Call"), new FootnoteReference(footnote, [new Text("1")])]),
            ]);
        var second = new Chapter(
            new NodeId("chapter-two"),
            [
                new Heading(secondHeading, 1, [new Text("Two")]),
                new Footnote(
                    footnote,
                    [
                        new Paragraph(
                            new NodeId("footnote-text"),
                            [new Text("Note "), new Link("flow:chapter-one/paragraph-one", [new Text("back")])]),
                    ]),
            ]);
        return CreateDocument([toc, first, second], "urn:flow:html-book:two");
    }

    private static FlowDocument CreateDocument(
        IEnumerable<DocumentNode> nodes,
        string id,
        IEnumerable<string>? authors = null) => new(
        new DocumentIdentity(new DocumentId(id), "1"),
        new DocumentMetadata("HTML book", "en", authors ?? ["Flow"]),
        new DocumentContent(nodes));

    private static XDocument Parse(HtmlBookPackage package, string path) =>
        XDocument.Parse(Encoding.UTF8.GetString(package.GetFile(path).Content.AsSpan()));

    private static void AssertProgress(
        HtmlBookPackage package,
        string path,
        string expectedProgress,
        string accessibleLabel)
    {
        var progress = Parse(package, path).Descendants("p")
            .Single(element => (string?)element.Attribute("aria-label") == accessibleLabel);
        Assert.Equal(expectedProgress, progress.Value);
    }

    private static string[] AllSemanticIds(HtmlBookPackage package) => package.Files
        .Where(static file => file.Path.EndsWith(".html", StringComparison.Ordinal))
        .SelectMany(file => XDocument.Parse(Encoding.UTF8.GetString(file.Content.AsSpan()))
            .Descendants()
            .Attributes("id")
            .Select(static attribute => attribute.Value))
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static void AssertAllInternalLinksResolve(HtmlBookPackage package)
    {
        var htmlFiles = package.Files
            .Where(static file => file.Path.EndsWith(".html", StringComparison.Ordinal))
            .ToDictionary(static file => file.Path, file => Parse(package, file.Path), StringComparer.Ordinal);
        foreach (var (sourcePath, source) in htmlFiles)
        {
            foreach (var href in source.Descendants("a").Attributes("href").Select(static attribute => attribute.Value))
            {
                if (Uri.TryCreate(href, UriKind.Absolute, out _))
                {
                    continue;
                }

                var parts = href.Split('#', 2);
                var targetPath = parts[0].Length == 0 ? sourcePath : ResolveRelative(sourcePath, parts[0]);
                Assert.True(htmlFiles.TryGetValue(targetPath, out var target), $"Missing target file '{targetPath}' from '{sourcePath}'.");
                if (parts.Length == 2)
                {
                    Assert.Contains(target!.Descendants(), element => (string?)element.Attribute("id") == parts[1]);
                }
            }
        }
    }

    private static string ResolveRelative(string sourcePath, string reference)
    {
        var sourceUri = new Uri("https://flow.invalid/" + sourcePath, UriKind.Absolute);
        return new Uri(sourceUri, reference).AbsolutePath.TrimStart('/');
    }

    private static void AssertAllLocalResourcesResolveWithoutScripts(HtmlBookPackage package)
    {
        var paths = package.Files.Select(static file => file.Path).ToHashSet(StringComparer.Ordinal);
        foreach (var htmlFile in package.Files.Where(static file => file.Path.EndsWith(".html", StringComparison.Ordinal)))
        {
            var html = Parse(package, htmlFile.Path);
            Assert.DoesNotContain(html.Descendants(), static element => element.Name.LocalName == "script");
            var resources = html.Descendants("link").Attributes("href")
                .Concat(html.Descendants("img").Attributes("src"));
            foreach (var resource in resources)
            {
                Assert.True(
                    paths.Contains(ResolveRelative(htmlFile.Path, resource.Value)),
                    $"Missing local resource '{resource.Value}' from '{htmlFile.Path}'.");
            }
        }
    }
}
