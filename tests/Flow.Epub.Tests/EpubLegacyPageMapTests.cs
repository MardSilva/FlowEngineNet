using Flow.Documents;
using Flow.Epub.Corpus;
using Flow.Security;

namespace Flow.Epub.Tests;

public sealed class EpubLegacyPageMapTests
{
    private const string PackageWithPageMap = """
        <package xmlns="http://www.idpf.org/2007/opf" version="2.0" unique-identifier="book-id">
          <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
            <dc:identifier id="book-id">urn:flow:test:legacy-page-map</dc:identifier>
            <dc:title>Legacy page-map fixture</dc:title>
            <dc:language>en</dc:language>
          </metadata>
          <manifest>
            <item id="chapter" href="text/chapter-1.xhtml" media-type="application/xhtml+xml" />
            <item id="page-map" href="navigation/page-map.xml" media-type="application/oebps-page-map+xml" />
          </manifest>
          <spine page-map="page-map"><itemref idref="chapter" /></spine>
        </package>
        """;

    private const string PackageWithoutPageMap = """
        <package xmlns="http://www.idpf.org/2007/opf" version="2.0" unique-identifier="book-id">
          <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
            <dc:identifier id="book-id">urn:flow:test:legacy-page-map</dc:identifier>
            <dc:title>Legacy page-map fixture</dc:title>
            <dc:language>en</dc:language>
          </metadata>
          <manifest>
            <item id="chapter" href="text/chapter-1.xhtml" media-type="application/xhtml+xml" />
          </manifest>
          <spine><itemref idref="chapter" /></spine>
        </package>
        """;

    private const string Chapter = """
        <html xmlns="http://www.w3.org/1999/xhtml">
          <head><title>Chapter</title></head>
          <body><h1 id="start">Chapter</h1><p>Readable text.</p></body>
        </html>
        """;

    private const string PageMap = """
        <page-map xmlns="http://www.idpf.org/2007/opf">
          <page name="1" href="../text/chapter-1.xhtml#start" />
          <page name="2" href="../text/chapter-1.xhtml" />
        </page-map>
        """;

    [Fact]
    public async Task LegacyPageMap_IsRecognizedAsAnExplicitApproximation()
    {
        var bytes = CreateEpub(PackageWithPageMap, PageMap).ToArray();

        var inspection = await new EpubPublicationInspector().InspectAsync(new MemoryStream(bytes, writable: false));
        var import = await new EpubImporter().ImportAsync(new MemoryStream(bytes, writable: false));

        Assert.True(inspection.IsSuccess, string.Join(Environment.NewLine, inspection.Diagnostics));
        Assert.True(inspection.Manifest.Single(static item => item.Id == "page-map").IsSupported);
        Assert.Equal(0, inspection.Resources.UnsupportedManifestItemCount);
        Assert.Contains(inspection.Diagnostics, IsPageMapApproximation);
        Assert.DoesNotContain(inspection.Diagnostics, IsPageMapReportedAsUnsupported);

        Assert.True(import.IsSuccess, string.Join(Environment.NewLine, import.Diagnostics));
        Assert.Contains(import.Diagnostics, IsPageMapApproximation);
        Assert.DoesNotContain(import.Diagnostics, IsPageMapReportedAsUnsupported);
        Assert.NotNull(import.Document);
        Assert.Empty(import.Document.Index.Locations.Select(static item => item.Node).OfType<TableOfContents>());

        var fidelity = new EpubFidelityAnalyzer().Analyze(import);
        Assert.Equal(0, fidelity.Summary.UnsupportedCount);
        Assert.Equal(0, fidelity.Summary.LostCount);
        Assert.Contains(fidelity.Findings, static item =>
            item.RelatedDiagnosticCode == EpubDiagnosticCodes.LegacyPageMapNotImported
            && item.Status == EpubFidelityStatus.Approximated);
    }

    [Fact]
    public async Task LegacyPageMap_DoesNotChangeCanonicalContentOrHash()
    {
        var withPageMap = Assert.IsType<FlowDocument>((await new EpubImporter().ImportAsync(
            CreateEpub(PackageWithPageMap, PageMap))).Document);
        var withoutPageMap = Assert.IsType<FlowDocument>((await new EpubImporter().ImportAsync(
            CreateEpub(PackageWithoutPageMap))).Document);
        var integrity = new Sha256DocumentIntegrityService(new FlowDocumentCanonicalizer());

        Assert.Equal(integrity.ComputeHash(withoutPageMap), integrity.ComputeHash(withPageMap));
    }

