using Flow.Documents;
using Flow.Layout;
using Flow.Rendering.Html;
using Flow.Security;

namespace Flow.Epub.Tests;

public sealed class EpubRealWorldHardeningTests
{
    [Fact]
    public async Task InlineAnchorApproximations_AreAggregatedPerResourceWithoutLosingSourceMapEntries()
    {
        const string chapter = """
            <html xmlns="http://www.w3.org/1999/xhtml"><body>
              <p id="paragraph">Before <span id="first">first</span> and <em id="second">second</em>.</p>
            </body></html>
            """;
        await using var epub = MinimalEpubFactory.Create(chapterOne: chapter, includeSecondChapter: false);

        var result = await new EpubImporter().ImportAsync(epub);

        var diagnostic = Assert.Single(result.Diagnostics, static item =>
            item.Code == EpubDiagnosticCodes.UnsupportedElement
            && item.Message.Contains("anchor(s)", StringComparison.Ordinal));
        Assert.Contains("2 anchor(s)", diagnostic.Message, StringComparison.Ordinal);
        var sourceMap = Assert.IsType<EpubSourceMap>(result.SourceMap);
        Assert.True(sourceMap.TryResolve("EPUB/text/chapter-1.xhtml", "first", out var first));
        Assert.True(sourceMap.TryResolve("EPUB/text/chapter-1.xhtml", "second", out var second));
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task SharedExternalStylesheet_IsParsedOnceAndCharsetDoesNotConsumeFirstRule()
    {
        const string package = """
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="id">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:identifier id="id">urn:test:shared-css</dc:identifier><dc:title>Shared CSS</dc:title></metadata>
              <manifest>
                <item id="one" href="text/chapter-1.xhtml" media-type="application/xhtml+xml" />
                <item id="two" href="text/chapter-2.xhtml" media-type="application/xhtml+xml" />
                <item id="css" href="styles/book.css" media-type="text/css" />
              </manifest>
              <spine><itemref idref="one" /><itemref idref="two" /></spine>
            </package>
            """;
        const string chapter = """
            <html xmlns="http://www.w3.org/1999/xhtml"><head><link rel="stylesheet" href="../styles/book.css" /></head><body><p>Styled</p></body></html>
            """;
        const string css = """
            @charset "UTF-8";
            p { font-size: 20px; color: navy; }
            """;
        await using var epub = MinimalEpubFactory.Create(
            package: package,
            chapterOne: chapter,
            chapterTwo: chapter,
            includeImage: false,
            additionalTextEntries: new Dictionary<string, string>
            {
                ["EPUB/styles/book.css"] = css,
            });

        var result = await new EpubImporter().ImportAsync(epub);

        var unsupportedColor = Assert.Single(result.Diagnostics, static item =>
            item.Code == EpubDiagnosticCodes.UnsupportedCssProperty
            && item.Message.Contains("'color'", StringComparison.Ordinal));
        Assert.DoesNotContain("occurrences", unsupportedColor.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Diagnostics, static item => item.Message.Contains("@charset", StringComparison.Ordinal));
        var document = Assert.IsType<FlowDocument>(result.Document);
        var presentation = Assert.IsType<DocumentPresentation>(document.Presentation);
        var paragraphs = document.Index.Locations.Select(static item => item.Node).OfType<Paragraph>().ToArray();
        Assert.Equal(2, paragraphs.Length);
        Assert.All(paragraphs, paragraph => Assert.Equal(Length.Px(20), presentation.NodeTypography[paragraph.Id].FontSize));
    }

    [Fact]
    public async Task NavigationFallbackAndUnknownManifestProperty_DoNotMarkWholeResourcesUnsupported()
    {
        const string package = """
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="id">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:identifier id="id">urn:test:manifest-property</dc:identifier><dc:title>Navigation</dc:title></metadata>
              <manifest>
                <item id="chapter" href="text/chapter-1.xhtml" media-type="application/xhtml+xml" properties="svg" />
                <item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav" />
                <item id="ncx" href="toc.ncx" media-type="application/x-dtbncx+xml" />
              </manifest>
              <spine toc="ncx"><itemref idref="chapter" /></spine>
            </package>
            """;
        const string chapter = """
            <html xmlns="http://www.w3.org/1999/xhtml"><body><h1 id="start">Start</h1></body></html>
            """;
        const string navigation = """
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops"><body><nav epub:type="toc"><ol><li><a href="text/chapter-1.xhtml#start">Start</a></li></ol></nav></body></html>
            """;
        const string ncx = """
            <ncx xmlns="http://www.daisy.org/z3986/2005/ncx/"><navMap><navPoint id="one"><navLabel><text>Start</text></navLabel><content src="text/chapter-1.xhtml#start" /></navPoint></navMap></ncx>
            """;
        await using var epub = MinimalEpubFactory.Create(
            package: package,
            chapterOne: chapter,
            includeSecondChapter: false,
            includeImage: false,
            additionalTextEntries: new Dictionary<string, string>
            {
                ["EPUB/nav.xhtml"] = navigation,
                ["EPUB/toc.ncx"] = ncx,
            });

        var result = await new EpubImporter().ImportAsync(epub);
        var fidelity = new EpubFidelityAnalyzer().Analyze(result);

        Assert.Contains(result.Diagnostics, static item => item.Code == EpubDiagnosticCodes.UnsupportedManifestProperty);
        Assert.DoesNotContain(result.Diagnostics, static item =>
            item.Code == EpubDiagnosticCodes.UnsupportedResource
            && item.Resource is "EPUB/text/chapter-1.xhtml" or "EPUB/toc.ncx");
        Assert.Equal(0, fidelity.Summary.UnsupportedCount);
        Assert.Equal(0, fidelity.Summary.LostCount);
    }

    [Fact]
    public async Task EncodedMailtoAndImageOnlyLink_ArePreservedOrReportedWithoutSilentLoss()
    {
        const string chapter = """
            <html xmlns="http://www.w3.org/1999/xhtml"><body>
              <p><a href="mailto:reader%40example.invalid?subject=Hello%20Flow">Email</a></p>
              <p><a href="mailto:reader@example.invalid?subject=unsafe%0Aheader">Blocked</a></p>
              <p><a href="https://example.invalid/image"><img src="../images/flow.png" alt="" /></a></p>
            </body></html>
            """;
        await using var epub = MinimalEpubFactory.Create(chapterOne: chapter);

        var result = await new EpubImporter().ImportAsync(epub);

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics));
        var document = Assert.IsType<FlowDocument>(result.Document);
        var link = Assert.Single(document.Index.Locations
            .Select(static item => item.Node)
            .OfType<Paragraph>()
            .SelectMany(static paragraph => paragraph.Content)
            .OfType<Link>());
        Assert.Equal("mailto:reader%40example.invalid?subject=Hello%20Flow", link.Target);
        Assert.Single(document.Index.Locations.Select(static item => item.Node).OfType<Figure>());
        Assert.Single(result.Diagnostics, static item =>
            item.Code == EpubDiagnosticCodes.LinkedImageTargetNotRepresentable);
        Assert.Contains(result.Diagnostics, static item =>
            item.Code == EpubDiagnosticCodes.InvalidReference
            && item.Message.Contains("Unsafe mailto", StringComparison.Ordinal));

