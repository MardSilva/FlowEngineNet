using System.Text;
using System.Xml.Linq;
using Flow.Documents;
using Flow.Layout;
using Flow.Rendering.Html;

namespace Flow.Epub.Tests;

public sealed class EpubSvgMathImportTests
{
    [Fact]
    public async Task ImportAsync_PreservesSafeInlineAndBlockMathThroughJsonAndHtml()
    {
        const string chapter = """
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:m="http://www.w3.org/1998/Math/MathML"><body>
              <p id="inline">Euler: <m:math alttext="e squared"><m:msup><m:mi>e</m:mi><m:mn>2</m:mn></m:msup></m:math>.</p>
              <m:math id="fraction" display="block" alttext="one half"><m:mfrac><m:mn>1</m:mn><m:mn>2</m:mn></m:mfrac></m:math>
            </body></html>
            """;
        await using var epub = Create(chapter);

        var result = await new EpubImporter().ImportAsync(epub);

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics));
        var document = Assert.IsType<FlowDocument>(result.Document);
        var paragraph = document.Index.Locations.Select(static item => item.Node).OfType<Paragraph>().Single();
        var inline = Assert.Single(paragraph.Content.OfType<InlineMath>());
        Assert.Equal("e squared", inline.AlternativeText);
        var block = Assert.Single(document.Index.Locations.Select(static item => item.Node).OfType<MathExpression>());
        Assert.Equal("fraction", block.Id.Value.Split('-').Last());
        Assert.Equal("one half", block.AlternativeText);

        await using var json = new MemoryStream();
        var serializer = new FlowJsonDocumentSerializer();
        await serializer.SerializeAsync(document, json);
        json.Position = 0;
        var roundTripped = await serializer.DeserializeAsync(json);
        Assert.Equal(
            "12",
            PlainText(Assert.Single(roundTripped.Index.Locations.Select(static item => item.Node).OfType<MathExpression>()).Root));

        var preferences = new UserReadingPreferences();
        var layout = new AdaptiveLayoutEngine().Layout(
            roundTripped,
            new LayoutContext(800, 1000, DeviceClass.Tablet, userPreferences: preferences));
        var html = new HtmlDocumentRenderer().RenderToString(roundTripped, layout, preferences);
        var parsed = XDocument.Parse(html);
        XNamespace math = "http://www.w3.org/1998/Math/MathML";
        Assert.Equal(2, parsed.Descendants(math + "math").Count());
        Assert.Contains("<mfrac>", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"one half\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportAsync_RemovesMaliciousMathAndDiagnosesEverySemanticLossClass()
    {
        const string chapter = """
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:m="http://www.w3.org/1998/Math/MathML"><body>
              <p>Unsafe <m:math alttext="safe alternative" onclick="alert(1)">
                <m:script>steal()</m:script>
                <m:unknown><m:mi href="https://example.invalid/value">x</m:mi></m:unknown>
                <m:semantics><m:annotation-xml encoding="application/xhtml+xml"><script>alert(2)</script></m:annotation-xml></m:semantics>
              </m:math></p>
            </body></html>
            """;
        await using var epub = Create(chapter);

        var result = await new EpubImporter().ImportAsync(epub);

        var document = Assert.IsType<FlowDocument>(result.Document);
        var math = Assert.Single(document.Index.Locations.Select(static item => item.Node)
            .OfType<Paragraph>().SelectMany(static paragraph => paragraph.Content).OfType<InlineMath>());
        Assert.Equal("safe alternative", math.AlternativeText);
        Assert.Equal("x", PlainText(math.Root).Trim());
        Assert.Contains(result.Diagnostics, static item => item.Code == EpubDiagnosticCodes.MathSemanticLoss);

        var preferences = new UserReadingPreferences();
        var layout = new AdaptiveLayoutEngine().Layout(
            document,
            new LayoutContext(390, 844, DeviceClass.Phone, userPreferences: preferences));
        var html = new HtmlDocumentRenderer().RenderToString(document, layout, preferences);
        Assert.DoesNotContain("steal", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("example.invalid", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("annotation-xml", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImportAsync_SanitizesSvgHandlersExternalReferencesAndActiveChildren()
    {
        const string package = """
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="id">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:identifier id="id">svg-test</dc:identifier><dc:title>SVG</dc:title></metadata>
              <manifest><item id="chapter" href="text/chapter-1.xhtml" media-type="application/xhtml+xml"/><item id="svg" href="images/unsafe.svg" media-type="image/svg+xml"/></manifest>
              <spine><itemref idref="chapter"/></spine>
            </package>
            """;
        const string chapter = """
            <html xmlns="http://www.w3.org/1999/xhtml"><body><img src="../images/unsafe.svg" alt="diagram"/></body></html>
            """;
        const string unsafeSvg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="10" height="10" onload="alert(1)">
              <script>alert(2)</script><image href="https://example.invalid/a.png"/><rect width="10" height="10" style="fill:red"/>
            </svg>
            """;
        await using var epub = MinimalEpubFactory.Create(
            package: package,
            chapterOne: chapter,
            includeSecondChapter: false,
            includeImage: false,
            additionalBinaryEntries: new Dictionary<string, byte[]>
            {
                ["EPUB/images/unsafe.svg"] = Encoding.UTF8.GetBytes(unsafeSvg),
            });

        var result = await new EpubImporter().ImportAsync(epub);

        var asset = Assert.Single(Assert.IsType<FlowDocument>(result.Document).Assets).Value;
        var safeSvg = Encoding.UTF8.GetString(asset.Data.ToArray());
        Assert.Contains("<rect", safeSvg, StringComparison.Ordinal);
        Assert.DoesNotContain("script", safeSvg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onload", safeSvg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("example.invalid", safeSvg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("style=", safeSvg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(result.Diagnostics, static item => item.Code == EpubDiagnosticCodes.SanitizedSvg);
    }

    private static MemoryStream Create(string chapter)
    {
        const string package = """
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="id">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:identifier id="id">math-test</dc:identifier><dc:title>Math</dc:title></metadata>
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

    private static string PlainText(MathNode node) => node switch
    {
        MathText text => text.Value,
        MathElement element => string.Concat(element.Children.Select(PlainText)),
        _ => string.Empty,
    };
}
