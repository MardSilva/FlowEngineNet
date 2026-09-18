using System.Xml.Linq;
using Flow.Core;
using Flow.Documents;
using Flow.Layout;
using Flow.Rendering.Html;

namespace Flow.Rendering.Tests;

public sealed class HtmlDocumentRendererTests
{
    private readonly HtmlDocumentRenderer _renderer = new();

    [Fact]
    public void Render_ProducesStandaloneSemanticHtml5()
    {
        var (document, preferences, layout) = CreateScenario();

        var rendered = _renderer.Render(document, layout, preferences);
        var html = rendered.ReadAsUtf8();
        var parsed = XDocument.Parse(html, LoadOptions.PreserveWhitespace);

        Assert.StartsWith("<!DOCTYPE html>\n", html, StringComparison.Ordinal);
        Assert.Equal("text/html; charset=utf-8", rendered.MediaType);
        Assert.Equal(".html", rendered.FileExtension);
        Assert.NotNull(parsed.Root?.Element("head")?.Element("style"));
        Assert.NotNull(parsed.Root?.Element("body")?.Element("article"));

        foreach (var element in new[] { "article", "section", "h1", "h2", "p", "figure", "figcaption", "blockquote", "nav", "ol", "ul", "li", "code", "pre" })
        {
            Assert.NotEmpty(parsed.Descendants(element));
        }
    }

    [Fact]
    public void Render_PreservesStableIdsAndProducesWorkingTocAndFootnoteLinks()
    {
        var (document, preferences, layout) = CreateScenario();

        var parsed = Parse(_renderer.RenderToString(document, layout, preferences));
        var tocLink = parsed.Descendants("nav").Single().Descendants("a").Single();
        var footnoteLink = parsed.Descendants("a")
            .Single(element => (string?)element.Attribute("role") == "doc-noteref");

        Assert.Equal("#heading-one", (string?)tocLink.Attribute("href"));
        Assert.Single(parsed.Descendants(), element => (string?)element.Attribute("id") == "heading-one");
        Assert.Equal("#footnote-one", (string?)footnoteLink.Attribute("href"));
        Assert.Single(parsed.Descendants(), element => (string?)element.Attribute("id") == "footnote-one");
    }

