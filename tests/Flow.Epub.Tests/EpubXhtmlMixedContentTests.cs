using Flow.Documents;

namespace Flow.Epub.Tests;

public sealed class EpubXhtmlMixedContentTests
{
    [Fact]
    public async Task ImportAsync_PreservesNestedSemanticContainersAndInlineTextInReadingOrder()
    {
        const string chapter = """
            <html xmlns="http://www.w3.org/1999/xhtml"><head><title>Nested</title></head><body>
              <article id="article">
                <header><h1 id="title">Nested semantics</h1><address>By <abbr title="Flow Author">FA</abbr></address></header>
                <main><section id="content">
                  <p>Intro <cite>Source</cite> <q>quoted</q> H<sub>2</sub>O x<sup>2</sup> <mark>marked</mark> <time datetime="2026-08-18">today</time>.</p>
                  <aside><p>Supplement with <abbr title="Flow Engine">FE</abbr>.</p></aside>
                  <details><summary>More information</summary><p>Expanded details.</p></details>
                </section></main>
                <footer><p>Final note.</p></footer>
              </article>
            </body></html>
            """;
        await using var epub = Create(chapter);

        var result = await new EpubImporter().ImportAsync(epub);

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics));
        var document = Assert.IsType<FlowDocument>(result.Document);
        var chapterNode = Assert.IsType<Chapter>(Assert.Single(document.Content.Children));
        var article = Assert.IsType<Section>(Assert.Single(chapterNode.Children));
        Assert.Contains(article.Children, static node => node is Heading);
        Assert.Contains(article.Children, static node => node is Section);
        Assert.Equal(
            [
                "By FA",
                "Intro Source quoted H2O x2 marked today.",
                "Supplement with FE.",
                "More information",
                "Expanded details.",
                "Final note.",
            ],
            ParagraphTexts(document));

        AssertSingleAggregatedDiagnostic(result, "Element <main>");
        AssertSingleAggregatedDiagnostic(result, "Element <article>");
        AssertSingleAggregatedDiagnostic(result, "Element <header>");
        AssertSingleAggregatedDiagnostic(result, "Element <aside>");
        AssertSingleAggregatedDiagnostic(result, "Element <details>");
        AssertSingleAggregatedDiagnostic(result, "Element <summary>");
        var abbreviation = Assert.Single(result.Diagnostics, static item =>
            item.Code == EpubDiagnosticCodes.UnsupportedElement
            && item.Message.Contains("Element <abbr>", StringComparison.Ordinal));
        Assert.Contains("2 times", abbreviation.Message, StringComparison.Ordinal);

        await using var json = new MemoryStream();
        var serializer = new FlowJsonDocumentSerializer();
        await serializer.SerializeAsync(document, json);
        json.Position = 0;
        var roundTripped = await serializer.DeserializeAsync(json);
        Assert.Equal(ParagraphTexts(document), ParagraphTexts(roundTripped));
    }

    [Fact]
    public async Task ImportAsync_DefinitionListPreservesTermsDefinitionsFormattingAndOrder()
    {
        const string chapter = """
            <html xmlns="http://www.w3.org/1999/xhtml"><head><title>Definitions</title></head><body>
              <h1>Definitions</h1>
              <dl id="terms">
                <dt>Flow</dt><dd>A <em>semantic</em> document engine.</dd>
                <dt>Spine</dt><dd>The publication reading order.</dd>
              </dl>
            </body></html>
            """;
        await using var epub = Create(chapter);

        var result = await new EpubImporter().ImportAsync(epub);

        var document = Assert.IsType<FlowDocument>(result.Document);
        Assert.Equal(
            ["Flow", "A semantic document engine.", "Spine", "The publication reading order."],
            ParagraphTexts(document));
        Assert.Contains(
            document.Index.Locations.Select(static item => item.Node).OfType<Paragraph>(),
            static paragraph => paragraph.Content.OfType<Emphasis>().Any());
        var dtDiagnostic = Assert.Single(result.Diagnostics, static item =>
            item.Code == EpubDiagnosticCodes.UnsupportedElement
            && item.Message.Contains("Element <dt>", StringComparison.Ordinal));
        var ddDiagnostic = Assert.Single(result.Diagnostics, static item =>
            item.Code == EpubDiagnosticCodes.UnsupportedElement
            && item.Message.Contains("Element <dd>", StringComparison.Ordinal));
        Assert.Contains("2 times", dtDiagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("2 times", ddDiagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportAsync_UnknownMixedContainersDoNotFlattenDuplicateOrReorderDescendants()
    {
        const string chapter = """
            <html xmlns="http://www.w3.org/1999/xhtml"><head><title>Mixed</title></head><body>
              <div>before <mystery>alpha <strong>x</strong><p>middle</p> omega</mystery> after</div>
              <mystery>one<mystery>two</mystery>three</mystery><mystery>four</mystery>
            </body></html>
            """;
        await using var epub = Create(chapter);

        var result = await new EpubImporter().ImportAsync(epub);

        var document = Assert.IsType<FlowDocument>(result.Document);
        Assert.Equal(
            ["before ", "alpha x", "middle", " omega", " after", "one", "two", "three", "four"],
            ParagraphTexts(document));
        Assert.DoesNotContain(
            ParagraphTexts(document),
            static text => text.Contains("alpha xmiddle omega", StringComparison.Ordinal));
        var diagnostic = Assert.Single(result.Diagnostics, static item =>
            item.Code == EpubDiagnosticCodes.UnsupportedElement
            && item.Message.Contains("Element <mystery>", StringComparison.Ordinal));
        Assert.Contains("4 times", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportAsync_PreservesSignificantInlineAndPreformattedWhitespace()
    {
        const string chapter = """
            <html xmlns="http://www.w3.org/1999/xhtml"><head><title>Whitespace</title></head><body>
              <p>Alpha  <abbr>A B</abbr>
                gamma <sub>2</sub> <sup>3</sup> <mark> marked </mark><time> now </time></p>
              <pre> line one
                line two </pre>
            </body></html>
            """;
        await using var epub = Create(chapter);

        var result = await new EpubImporter().ImportAsync(epub);

        var document = Assert.IsType<FlowDocument>(result.Document);
        var paragraph = Assert.Single(document.Index.Locations.Select(static item => item.Node).OfType<Paragraph>());
        Assert.Equal("Alpha  A B\n    gamma 2 3  marked  now ", InlineText(paragraph.Content));
        Assert.Equal(" line one\n    line two ", Assert.Single(document.Index.Locations.Select(static item => item.Node).OfType<CodeBlock>()).Code);
    }

    private static MemoryStream Create(string chapter) => MinimalEpubFactory.Create(
        package: """
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="book-id">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="book-id">urn:flow:test:mixed-xhtml</dc:identifier><dc:title>Mixed XHTML</dc:title>
              </metadata>
              <manifest><item id="chapter" href="text/chapter-1.xhtml" media-type="application/xhtml+xml"/></manifest>
              <spine><itemref idref="chapter"/></spine>
            </package>
            """,
        chapterOne: chapter,
        includeSecondChapter: false,
        includeImage: false);

    private static string[] ParagraphTexts(FlowDocument document) => document.Index.Locations
        .Select(static item => item.Node)
        .OfType<Paragraph>()
        .Select(static paragraph => InlineText(paragraph.Content))
        .ToArray();

    private static string InlineText(IEnumerable<InlineNode> nodes) => string.Concat(nodes.Select(static node => node switch
    {
        Text text => text.Value,
        InlineContainerNode container => InlineText(container.Children),
        InlineCode code => code.Code,
        LineBreak => "\n",
        _ => string.Empty,
    }));

    private static void AssertSingleAggregatedDiagnostic(EpubImportResult result, string messagePart) =>
        Assert.Single(result.Diagnostics, item =>
            item.Code == EpubDiagnosticCodes.UnsupportedElement
            && item.Message.Contains(messagePart, StringComparison.Ordinal));
}
