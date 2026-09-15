using System.Text;
using System.Xml.Linq;
using Flow.Documents;
using Flow.Layout;
using Flow.Rendering.Html;
using Flow.Security;

namespace Flow.Epub.Tests;

public sealed class EpubCssImportTests
{
    private const string Package = """
        <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="book-id">
          <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
            <dc:identifier id="book-id">urn:flow:test:css</dc:identifier>
            <dc:title>CSS test</dc:title>
          </metadata>
          <manifest>
            <item id="chapter" href="text/chapter-1.xhtml" media-type="application/xhtml+xml" />
            <item id="styles" href="styles/book.css" media-type="text/css" />
          </manifest>
          <spine><itemref idref="chapter" /></spine>
        </package>
        """;

    private const string ExternalCss = """
        body { font-family: "Book Serif", serif; font-size: 18px; line-height: 140%; }
        p { font-size: 16px; text-align: left; }
        .lead { font-size: 20px; font-weight: 600; font-style: italic; letter-spacing: 0.1em;
                text-decoration: underline line-through; margin: 1em 0 2em; text-indent: 1.5em; }
        #target { font-size: 22px; text-transform: uppercase; }
        """;

    [Fact]
    public async Task ImportAsync_AppliesExternalEmbeddedInlineInheritanceSpecificityAndOrder()
    {
        const string head = """
            <link rel="stylesheet" href="../styles/book.css" />
            <style>p { text-align: justify; } p.lead { font-style: oblique; }</style>
            """;
        const string body = """
            <h1 id="title">Title</h1>
            <p id="ordinary">Ordinary</p>
            <p id="target" class="lead" style="font-size: 24px; text-align: center;">Target</p>
            """;

        var result = await ImportAsync(head, body, ExternalCss);

        var document = Assert.IsType<FlowDocument>(result.Document);
        var presentation = Assert.IsType<DocumentPresentation>(document.Presentation);
        var target = document.Index.Locations.Select(static location => location.Node)
            .OfType<Paragraph>().Single(static paragraph => paragraph.Id.Value.Contains("target", StringComparison.Ordinal));
        var style = presentation.NodeTypography[target.Id];
        Assert.Equal("Book Serif", style.FontFamily);
        Assert.Equal(Length.Px(24), style.FontSize);
        Assert.Equal(FontWeight.SemiBold, style.FontWeight);
        Assert.Equal(FontStyle.Oblique, style.FontStyle);
        Assert.Equal(1.4, style.LineHeight);
        Assert.Equal(Length.Em(0.1), style.LetterSpacing);
        Assert.Equal(TextAlignment.Center, style.TextAlignment);
        Assert.Equal(TextTransform.Uppercase, style.TextTransform);
        Assert.Equal(TextDecoration.Underline | TextDecoration.LineThrough, style.TextDecoration);
        Assert.Equal(Length.Em(1), style.MarginBefore);
        Assert.Equal(Length.Em(2), style.MarginAfter);
        Assert.Equal(Length.Em(1.5), style.Indent);

        var ordinary = document.Index.Locations.Select(static location => location.Node)
            .OfType<Paragraph>().Single(static paragraph => paragraph.Id.Value.Contains("ordinary", StringComparison.Ordinal));
        Assert.Equal(Length.Px(16), presentation.NodeTypography[ordinary.Id].FontSize);
        Assert.Equal(TextAlignment.Justify, presentation.NodeTypography[ordinary.Id].TextAlignment);
        Assert.Equal("Book Serif", presentation.NodeTypography[ordinary.Id].FontFamily);
    }

