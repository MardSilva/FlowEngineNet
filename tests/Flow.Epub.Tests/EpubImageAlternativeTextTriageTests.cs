using System.Text;
using System.Xml.Linq;
using Flow.Documents;
using Flow.Epub.Corpus;
using Flow.Layout;
using Flow.Rendering.Html;
using Flow.Security;

namespace Flow.Epub.Tests;

public sealed class EpubImageAlternativeTextTriageTests
{
    private const string Package = """
        <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="book-id">
          <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
            <dc:identifier id="book-id">urn:flow:test:image-alternative</dc:identifier>
            <dc:title>Alternative text fixture</dc:title>
            <dc:language>en</dc:language>
          </metadata>
          <manifest>
            <item id="chapter" href="text/chapter-1.xhtml" media-type="application/xhtml+xml" />
            <item id="image" href="images/art.png" media-type="image/png" />
          </manifest>
          <spine><itemref idref="chapter" /></spine>
        </package>
        """;

    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    [Theory]
    [InlineData("<img src=\"../images/art.png\" aria-label=\"Authored ARIA label\" />", "Authored ARIA label", "aria-label")]
    [InlineData("<p id=\"label\">First label</p><span id=\"detail\">and detail</span><img src=\"../images/art.png\" aria-labelledby=\"label detail\" />", "First label and detail", "aria-labelledby")]
    [InlineData("<img src=\"../images/art.png\" title=\"Authored title\" />", "Authored title", "title")]
    [InlineData("<figure><img src=\"../images/art.png\" /><figcaption>Authored caption</figcaption></figure>", "Authored caption", "figcaption")]
    public async Task ExplicitAuthoredFallback_IsPreservedAndDiagnosed(
        string body,
        string expected,
        string source)
    {
        var import = await ImportAsync(body);

        var document = Assert.IsType<FlowDocument>(import.Document);
        var figure = Assert.Single(document.Index.Locations.Select(static item => item.Node).OfType<Figure>());
        Assert.Equal(expected, figure.AlternativeText);
        Assert.Contains(import.Diagnostics, item =>
            item.Code == EpubDiagnosticCodes.ImageAlternativeTextRecovered
            && item.Message.Contains(source, StringComparison.Ordinal));
        Assert.DoesNotContain(import.Diagnostics, static item =>
            item.Code == EpubDiagnosticCodes.MissingImageAlternativeText);

        var fidelity = new EpubFidelityAnalyzer().Analyze(import);
        Assert.Contains(fidelity.Findings, static item =>
            item.RelatedDiagnosticCode == EpubDiagnosticCodes.ImageAlternativeTextRecovered
            && item.Status == EpubFidelityStatus.Approximated
            && item.Impact == EpubFidelityImpact.Minor);

        var preferences = new UserReadingPreferences();
        var layout = new AdaptiveLayoutEngine().Layout(
            document,
            new LayoutContext(390, 844, DeviceClass.Phone, userPreferences: preferences));
        var html = new HtmlDocumentRenderer().RenderToString(document, layout, preferences);
        var renderedImage = XDocument.Parse(html).Descendants().Single(static item => item.Name.LocalName == "img");
        Assert.Equal(expected, (string?)renderedImage.Attribute("alt"));
    }

    [Fact]
    public async Task ExplicitEmptyAlt_RemainsDecorativeWithoutRecoveryDiagnostic()
    {
        var import = await ImportAsync("<img src=\"../images/art.png\" alt=\"\" title=\"Ignored title\" />");

        var figure = Assert.Single(Assert.IsType<FlowDocument>(import.Document).Index.Locations
            .Select(static item => item.Node).OfType<Figure>());
        Assert.Equal(string.Empty, figure.AlternativeText);
        Assert.DoesNotContain(import.Diagnostics, static item =>
            item.Code is EpubDiagnosticCodes.MissingImageAlternativeText
                or EpubDiagnosticCodes.ImageAlternativeTextRecovered);
    }

