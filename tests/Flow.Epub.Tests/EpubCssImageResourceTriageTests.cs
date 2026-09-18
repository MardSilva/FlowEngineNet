using Flow.Documents;
using Flow.Epub.Corpus;
using Flow.Security;

namespace Flow.Epub.Tests;

public sealed class EpubCssImageResourceTriageTests
{
    private const string Package = """
        <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="book-id">
          <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
            <dc:identifier id="book-id">urn:flow:test:css-image</dc:identifier>
            <dc:title>CSS image fixture</dc:title>
            <dc:language>en</dc:language>
          </metadata>
          <manifest>
            <item id="chapter" href="text/chapter-1.xhtml" media-type="application/xhtml+xml" />
            <item id="styles" href="styles/book.css" media-type="text/css" />
            <item id="background" href="images/background.png" media-type="image/png" />
          </manifest>
          <spine><itemref idref="chapter" /></spine>
        </package>
        """;

    private const string PackageWithoutImage = """
        <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="book-id">
          <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
            <dc:identifier id="book-id">urn:flow:test:css-image</dc:identifier>
            <dc:title>CSS image fixture</dc:title>
            <dc:language>en</dc:language>
          </metadata>
          <manifest>
            <item id="chapter" href="text/chapter-1.xhtml" media-type="application/xhtml+xml" />
            <item id="styles" href="styles/book.css" media-type="text/css" />
          </manifest>
          <spine><itemref idref="chapter" /></spine>
        </package>
        """;

    private const string Chapter = """
        <html xmlns="http://www.w3.org/1999/xhtml">
          <head><title>Chapter</title><link rel="stylesheet" href="../styles/book.css" /></head>
          <body><p id="text">Readable text.</p></body>
        </html>
        """;

    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    [Theory]
    [InlineData("url(../images/background.png)")]
    [InlineData("url('../images/./background.png')")]
    [InlineData("url(\"../images/back%67round.png\")")]
    public async Task LocalCssImage_IsClassifiedAsAnExplicitApproximation(string reference)
    {
        var import = await new EpubImporter().ImportAsync(CreateEpub(
            Package,
            $"body {{ background: {reference} no-repeat center; }}",
            includeImage: true));

        Assert.True(import.IsSuccess, string.Join(Environment.NewLine, import.Diagnostics));
        Assert.Contains(import.Diagnostics, IsCssImageApproximation);
        Assert.DoesNotContain(import.Diagnostics, IsImageReportedAsUnsupported);
        Assert.Empty(Assert.IsType<FlowDocument>(import.Document).Assets);

        var fidelity = new EpubFidelityAnalyzer().Analyze(import);
        Assert.Equal(0, fidelity.Summary.UnsupportedCount);
        Assert.Equal(0, fidelity.Summary.LostCount);
        Assert.Contains(fidelity.Findings, static item =>
            item.RelatedDiagnosticCode == EpubDiagnosticCodes.CssImageResourceNotPreserved
            && item.Status == EpubFidelityStatus.Approximated);

        var matrix = new EpubPrivateDifferenceMatrixService().Create(
            Qualification(import),
            new EpubCorpusSha256(new string('A', 64)));
        var difference = Assert.Single(Assert.Single(matrix.Candidates).Differences);
        Assert.Equal(EpubPrivateDifferenceCategory.ApprovedWithApproximations, difference.Category);
        Assert.Equal(EpubPrivateDifferenceCause.SourceApproximation, difference.Cause);
        Assert.Equal(EpubPrivateDifferenceMetric.Assets, difference.Metric);
    }

    [Fact]
    public async Task RepeatedCssImageReferences_AreAggregatedDeterministically()
    {
        const string css = "body { background: url(../images/background.png); } section { background: url('../images/background.png'); }";

        var import = await new EpubImporter().ImportAsync(CreateEpub(Package, css, includeImage: true));

        var diagnostics = import.Diagnostics
            .Where(static item => item.Code == EpubDiagnosticCodes.CssImageResourceNotPreserved)
            .ToArray();
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("EPUB/images/background.png", diagnostic.Resource);
        Assert.Contains("2 occurrences", diagnostic.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(import.Diagnostics, IsImageReportedAsUnsupported);
    }

    [Fact]
    public async Task CssImageBytesAndPresentation_DoNotChangeCanonicalHash()
    {
        var withImage = Assert.IsType<FlowDocument>((await new EpubImporter().ImportAsync(CreateEpub(
            Package,
            "body { background: url(../images/background.png); }",
            includeImage: true))).Document);
        var withoutImage = Assert.IsType<FlowDocument>((await new EpubImporter().ImportAsync(CreateEpub(
            PackageWithoutImage,
            string.Empty,
            includeImage: false))).Document);
        var integrity = new Sha256DocumentIntegrityService(new FlowDocumentCanonicalizer());

        Assert.Equal(integrity.ComputeHash(withoutImage), integrity.ComputeHash(withImage));
    }

    [Fact]
    public async Task UnsafeOrExternalCssReferences_AreNotAssociatedWithLocalManifestImage()
    {
        const string css = "body { background: url(https://example.invalid/image.png); } p { background-image: url(../../../images/background.png); }";

        var import = await new EpubImporter().ImportAsync(CreateEpub(Package, css, includeImage: true));

        Assert.DoesNotContain(import.Diagnostics, IsCssImageApproximation);
        Assert.Contains(import.Diagnostics, IsImageReportedAsUnsupported);
    }

    private static MemoryStream CreateEpub(string package, string css, bool includeImage)
    {
        var binaries = includeImage
            ? new Dictionary<string, byte[]> { ["EPUB/images/background.png"] = OnePixelPng }
            : null;
        return MinimalEpubFactory.Create(
            package: package,
            chapterOne: Chapter,
            includeSecondChapter: false,
            includeImage: false,
            additionalTextEntries: new Dictionary<string, string> { ["EPUB/styles/book.css"] = css },
            additionalBinaryEntries: binaries);
    }

    private static EpubPrivateQualificationReport Qualification(EpubImportResult import)
    {
        var diagnostics = import.Diagnostics
            .Where(IsCssImageApproximation)
            .Select(static item => new EpubPrivateQualificationDiagnosticCount(
                item.Code,
                EpubCorpusExecutionDiagnosticSeverity.Warning,
                EpubCorpusExecutionPhase.Import,
                item.Count));
        var item = new EpubPrivateQualificationItem(
            new EpubCorpusPublicationId("css-image-fixture"),
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

    private static bool IsCssImageApproximation(EpubDiagnostic item) =>
        item.Code == EpubDiagnosticCodes.CssImageResourceNotPreserved
        && item.Resource == "EPUB/images/background.png";

    private static bool IsImageReportedAsUnsupported(EpubDiagnostic item) =>
        item.Code == EpubDiagnosticCodes.UnsupportedResource
        && item.Resource == "EPUB/images/background.png";
}