        var fidelity = new EpubFidelityAnalyzer().Analyze(result);
        var externalLinks = fidelity.Measurements.Single(static item =>
            item.Metric == EpubFidelityMetric.ExternalLinks);
        Assert.Equal(2, externalLinks.SourceCount);
        Assert.Equal(1, externalLinks.DestinationCount);
        Assert.Equal(1, externalLinks.UnsupportedCount);
        Assert.Equal(0, externalLinks.LostCount);
        Assert.Contains(fidelity.Findings, static finding =>
            finding.RelatedDiagnosticCode == EpubDiagnosticCodes.LinkedImageTargetNotRepresentable
            && finding.Status == EpubFidelityStatus.Unsupported);

        var serializer = new FlowJsonDocumentSerializer();
        var integrity = new Sha256DocumentIntegrityService(new FlowDocumentCanonicalizer());
        await using var json = new MemoryStream();
        await serializer.SerializeAsync(document, json);
        json.Position = 0;
        var roundTripped = await serializer.DeserializeAsync(json);
        Assert.Equal(integrity.ComputeHash(document), integrity.ComputeHash(roundTripped));

        var preferences = new UserReadingPreferences();
        var layout = new AdaptiveLayoutEngine().Layout(
            roundTripped,
            new LayoutContext(390, 844, userPreferences: preferences));
        var html = new HtmlDocumentRenderer().RenderToString(roundTripped, layout, preferences);
        Assert.Contains("href=\"mailto:reader%40example.invalid?subject=Hello%20Flow\"", html, StringComparison.Ordinal);
        Assert.Contains("<figure", html, StringComparison.Ordinal);
        Assert.DoesNotContain("https://example.invalid/image", html, StringComparison.Ordinal);
    }
}