    [Fact]
    public async Task EquivalentAltAndAriaLabel_ProduceTheSameCanonicalHash()
    {
        var withAlt = Assert.IsType<FlowDocument>((await ImportAsync(
            "<img src=\"../images/art.png\" alt=\"Equivalent label\" />")).Document);
        var withAria = Assert.IsType<FlowDocument>((await ImportAsync(
            "<img src=\"../images/art.png\" aria-label=\"Equivalent label\" />")).Document);
        var integrity = new Sha256DocumentIntegrityService(new FlowDocumentCanonicalizer());

        Assert.Equal(integrity.ComputeHash(withAlt), integrity.ComputeHash(withAria));
    }

    [Fact]
    public async Task MissingAuthoredAlternative_RemainsAHumanReviewableSourceDefect()
    {
        var import = await ImportAsync("<h1>Nearby heading</h1><p><span><img src=\"../images/art.png\" /></span></p>");

        var document = Assert.IsType<FlowDocument>(import.Document);
        var figure = Assert.Single(document.Index.Locations.Select(static item => item.Node).OfType<Figure>());
        Assert.Null(figure.AlternativeText);
        Assert.Contains(import.Diagnostics, static item =>
            item.Code == EpubDiagnosticCodes.MissingImageAlternativeText
            && item.Message.Contains("human review", StringComparison.Ordinal));
        Assert.DoesNotContain(import.Diagnostics, static item =>
            item.Code == EpubDiagnosticCodes.ImageAlternativeTextRecovered);

        var fidelity = new EpubFidelityAnalyzer().Analyze(import);
        Assert.Contains(fidelity.Findings, static item =>
            item.RelatedDiagnosticCode == EpubDiagnosticCodes.MissingImageAlternativeText
            && item.Status == EpubFidelityStatus.Approximated
            && item.Impact == EpubFidelityImpact.Moderate);

        var matrix = new EpubPrivateDifferenceMatrixService().Create(
            Qualification(import),
            new EpubCorpusSha256(new string('A', 64)));
        var difference = Assert.Single(Assert.Single(matrix.Candidates).Differences);
        Assert.Equal(EpubPrivateDifferenceCategory.HumanReviewRequired, difference.Category);
        Assert.Equal(EpubPrivateDifferenceCause.SourceAccessibilityDefect, difference.Cause);
        Assert.Equal(EpubPrivateDifferenceMetric.Assets, difference.Metric);
        var json = Encoding.UTF8.GetString(EpubPrivateDifferenceMatrixJsonSerializer.Serialize(matrix));
        Assert.Contains("\"cause\": \"source-accessibility-defect\"", json, StringComparison.Ordinal);
    }

    private static async Task<EpubImportResult> ImportAsync(string body)
    {
        var chapter = $"""
            <html xmlns="http://www.w3.org/1999/xhtml"><head><title>Chapter</title></head><body>{body}</body></html>
            """;
        await using var epub = MinimalEpubFactory.Create(
            package: Package,
            chapterOne: chapter,
            includeSecondChapter: false,
            includeImage: false,
            additionalBinaryEntries: new Dictionary<string, byte[]> { ["EPUB/images/art.png"] = OnePixelPng });
        return await new EpubImporter().ImportAsync(epub);
    }

    private static EpubPrivateQualificationReport Qualification(EpubImportResult import)
    {
        var diagnostics = import.Diagnostics
            .Where(static item => item.Code == EpubDiagnosticCodes.MissingImageAlternativeText)
            .Select(static item => new EpubPrivateQualificationDiagnosticCount(
                item.Code,
                EpubCorpusExecutionDiagnosticSeverity.Warning,
                EpubCorpusExecutionPhase.Import,
                item.Count));
        var publication = new EpubPrivateQualificationItem(
            new EpubCorpusPublicationId("missing-image-alternative-fixture"),
            new EpubCorpusSha256(new string('B', 64)),
            EpubPrivateInventoryStatus.Ready,
            EpubPrivateQualificationStatus.Passed,
            eligible: true,
            stableAcrossRepeatedRuns: true,
            [EpubCorpusExecutionPhase.Import],
            new EpubPrivateQualificationEvidence(
                2, 1, 1, 0, 0, 1, 0, new string('C', 64), 1, 1, 1, 2, 2, 1,
                1, 0, 1, 0, 0, 0, 0, 0, 0, 0),
            diagnostics);
        return new EpubPrivateQualificationReport(true, [publication]);
    }
}