    [Fact]
    public async Task ImportAsync_SerializesTypedPresentationAndRendererHonorsUserPrecedence()
    {
        const string head = "<link rel=\"stylesheet\" href=\"../styles/book.css\" />";
        const string body = "<p id=\"target\" class=\"lead\">Target</p>";
        var result = await ImportAsync(head, body, ExternalCss);
        var document = Assert.IsType<FlowDocument>(result.Document);

        var serializer = new FlowJsonDocumentSerializer();
        await using var json = new MemoryStream();
        await serializer.SerializeAsync(document, json);
        var jsonText = Encoding.UTF8.GetString(json.ToArray());
        Assert.Contains("nodeTypography", jsonText, StringComparison.Ordinal);
        Assert.DoesNotContain(".lead", jsonText, StringComparison.Ordinal);
        Assert.DoesNotContain("font-family:", jsonText, StringComparison.Ordinal);
        json.Position = 0;
        var roundTripped = await serializer.DeserializeAsync(json);
        Assert.Equal(document.Presentation!.NodeTypography, roundTripped.Presentation!.NodeTypography);

        var preferences = new UserReadingPreferences(preferredBodyFont: "Reader Serif");
        var layout = new AdaptiveLayoutEngine().Layout(
            roundTripped,
            new LayoutContext(390, 844, DeviceClass.Phone, userPreferences: preferences));
        var html = new HtmlDocumentRenderer().RenderToString(roundTripped, layout, preferences);
        var paragraph = XDocument.Parse(html).Descendants().Single(static element => element.Name.LocalName == "p");
        var renderedStyle = Assert.IsType<string>((string?)paragraph.Attribute("style"));
        Assert.Contains("font-family: \"Reader Serif\"", renderedStyle, StringComparison.Ordinal);
        Assert.Contains("text-decoration: underline line-through", renderedStyle, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportAsync_ChangingCssDoesNotChangeCanonicalHash()
    {
        const string head = "<style>p { font-family: Alpha; font-size: 14px; }</style>";
        const string changedHead = "<style>p { font-family: Beta; font-size: 28px; text-align: right; }</style>";
        const string body = "<p id=\"same\">Canonical content</p>";
        var first = Assert.IsType<FlowDocument>((await ImportAsync(head, body)).Document);
        var second = Assert.IsType<FlowDocument>((await ImportAsync(changedHead, body)).Document);
        Assert.NotEqual(first.Presentation, second.Presentation);

        var integrity = new Sha256DocumentIntegrityService(new FlowDocumentCanonicalizer());
        Assert.Equal(integrity.ComputeHash(first), integrity.ComputeHash(second));
    }

    [Fact]
    public async Task ImportAsync_AggregatesIgnoredRulesAndBlocksExternalStylesheets()
    {
        var paragraphs = string.Concat(Enumerable.Range(1, 25).Select(index =>
            $"<p id=\"p{index}\" style=\"position:absolute; color:red;\">Text</p>"));
        var head = """
            <link rel="stylesheet" href="https://example.com/evil.css" />
            <style>
              p span { font-size: 99px; }
              p { float: left; font-family: url(https://example.com/font); }
            </style>
            """;

        var result = await ImportAsync(head, paragraphs);

        Assert.NotNull(result.Document);
        Assert.Single(result.Diagnostics.Where(static diagnostic =>
            diagnostic.Code == EpubDiagnosticCodes.UnsupportedCssProperty
            && diagnostic.Message.Contains("position", StringComparison.Ordinal)));
        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Code == EpubDiagnosticCodes.UnsupportedCssProperty
            && diagnostic.Message.Contains("25 occurrences", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == EpubDiagnosticCodes.UnsupportedCssSelector);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == EpubDiagnosticCodes.ExternalStylesheetBlocked);
    }

    [Fact]
    public async Task ImportAsync_DiagnosesInlineTargetThatCannotBeRepresentedIndependently()
    {
        const string head = "<style>.marked { font-weight: bold; }</style>";
        const string body = "<p id=\"paragraph\">Before <span class=\"marked\">marked</span> after.</p>";

        var result = await ImportAsync(head, body);

        Assert.NotNull(result.Document);
        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Code == EpubDiagnosticCodes.CssTargetNotRepresentable);
    }

    private static async Task<EpubImportResult> ImportAsync(
        string head,
        string body,
        string? stylesheet = null)
    {
        var chapter = $"""
            <html xmlns="http://www.w3.org/1999/xhtml"><head>{head}</head><body>{body}</body></html>
            """;
        var additional = new Dictionary<string, string> { ["EPUB/styles/book.css"] = stylesheet ?? string.Empty };
        await using var epub = MinimalEpubFactory.Create(
            package: Package,
            chapterOne: chapter,
            includeSecondChapter: false,
            includeImage: false,
            additionalTextEntries: additional);
        return await new EpubImporter().ImportAsync(epub);
    }
}
