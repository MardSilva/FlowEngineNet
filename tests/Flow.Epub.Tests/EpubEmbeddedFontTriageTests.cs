using System.Xml.Linq;
using Flow.Documents;
using Flow.Epub.Corpus;
using Flow.Layout;
using Flow.Rendering.Html;
using Flow.Security;

namespace Flow.Epub.Tests;

public sealed class EpubEmbeddedFontTriageTests
{
    private const string Package = """
        <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="book-id">
          <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
            <dc:identifier id="book-id">urn:flow:test:embedded-font</dc:identifier>
            <dc:title>Embedded font fixture</dc:title>
            <dc:language>en</dc:language>
          </metadata>
          <manifest>
            <item id="chapter" href="text/chapter-1.xhtml" media-type="application/xhtml+xml" />
            <item id="styles" href="styles/book.css" media-type="text/css" />
            <item id="font" href="fonts/book.ttf" media-type="font/ttf" />
          </manifest>
          <spine><itemref idref="chapter" /></spine>
        </package>
        """;

    private const string Chapter = """
        <html xmlns="http://www.w3.org/1999/xhtml">
          <head><title>Font fixture</title><link rel="stylesheet" href="../styles/book.css" /></head>
          <body><p id="sample">Readable text</p></body>
        </html>
        """;

    private const string Css = """
        @font-face { font-family: "Fixture Serif"; src: url("../fonts/book.ttf"); }
        p { font-family: "Fixture Serif", serif; }
        """;