    [Fact]
    public void Render_EscapesTextAttributesAndCssAndRejectsUnsafeLinks()
    {
        const string hostileText = "<script>alert('content')</script> & text";
        const string hostileFont = "\"</style><script>alert(1)</script>";
        var presentation = new DocumentPresentation(
            new TypographySet(
            [
                KeyValuePair.Create(
                    TypographyRole.Body,
                    new TypographyStyle(fontFamily: hostileFont)),
            ]));
        var document = CreateSimpleDocument(
            [
                new Paragraph(
                    new NodeId("hostile-content"),
                    [
                        new Text(hostileText),
                        new Link("javascript:alert(1)", [new Text("unsafe")]),
                        new Link("mailto:reader%40example.invalid?subject=Flow%20Engine", [new Text("safe mail")]),
                        new Link("mailto:reader@example.invalid?subject=unsafe%0Aheader", [new Text("unsafe mail")]),
                    ]),
            ],
            presentation: presentation,
            title: "<title>unsafe</title>");
        var preferences = new UserReadingPreferences();
        var layout = Layout(document, preferences);

        var html = _renderer.RenderToString(document, layout, preferences);
        var parsed = Parse(html);

        Assert.DoesNotContain("<script>", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(hostileFont, html, StringComparison.Ordinal);
        Assert.Equal("<title>unsafe</title>", parsed.Root?.Element("head")?.Element("title")?.Value);
        Assert.Contains(hostileText, parsed.Descendants("p").Single().Value, StringComparison.Ordinal);
        Assert.DoesNotContain(
            parsed.Descendants("a"),
            element => ((string?)element.Attribute("href"))?.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Contains(parsed.Descendants("span"), element => element.Value == "unsafe");
        Assert.Contains(parsed.Descendants("a"), element =>
            (string?)element.Attribute("href") == "mailto:reader%40example.invalid?subject=Flow%20Engine");
        Assert.Contains(parsed.Descendants("span"), element => element.Value == "unsafe mail");
    }

    [Fact]
    public void Render_UsesResolvedAuthorTypographyAndUserPreferences()
    {
        var presentation = new DocumentPresentation(
            new TypographySet(
            [
                KeyValuePair.Create(
                    TypographyRole.Body,
                    new TypographyStyle(fontFamily: "Author Serif", fontSize: Length.Px(20))),
                KeyValuePair.Create(
                    TypographyRole.Heading1,
                    new TypographyStyle(fontFamily: "Author Heading", fontSize: Length.Px(30))),
            ]));
        var document = CreateSimpleDocument(
            [
                new Heading(new NodeId("custom-heading"), 1, [new Text("Heading")]),
                new Paragraph(new NodeId("custom-body"), [new Text("Body")]),
            ],
            presentation: presentation);
        var preferences = new UserReadingPreferences(
            preferredBodyFont: "Reader Serif",
            fontScale: 1.5,
            preferredHeadingFont: "Reader Sans",
            headingScale: 1.2,
            theme: ReadingTheme.Sepia);
        var layout = Layout(document, preferences);

        var html = _renderer.RenderToString(document, layout, preferences);

        Assert.Contains("font-family: \"Reader Serif\";", html, StringComparison.Ordinal);
        Assert.Contains("font-family: \"Reader Sans\";", html, StringComparison.Ordinal);
        Assert.Contains("font-size: 30px;", html, StringComparison.Ordinal);
        Assert.Contains("font-size: 54px;", html, StringComparison.Ordinal);
        Assert.Contains("background: #f4ecd8;", html, StringComparison.Ordinal);
        Assert.Equal("Author Serif", presentation.Typography[TypographyRole.Body]?.FontFamily);
    }

    [Fact]
    public void Render_EmbedsResponsiveFiguresAndMobileRules()
    {
        var (document, preferences, layout) = CreateScenario();

        var html = _renderer.RenderToString(document, layout, preferences);
        var parsed = Parse(html);
        var image = parsed.Descendants("img").Single();

        Assert.StartsWith("data:image/png;base64,", (string?)image.Attribute("src"), StringComparison.Ordinal);
        Assert.Equal("A safe & useful image", (string?)image.Attribute("alt"));
        Assert.Contains("figure img { display: block; height: auto; max-width: 100%; width: 100%; }", html, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 599px)", html, StringComparison.Ordinal);
        Assert.Contains("column-count: 1;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_SameInputsAlwaysProduceIdenticalBytes()
    {
        var (document, preferences, layout) = CreateScenario();

        var first = _renderer.Render(document, layout, preferences);
        var second = _renderer.Render(document, layout, preferences);

        Assert.True(first.Content.AsSpan().SequenceEqual(second.Content.AsSpan()));
        Assert.NotEmpty(first.Content);
        Assert.NotEqual(0xEF, first.Content[0]);
    }

    [Fact]
    public void Render_DoesNotExposeInternalClrTypeNamesOrClasses()
    {
        var (document, preferences, layout) = CreateScenario();

        var parsed = Parse(_renderer.RenderToString(document, layout, preferences));
        var html = parsed.ToString(SaveOptions.DisableFormatting);

        Assert.Empty(parsed.Descendants().Attributes("class"));
        Assert.DoesNotContain("Flow.Documents", html, StringComparison.Ordinal);
        Assert.DoesNotContain("HeadingLayoutIntent", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Paragraph", html, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderToFile_WritesExactlyTheRenderedBytes()
    {
        var (document, preferences, layout) = CreateScenario();
        var expected = _renderer.Render(document, layout, preferences);
        var directory = Path.Combine(Path.GetTempPath(), $"flow-renderer-{Guid.NewGuid():N}");
        var outputPath = Path.Combine(directory, "document.html");

        try
        {
            Directory.CreateDirectory(directory);
            _renderer.RenderToFile(document, layout, preferences, outputPath);

            Assert.True(expected.Content.AsSpan().SequenceEqual(File.ReadAllBytes(outputPath)));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Render_RejectsLayoutFromAnotherDocument()
    {
        var first = CreateSimpleDocument(
            [new Paragraph(new NodeId("first"), [new Text("First")])],
            documentId: "urn:flow:render:first");
        var second = CreateSimpleDocument(
            [new Paragraph(new NodeId("second"), [new Text("Second")])],
            documentId: "urn:flow:render:second");
        var preferences = new UserReadingPreferences();
        var secondLayout = Layout(second, preferences);

        Assert.Throws<ArgumentException>(() => _renderer.Render(first, secondLayout, preferences));
    }

    private static XDocument Parse(string html) => XDocument.Parse(html, LoadOptions.PreserveWhitespace);

    private static (FlowDocument Document, UserReadingPreferences Preferences, LayoutDocument Layout) CreateScenario()
    {
        var headingId = new NodeId("heading-one");
        var footnoteId = new NodeId("footnote-one");
        var tableOfContents = new TableOfContents(
            new NodeId("contents"),
            [new Text("Contents")],
            [
                new TableOfContentsEntry(
                    [new Text("Chapter")],
                    DocumentAnchor.Parse("flow:chapter-one/heading-one"),
                    level: 1),
            ]);
        var caption = new Caption(new NodeId("caption-one"), [new Text("Responsive figure")]);
        var chapter = new Chapter(
            new NodeId("chapter-one"),
            [
                new Heading(headingId, 1, [new Text("Chapter")]),
                new Paragraph(
                    new NodeId("paragraph-one"),
                    [
                        new Text("Text "),
                        new Strong([new Text("strong")]),
                        new Text(" "),
                        new Emphasis([new Text("emphasis")]),
                        new Text(" "),
                        new Underline([new Text("underline")]),
                        new Text(" "),
                        new Strikethrough([new Text("struck")]),
                        new Text(" "),
                        new InlineCode("code"),
                        new LineBreak(),
                        new Link("https://example.com/?a=1&b=2", [new Text("external")]),
                        new Text(" "),
                        new FootnoteReference(footnoteId),
                    ]),
                new Section(
                    new NodeId("section-one"),
                    [
                        new Heading(new NodeId("heading-two"), 2, [new Text("Section")]),
                        new BlockQuote(
                            new NodeId("quote-one"),
                            [new Paragraph(new NodeId("quote-paragraph"), [new Text("Quoted")])]),
                        new OrderedList(
                            new NodeId("ordered-one"),
                            [
                                new ListItem(
                                    new NodeId("ordered-item"),
                                    [new Paragraph(new NodeId("ordered-paragraph"), [new Text("First")])]),
                            ]),
                        new UnorderedList(
                            new NodeId("unordered-one"),
                            [
                                new ListItem(
                                    new NodeId("unordered-item"),
                                    [new Paragraph(new NodeId("unordered-paragraph"), [new Text("Item")])]),
                            ]),
                        new Figure(
                            new NodeId("figure-one"),
                            new AssetId("figure.png"),
                            caption,
                            "A safe & useful image"),
                        new CodeBlock(new NodeId("code-block-one"), "if (a < b) return;", "csharp"),
                        new HorizontalRule(new NodeId("rule-one")),
                    ]),
                new Footnote(
                    footnoteId,
                    [new Paragraph(new NodeId("footnote-paragraph"), [new Text("Footnote")])]),
            ]);
        var document = new FlowDocument(
            new DocumentIdentity(new DocumentId("urn:flow:render:scenario"), "1"),
            new DocumentMetadata("Renderer scenario", "en"),
            new DocumentContent([tableOfContents, chapter]),
            [new FlowAsset(new AssetId("figure.png"), "image/png", "figure.png", new byte[] { 1, 2, 3 })]);
        var preferences = new UserReadingPreferences();

        return (document, preferences, Layout(document, preferences));
    }

    private static FlowDocument CreateSimpleDocument(
        IEnumerable<DocumentNode> children,
        DocumentPresentation? presentation = null,
        string title = "Renderer test",
        string documentId = "urn:flow:render:test") =>
        new(
            new DocumentIdentity(new DocumentId(documentId)),
            new DocumentMetadata(title),
            new DocumentContent(children),
            presentation: presentation);

    private static LayoutDocument Layout(FlowDocument document, UserReadingPreferences preferences) =>
        new AdaptiveLayoutEngine().Layout(
            document,
            new LayoutContext(1024, 768, userPreferences: preferences));
}
