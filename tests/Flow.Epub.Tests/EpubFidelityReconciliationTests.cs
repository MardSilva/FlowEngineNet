using Flow.Documents;
using Flow.Layout;
using Flow.Rendering.Html;
using Flow.Security;

namespace Flow.Epub.Tests;

public sealed class EpubFidelityReconciliationTests
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    [Fact]
    public async Task LinkedImageInsideTransparentContainers_IsPreservedAsFigure()
    {
        const string body = """
            <div class="outer"><div class="inner"><a href="https://example.invalid/art"><img src="../images/art.png" alt="" /></a></div></div>
            """;
        var result = await ImportAsync(
            Package("<item id=\"art\" href=\"images/art.png\" media-type=\"image/png\" />"),
            body,
            new Dictionary<string, byte[]> { ["EPUB/images/art.png"] = Png });

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics));
        var document = Assert.IsType<FlowDocument>(result.Document);
        var figure = Assert.Single(document.Index.Locations.Select(static item => item.Node).OfType<Figure>());
        Assert.Equal("https://example.invalid/art", figure.Link?.ExternalUri);
        var images = Measurement(result, EpubFidelityMetric.Images);
        Assert.Equal(1, images.SourceCount);
        Assert.Equal(1, images.DestinationCount);
        Assert.Equal(1, images.TransformedCount);
        Assert.Equal(0, images.LostCount);
    }

    [Fact]
    public async Task ImageOnlyParagraph_IsMeasuredAsTransformedRatherThanLost()
    {
        const string body = """
            <p class="spacer"><span>&#160;</span></p>
            <p class="art"><span><img src="../images/art.png" alt="Artwork" /></span><br /></p>
            """;
        var result = await ImportAsync(
            Package("<item id=\"art\" href=\"images/art.png\" media-type=\"image/png\" />"),
            body,
            new Dictionary<string, byte[]> { ["EPUB/images/art.png"] = Png });

        var paragraphs = Measurement(result, EpubFidelityMetric.Paragraphs);
        Assert.Equal(2, paragraphs.SourceCount);
        Assert.Equal(1, paragraphs.TransformedCount);
        Assert.Equal(0, paragraphs.LostCount);
        Assert.Equal(0, new EpubFidelityAnalyzer().Analyze(result).Summary.LostCount);
    }

    [Fact]
    public async Task UniqueImagePathCaseMismatch_IsRecoveredDeterministically()
    {
        const string body = """
            <div><img src="../images/State-Of-Art.png" alt="Artwork" /></div>
            """;
        var package = Package("<item id=\"art\" href=\"images/State-of-Art.png\" media-type=\"image/png\" />");
        var assets = new Dictionary<string, byte[]> { ["EPUB/images/State-of-Art.png"] = Png };

        var first = await ImportAsync(package, body, assets);
        var second = await ImportAsync(package, body, assets);

        Assert.True(first.IsSuccess, string.Join(Environment.NewLine, first.Diagnostics));
        Assert.Contains(first.Diagnostics, static item =>
            item.Code == EpubDiagnosticCodes.ArchivePathCaseMismatchRecovered
            && item.Resource == "EPUB/images/State-Of-Art.png");
        Assert.DoesNotContain(first.Diagnostics, static item =>
            item.Code == EpubDiagnosticCodes.MissingResource
            && item.Resource == "EPUB/images/State-Of-Art.png");
        var firstDocument = Assert.IsType<FlowDocument>(first.Document);
        var secondDocument = Assert.IsType<FlowDocument>(second.Document);
        Assert.Single(firstDocument.Index.Locations.Select(static item => item.Node).OfType<Figure>());
        Assert.Equal(0, new EpubFidelityAnalyzer().Analyze(first).Summary.LostCount);

        var integrity = new Sha256DocumentIntegrityService(new FlowDocumentCanonicalizer());
        Assert.Equal(integrity.ComputeHash(firstDocument), integrity.ComputeHash(secondDocument));
        var preferences = new UserReadingPreferences();
        var layout = new AdaptiveLayoutEngine().Layout(
            firstDocument,
            new LayoutContext(390, 844, userPreferences: preferences));
        var html = new HtmlDocumentRenderer().RenderToString(firstDocument, layout, preferences);
        Assert.Contains("<img", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AmbiguousImagePathCaseMismatch_RemainsMissing()
    {
        const string body = """
            <div><img src="../images/STATE-OF-ART.png" alt="Artwork" /></div>
            """;
        var package = Package("""
            <item id="first" href="images/State-of-Art.png" media-type="image/png" />
            <item id="second" href="images/state-of-art.png" media-type="image/png" />
            """);
        var result = await ImportAsync(
            package,
            body,
            new Dictionary<string, byte[]>
            {
                ["EPUB/images/State-of-Art.png"] = Png,
                ["EPUB/images/state-of-art.png"] = Png,
            });

        Assert.Contains(result.Diagnostics, static item =>
            item.Code == EpubDiagnosticCodes.MissingResource
            && item.Resource == "EPUB/images/STATE-OF-ART.png");
        Assert.DoesNotContain(result.Diagnostics, static item =>
            item.Code == EpubDiagnosticCodes.ArchivePathCaseMismatchRecovered);
        Assert.Equal(1, Measurement(result, EpubFidelityMetric.Images).LostCount);
    }

    private static EpubFidelityMeasurement Measurement(EpubImportResult result, EpubFidelityMetric metric) =>
        new EpubFidelityAnalyzer().Analyze(result).Measurements.Single(item => item.Metric == metric);

    private static async Task<EpubImportResult> ImportAsync(
        string package,
        string body,
        IReadOnlyDictionary<string, byte[]> assets)
    {
        var chapter = $"""
            <html xmlns="http://www.w3.org/1999/xhtml">
              <head><title>Fixture</title></head>
              <body>{body}</body>
            </html>
            """;
        await using var epub = MinimalEpubFactory.Create(
            package: package,
            chapterOne: chapter,
            includeSecondChapter: false,
            includeImage: false,
            additionalBinaryEntries: assets);
        return await new EpubImporter().ImportAsync(epub);
    }

    private static string Package(string additionalManifest) => $"""
        <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="book-id">
          <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
            <dc:identifier id="book-id">urn:flow:test:fidelity-reconciliation</dc:identifier>
            <dc:title>Fidelity reconciliation fixture</dc:title>
            <dc:language>en</dc:language>
          </metadata>
          <manifest>
            <item id="chapter" href="text/chapter-1.xhtml" media-type="application/xhtml+xml" />
            {additionalManifest}
          </manifest>
          <spine><itemref idref="chapter" /></spine>
        </package>
        """;
}