    [Fact]
    public async Task LegacyPageMapContent_IsNotParsedOrExecuted()
    {
        const string untrustedPageMap = """
            <!DOCTYPE page-map [<!ENTITY local "expanded">]>
            <page-map xmlns="http://www.idpf.org/2007/opf">
              <page name="&local;" href="https://example.invalid/external" />
            </page-map>
            """;

        var result = await new EpubImporter().ImportAsync(CreateEpub(PackageWithPageMap, untrustedPageMap));

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Contains(result.Diagnostics, IsPageMapApproximation);
        Assert.DoesNotContain(result.Diagnostics, static item =>
            item.Message.Contains("expanded", StringComparison.Ordinal)
            || item.Message.Contains("example.invalid", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("application/oebps-page-map+xml", true)]
    [InlineData("APPLICATION/OEBPS-PAGE-MAP+XML", true)]
    [InlineData("application/x-vendor-page-map+xml", false)]
    [InlineData("application/oebps-page-map", false)]
    public void MediaTypeClassification_IsExactAndCaseInsensitive(string mediaType, bool expected) =>
        Assert.Equal(expected, EpubMediaTypeClassifier.IsLegacyPageMap(mediaType));

    [Fact]
    public async Task SimilarUnknownMediaType_RemainsUnsupported()
    {
        var package = PackageWithPageMap.Replace(
            EpubMediaTypeClassifier.LegacyPageMapMediaType,
            "application/x-vendor-page-map+xml",
            StringComparison.Ordinal);

        var result = await new EpubImporter().ImportAsync(CreateEpub(package, PageMap));

        Assert.Contains(result.Diagnostics, IsPageMapReportedAsUnsupported);
        Assert.DoesNotContain(result.Diagnostics, IsPageMapApproximation);
    }

    [Fact]
    public void DifferenceMatrix_ClassifiesPageMapAsReferenceApproximation()
    {
        var diagnostic = new EpubPrivateQualificationDiagnosticCount(
            EpubDiagnosticCodes.LegacyPageMapNotImported,
            EpubCorpusExecutionDiagnosticSeverity.Warning,
            EpubCorpusExecutionPhase.Import,
            1);
        var evidence = new EpubPrivateQualificationEvidence(
            2, 1, 1, 0, 0, 1, 0, new string('C', 64), 1, 1, 1, 2, 2, 1,
            1, 0, 1, 0, 0, 0, 0, 0, 0, 0);
        var publication = new EpubPrivateQualificationItem(
            new EpubCorpusPublicationId("page-map-fixture"),
            new EpubCorpusSha256(new string('B', 64)),
            EpubPrivateInventoryStatus.Ready,
            EpubPrivateQualificationStatus.Passed,
            eligible: true,
            stableAcrossRepeatedRuns: true,
            [EpubCorpusExecutionPhase.Import],
            evidence,
            [diagnostic]);
        var report = new EpubPrivateQualificationReport(true, [publication]);

        var matrix = new EpubPrivateDifferenceMatrixService().Create(
            report,
            new EpubCorpusSha256(new string('A', 64)));

        var difference = Assert.Single(Assert.Single(matrix.Candidates).Differences);
        Assert.Equal(EpubPrivateDifferenceCategory.ApprovedWithApproximations, difference.Category);
        Assert.Equal(EpubPrivateDifferenceCause.SourceApproximation, difference.Cause);
        Assert.Equal(EpubPrivateDifferenceMetric.References, difference.Metric);
    }

    private static MemoryStream CreateEpub(string package, string? pageMap = null) => MinimalEpubFactory.Create(
        package: package,
        chapterOne: Chapter,
        includeSecondChapter: false,
        includeImage: false,
        additionalTextEntries: pageMap is null
            ? null
            : new Dictionary<string, string> { ["EPUB/navigation/page-map.xml"] = pageMap });

    private static bool IsPageMapApproximation(EpubDiagnostic item) =>
        item.Code == EpubDiagnosticCodes.LegacyPageMapNotImported
        && item.Resource == "EPUB/navigation/page-map.xml";

    private static bool IsPageMapReportedAsUnsupported(EpubDiagnostic item) =>
        item.Code == EpubDiagnosticCodes.UnsupportedResource
        && item.Resource == "EPUB/navigation/page-map.xml";
}
