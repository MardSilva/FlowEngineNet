using System.Xml.Linq;
using Flow.Documents;
using Flow.Layout;
using Flow.Rendering.Html;

namespace Flow.Epub.Tests;

public sealed class EpubInternationalizationImportTests
{
    [Fact]
    public async Task ImportAsync_PreservesLanguagesDirectionsRubyAndMixedBidirectionalContent()
    {
        const string chapter = """
            <html xmlns="http://www.w3.org/1999/xhtml" xml:lang="pt-PT"><body>
              <p id="mixed">Olá <span lang="en">Flow Engine</span>. Utilizador: <bdi dir="auto">مريم-42</bdi>.</p>
              <section lang="ar" dir="rtl"><p id="arabic">مرحبا <span lang="en" dir="ltr">Flow</span></p></section>
              <p id="override"><bdo dir="rtl">ABC 123</bdo></p>
              <p id="japanese" lang="ja"><ruby>漢<rp>(</rp><rt>かん</rt><rp>)</rp></ruby>字</p>
            </body></html>
            """;
        await using var epub = Create(chapter);

        var result = await new EpubImporter().ImportAsync(epub);

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics));
        var document = Assert.IsType<FlowDocument>(result.Document);
        var paragraphs = document.Index.Locations.Select(static item => item.Node).OfType<Paragraph>().ToArray();
        Assert.Equal(4, paragraphs.Length);

        var portuguese = Assert.IsType<LanguageSpan>(Assert.Single(paragraphs[0].Content));
        Assert.Equal("pt-PT", portuguese.Language.Value);
        Assert.Contains(portuguese.Children, static node => node is LanguageSpan { Language.Value: "en" });
        Assert.Contains(portuguese.Children, static node => node is BidirectionalSpan
        {
            Direction: TextDirection.Auto,
            Mode: BidirectionalMode.Isolation,
        });

        var arabic = Assert.IsType<LanguageSpan>(Assert.Single(paragraphs[1].Content));
        Assert.Equal("ar", arabic.Language.Value);
        var rtl = Assert.IsType<BidirectionalSpan>(Assert.Single(arabic.Children));
        Assert.Equal(TextDirection.RightToLeft, rtl.Direction);
        Assert.Contains(rtl.Children, static node => node is LanguageSpan { Language.Value: "en" });

        var overrideLanguage = Assert.IsType<LanguageSpan>(Assert.Single(paragraphs[2].Content));
        Assert.Contains(overrideLanguage.Children, static node => node is BidirectionalSpan
        {
            Direction: TextDirection.RightToLeft,
            Mode: BidirectionalMode.Override,
        });

        var japanese = Assert.IsType<LanguageSpan>(Assert.Single(paragraphs[3].Content));
        Assert.Equal("ja", japanese.Language.Value);
        var ruby = Assert.Single(japanese.Children.OfType<Ruby>());
        Assert.Equal(2, ruby.Children.OfType<RubyFallbackParenthesis>().Count());
        Assert.Equal("かん", TextOf(Assert.Single(ruby.Children.OfType<RubyAnnotation>()).Children));

        await using var json = new MemoryStream();
        var serializer = new FlowJsonDocumentSerializer();
        await serializer.SerializeAsync(document, json);
        json.Position = 0;
        var restored = await serializer.DeserializeAsync(json);
        var preferences = new UserReadingPreferences();
        var layout = new AdaptiveLayoutEngine().Layout(
            restored,
            new LayoutContext(390, 844, DeviceClass.Phone, userPreferences: preferences));
        var html = new HtmlDocumentRenderer().RenderToString(restored, layout, preferences);
        var parsed = XDocument.Parse(html);

        Assert.Contains(parsed.Descendants("span"), static item => (string?)item.Attribute("lang") == "pt-PT");
        Assert.Contains(parsed.Descendants("span"), static item =>
            (string?)item.Attribute("lang") == "ar" && item.Descendants("span").Any(span => (string?)span.Attribute("dir") == "rtl"));
        Assert.Contains(parsed.Descendants("bdi"), static item => (string?)item.Attribute("dir") == "auto");
        Assert.Contains(parsed.Descendants("bdo"), static item => (string?)item.Attribute("dir") == "rtl");
        Assert.Single(parsed.Descendants("ruby"));
        Assert.Single(parsed.Descendants("rt"));
        Assert.Equal(2, parsed.Descendants("rp").Count());
        Assert.Contains("مرحبا", html, StringComparison.Ordinal);
        Assert.Contains("漢", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportAsync_DiagnosesInvalidDirectionLanguageConflictsAndMalformedRubyWithoutTextLoss()
    {
        const string chapter = """
            <html xmlns="http://www.w3.org/1999/xhtml"><body>
              <p lang="en" xml:lang="pt"><span dir="sideways">preserved</span><bdo>override text</bdo></p>
              <p lang="bad_tag">invalid language</p>
              <p><rt>orphan reading</rt><ruby>base only</ruby></p>
            </body></html>
            """;
        await using var epub = Create(chapter);

        var result = await new EpubImporter().ImportAsync(epub);

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Contains(result.Diagnostics, static item => item.Code == EpubDiagnosticCodes.ConflictingInlineLanguage);
        Assert.Contains(result.Diagnostics, static item => item.Code == EpubDiagnosticCodes.InvalidLanguage);
        Assert.Contains(result.Diagnostics, static item => item.Code == EpubDiagnosticCodes.InvalidTextDirection);
        Assert.Contains(result.Diagnostics, static item => item.Code == EpubDiagnosticCodes.InvalidRubyStructure);
        var text = string.Concat(Assert.IsType<FlowDocument>(result.Document).Index.Locations
            .Select(static item => item.Node).OfType<Paragraph>().SelectMany(static paragraph => paragraph.Content)
            .Select(TextOf));
        Assert.Contains("preserved", text, StringComparison.Ordinal);
        Assert.Contains("override text", text, StringComparison.Ordinal);
        Assert.Contains("invalid language", text, StringComparison.Ordinal);
        Assert.Contains("orphan reading", text, StringComparison.Ordinal);
        Assert.Contains("base only", text, StringComparison.Ordinal);
    }

    private static MemoryStream Create(string chapter)
    {
        const string package = """
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="id">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:identifier id="id">i18n-test</dc:identifier><dc:title>Internationalization</dc:title><dc:language>pt-PT</dc:language></metadata>
              <manifest><item id="chapter" href="text/chapter-1.xhtml" media-type="application/xhtml+xml"/></manifest>
              <spine><itemref idref="chapter"/></spine>
            </package>
            """;
        return MinimalEpubFactory.Create(
            package: package,
            chapterOne: chapter,
            includeSecondChapter: false,
            includeImage: false);
    }

    private static string TextOf(IEnumerable<InlineNode> nodes) => string.Concat(nodes.Select(TextOf));

    private static string TextOf(InlineNode node) => node switch
    {
        Text text => text.Value,
        InlineContainerNode container => TextOf(container.Children),
        InlineCode code => code.Code,
        FootnoteReference reference => TextOf(reference.Label),
        LineBreak => "\n",
        _ => string.Empty,
    };
}