    [Fact]
    public async Task EmbeddedFont_IsAVisibleTypographyApproximationRatherThanUnsupportedContent()
    {
        var bytes = CreateEpub([0x00, 0x01, 0x02]).ToArray();

        var inspection = await new EpubPublicationInspector().InspectAsync(new MemoryStream(bytes, writable: false));
        var import = await new EpubImporter().ImportAsync(new MemoryStream(bytes, writable: false));

        Assert.True(inspection.IsSuccess, string.Join(Environment.NewLine, inspection.Diagnostics));
        Assert.True(inspection.Manifest.Single(static item => item.Id == "font").IsSupported);
        Assert.Equal(0, inspection.Resources.UnsupportedManifestItemCount);
        Assert.Contains(inspection.Diagnostics, IsEmbeddedFontApproximation);
        Assert.DoesNotContain(inspection.Diagnostics, IsFontReportedAsUnsupported);

        Assert.True(import.IsSuccess, string.Join(Environment.NewLine, import.Diagnostics));
        Assert.Contains(import.Diagnostics, IsEmbeddedFontApproximation);
        Assert.DoesNotContain(import.Diagnostics, IsFontReportedAsUnsupported);
        var document = Assert.IsType<FlowDocument>(import.Document);
        Assert.Empty(document.Assets);
        var paragraph = document.Index.Locations.Select(static item => item.Node).OfType<Paragraph>().Single();
        Assert.Equal("Fixture Serif", document.Presentation?.NodeTypography[paragraph.Id].FontFamily);

        var fidelity = new EpubFidelityAnalyzer().Analyze(import);
        Assert.Equal(0, fidelity.Summary.UnsupportedCount);
        Assert.Equal(0, fidelity.Summary.LostCount);
        Assert.Contains(fidelity.Findings, static item =>
            item.RelatedDiagnosticCode == EpubDiagnosticCodes.EmbeddedFontBytesNotPreserved
            && item.Status == EpubFidelityStatus.Approximated);

        var matrix = new EpubPrivateDifferenceMatrixService().Create(
            Qualification(import),
            new EpubCorpusSha256(new string('A', 64)));
        var difference = Assert.Single(Assert.Single(matrix.Candidates).Differences);
        Assert.Equal(EpubPrivateDifferenceCategory.ApprovedWithApproximations, difference.Category);
        Assert.Equal(EpubPrivateDifferenceCause.EmbeddedFontSubstitution, difference.Cause);
        Assert.Equal(EpubPrivateDifferenceMetric.Typography, difference.Metric);

        var preferences = new UserReadingPreferences();
        var layout = new AdaptiveLayoutEngine().Layout(
            document,
            new LayoutContext(390, 844, userPreferences: preferences));
        var html = new HtmlDocumentRenderer().RenderToString(document, layout, preferences);
        var renderedParagraph = XDocument.Parse(html).Descendants()
            .Single(static item => item.Name.LocalName == "p");
        Assert.Contains(
            "font-family: \"Fixture Serif\"",
            Assert.IsType<string>((string?)renderedParagraph.Attribute("style")),
            StringComparison.Ordinal);
        Assert.DoesNotContain("@font-face", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("book.ttf", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmbeddedFontBytes_DoNotChangeCanonicalContentHash()
    {
        var first = Assert.IsType<FlowDocument>((await new EpubImporter().ImportAsync(CreateEpub([0x01]))).Document);
        var second = Assert.IsType<FlowDocument>((await new EpubImporter().ImportAsync(CreateEpub([0x02, 0x03]))).Document);
        var integrity = new Sha256DocumentIntegrityService(new FlowDocumentCanonicalizer());

        Assert.Equal(integrity.ComputeHash(first), integrity.ComputeHash(second));
    }

    [Fact]
    public async Task LegacyTrueTypeMediaType_IsAFontApproximationRatherThanUnsupportedContent()
    {
        var package = Package.Replace(
            "media-type=\"font/ttf\"",
            "media-type=\"application/x-font-truetype\"",
            StringComparison.Ordinal);
        var bytes = CreateEpub([0x00, 0x01, 0x02], package).ToArray();

        var inspection = await new EpubPublicationInspector().InspectAsync(new MemoryStream(bytes, writable: false));
        var import = await new EpubImporter().ImportAsync(new MemoryStream(bytes, writable: false));

        var font = inspection.Manifest.Single(static item => item.Id == "font");
        Assert.True(font.IsSupported);
        Assert.Equal("application/x-font-truetype", font.MediaType);
        Assert.Equal(0, inspection.Resources.UnsupportedManifestItemCount);
        Assert.Contains(inspection.Diagnostics, IsEmbeddedFontApproximation);
        Assert.DoesNotContain(inspection.Diagnostics, IsFontReportedAsUnsupported);
        Assert.True(import.IsSuccess, string.Join(Environment.NewLine, import.Diagnostics));
        Assert.Contains(import.Diagnostics, IsEmbeddedFontApproximation);
        Assert.DoesNotContain(import.Diagnostics, IsFontReportedAsUnsupported);
        var fidelity = new EpubFidelityAnalyzer().Analyze(import);
        Assert.Equal(0, fidelity.Summary.UnsupportedCount);
        Assert.Contains(fidelity.Findings, static item =>
            item.RelatedDiagnosticCode == EpubDiagnosticCodes.EmbeddedFontBytesNotPreserved
            && item.Status == EpubFidelityStatus.Approximated);
    }

    [Theory]
    [InlineData("font/otf", true)]
    [InlineData("font/ttf", true)]
    [InlineData("font/woff", true)]
    [InlineData("font/woff2", true)]
    [InlineData("application/vnd.ms-opentype", true)]
    [InlineData("application/font-woff", true)]
    [InlineData("application/x-font-truetype", true)]
    [InlineData("APPLICATION/X-FONT-TRUETYPE", true)]
    [InlineData("application/x-font-unknown", false)]
    [InlineData("application/octet-stream", false)]
    public void EmbeddedFontMediaTypeClassification_IsExplicitAndCaseInsensitive(
        string mediaType,
        bool expected) => Assert.Equal(expected, EpubMediaTypeClassifier.IsEmbeddedFont(mediaType));

    [Fact]
    public async Task UnknownBinaryResource_RemainsUnsupported()
    {
        var package = Package.Replace(
            "<item id=\"font\" href=\"fonts/book.ttf\" media-type=\"font/ttf\" />",
            "<item id=\"binary\" href=\"resources/data.bin\" media-type=\"application/octet-stream\" />",
            StringComparison.Ordinal);
        await using var epub = MinimalEpubFactory.Create(
            package: package,
            chapterOne: Chapter,
            includeSecondChapter: false,
            includeImage: false,
            additionalTextEntries: new Dictionary<string, string> { ["EPUB/styles/book.css"] = Css },
            additionalBinaryEntries: new Dictionary<string, byte[]> { ["EPUB/resources/data.bin"] = [0x01] });

        var result = await new EpubImporter().ImportAsync(epub);

        Assert.Contains(result.Diagnostics, static item =>
            item.Code == EpubDiagnosticCodes.UnsupportedResource
            && item.Resource == "EPUB/resources/data.bin");
        Assert.DoesNotContain(result.Diagnostics, IsEmbeddedFontApproximation);
    }

    private static MemoryStream CreateEpub(byte[] fontBytes, string package = Package) => MinimalEpubFactory.Create(
        package: package,
        chapterOne: Chapter,
        includeSecondChapter: false,
        includeImage: false,
        additionalTextEntries: new Dictionary<string, string> { ["EPUB/styles/book.css"] = Css },
        additionalBinaryEntries: new Dictionary<string, byte[]> { ["EPUB/fonts/book.ttf"] = fontBytes });

    private static EpubPrivateQualificationReport Qualification(EpubImportResult import)
    {
        var diagnostics = import.Diagnostics
            .Where(static item => item.Code == EpubDiagnosticCodes.EmbeddedFontBytesNotPreserved)
            .Select(static item => new EpubPrivateQualificationDiagnosticCount(
                item.Code,
                EpubCorpusExecutionDiagnosticSeverity.Warning,
                EpubCorpusExecutionPhase.Import,
                item.Count));
        var item = new EpubPrivateQualificationItem(
            new EpubCorpusPublicationId("font-fixture"),
            new EpubCorpusSha256(new string('B', 64)),
            EpubPrivateInventoryStatus.Ready,
            EpubPrivateQualificationStatus.Passed,
            eligible: true,
            stableAcrossRepeatedRuns: true,
            [EpubCorpusExecutionPhase.Import],
            new EpubPrivateQualificationEvidence(
                3, 1, 1, 0, 0, 1, 0, new string('C', 64), 1, 1, 1, 2, 2, 1,
                1, 0, 1, 0, 0, 0, 0, 0, 0, 0),
            diagnostics);
        return new EpubPrivateQualificationReport(true, [item]);
    }

    private static bool IsEmbeddedFontApproximation(EpubDiagnostic item) =>
        item.Code == EpubDiagnosticCodes.EmbeddedFontBytesNotPreserved
        && item.Resource == "EPUB/fonts/book.ttf";

    private static bool IsFontReportedAsUnsupported(EpubDiagnostic item) =>
        item.Code == EpubDiagnosticCodes.UnsupportedResource
        && item.Resource == "EPUB/fonts/book.ttf";
}
