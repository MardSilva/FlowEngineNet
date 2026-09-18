using Flow.Documents;
using Flow.Security;

namespace Flow.Epub.Tests;

public sealed class EpubFidelityAnalyzerTests
{
    [Fact]
    public async Task Analyze_PreCancelledTokenIsDistinctFromAConversionFinding()
    {
        await using var epub = MinimalEpubFactory.Create();
        var import = await new EpubImporter().ImportAsync(epub);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => new EpubFidelityAnalyzer().Analyze(import, cancellation.Token));
    }

    [Fact]
    public async Task Analyze_MeasuresRichFixtureAndDoesNotChangeCanonicalHash()
    {
        await using var epub = MinimalEpubFactory.Create();
        var import = await new EpubImporter().ImportAsync(epub);
        var document = Assert.IsType<FlowDocument>(import.Document);
        var integrity = new Sha256DocumentIntegrityService(new FlowDocumentCanonicalizer());
        var before = integrity.ComputeHash(document);

        var report = new EpubFidelityAnalyzer().Analyze(import);

        Assert.True(import.IsSuccess);
        Assert.False(report.Summary.IsPartial);
        Assert.Equal(before, integrity.ComputeHash(document));
        Assert.Equal(2, Measurement(report, EpubFidelityMetric.LinearSpineItems).SourceCount);
        Assert.Equal(2, Measurement(report, EpubFidelityMetric.LinearSpineItems).DestinationCount);
        Assert.Equal(2, Measurement(report, EpubFidelityMetric.Headings).PreservedCount);
        Assert.Equal(1, Measurement(report, EpubFidelityMetric.InternalLinks).TransformedCount);
        Assert.Equal(1, Measurement(report, EpubFidelityMetric.Images).TransformedCount);
        Assert.Null(Measurement(report, EpubFidelityMetric.Notes).PreservationPercentage);
    }

    [Fact]
    public async Task Analyze_ClassifiesApproximatedUnknownElementAndLocatesItsResource()
    {
        const string chapter = """
            <html xmlns="http://www.w3.org/1999/xhtml"><head><title>x</title></head><body>
              <h1 id="start">Título</h1><p>Antes <custom-box>texto recuperado</custom-box> depois.</p>
            </body></html>
            """;
        await using var epub = MinimalEpubFactory.Create(chapterOne: chapter);

        var report = new EpubFidelityAnalyzer().Analyze(await new EpubImporter().ImportAsync(epub));

        var unknown = Measurement(report, EpubFidelityMetric.UnknownOrUnrepresentableElements);
        Assert.Equal(1, unknown.SourceCount);
        Assert.Equal(1, unknown.ApproximatedCount);
        Assert.Contains(report.Findings, finding =>
            finding.Status == EpubFidelityStatus.Approximated
            && finding.ResourcePath == "EPUB/text/chapter-1.xhtml"
            && finding.Count == 1);
    }

    [Fact]
    public async Task Analyze_SeparatesTransparentContainerTransformationsFromSemanticApproximations()
    {
        const string chapter = """
            <html xmlns="http://www.w3.org/1999/xhtml"><head><title>x</title></head><body>
              <div><p>Antes <span>texto preservado</span>.</p><div><p>Depois.</p></div></div>
              <p><span><img src="../images/flow.png" alt="Imagem preservada" /></span></p>
              <aside><p>Conteúdo lateral.</p></aside>
              <custom-box><p>Conteúdo desconhecido.</p></custom-box>
            </body></html>
            """;
        await using var epub = MinimalEpubFactory.Create(chapterOne: chapter);
        var import = await new EpubImporter().ImportAsync(epub);
        var document = Assert.IsType<FlowDocument>(import.Document);
        var integrity = new Sha256DocumentIntegrityService(new FlowDocumentCanonicalizer());
        var before = integrity.ComputeHash(document);

        var report = new EpubFidelityAnalyzer().Analyze(import);

        var unknown = Measurement(report, EpubFidelityMetric.UnknownOrUnrepresentableElements);
        Assert.Equal(6, unknown.SourceCount);
        Assert.Equal(4, unknown.TransformedCount);
        Assert.Equal(2, unknown.ApproximatedCount);
        Assert.Equal(0, unknown.LostCount);
        Assert.Equal(before, integrity.ComputeHash(document));
        Assert.Equal(4, import.Diagnostics
            .Where(static item => item.Code == EpubDiagnosticCodes.TransparentContainerTransformed)
            .Sum(static item => item.Count));
        Assert.All(
            report.Findings.Where(static item =>
                item.RelatedDiagnosticCode == EpubDiagnosticCodes.TransparentContainerTransformed),
            static finding =>
            {
                Assert.Equal(EpubFidelityStatus.Transformed, finding.Status);
                Assert.Equal(EpubFidelityImpact.Informational, finding.Impact);
            });
        Assert.Contains(report.Findings, static finding =>
            finding.RelatedDiagnosticCode == EpubDiagnosticCodes.UnsupportedElement
            && finding.Status == EpubFidelityStatus.Approximated);
    }

    [Fact]
    public async Task Analyze_ReportsDeduplicatedImagesAsManyToOneTransformation()
    {
        const string chapter = """
            <html xmlns="http://www.w3.org/1999/xhtml"><head><title>x</title></head><body>
              <h1 id="start">Título</h1>
              <figure id="one"><img src="../images/flow.png" alt="um" /></figure>
              <figure id="two"><img src="../images/flow.png" alt="dois" /></figure>
            </body></html>
            """;
        await using var epub = MinimalEpubFactory.Create(chapterOne: chapter);
        var import = await new EpubImporter().ImportAsync(epub);

        var report = new EpubFidelityAnalyzer().Analyze(import);

        Assert.Single(Assert.IsType<FlowDocument>(import.Document).Assets);
        var images = Measurement(report, EpubFidelityMetric.Images);
        Assert.Equal(2, images.SourceCount);
        Assert.Equal(2, images.DestinationCount);
        Assert.Equal(2, images.TransformedCount);
    }

    [Fact]
    public async Task Analyze_ClassifiesUnrepresentedTableColumnMetadataAsMinorApproximation()
    {
        const string chapter = """
            <html xmlns="http://www.w3.org/1999/xhtml"><body>
              <table><colgroup><col /><col /></colgroup><tbody><tr><td>A</td><td>B</td></tr></tbody></table>
            </body></html>
            """;
        await using var epub = MinimalEpubFactory.Create(chapterOne: chapter);

        var report = new EpubFidelityAnalyzer().Analyze(await new EpubImporter().ImportAsync(epub));

        var finding = Assert.Single(report.Findings, static item =>
            item.RelatedDiagnosticCode == EpubDiagnosticCodes.TableColumnMetadataNotRepresented);
        Assert.Equal(EpubFidelityStatus.Approximated, finding.Status);
        Assert.Equal(EpubFidelityImpact.Minor, finding.Impact);
        Assert.Equal(1, finding.Count);
    }

    [Fact]
    public async Task Analyze_FailedImportIsPartialAndNeverInventsOneHundredPercent()
    {
        await using var invalid = new MemoryStream("not a zip"u8.ToArray());

        var report = new EpubFidelityAnalyzer().Analyze(await new EpubImporter().ImportAsync(invalid));

        Assert.False(report.ImportSucceeded);
        Assert.True(report.Summary.IsPartial);
        Assert.Null(report.Summary.PreservationPercentage);
        Assert.Contains(report.Findings, static finding =>
            finding.Status == EpubFidelityStatus.Lost
            && finding.RelatedDiagnosticCode == EpubDiagnosticCodes.InvalidArchive);
    }

    [Fact]
    public void Finding_RejectsInvalidAggregationCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EpubFidelityFinding(
            "FID-TEST",
            EpubFidelityStatus.Preserved,
            EpubFidelityImpact.Informational,
            "test",
            count: 0));
    }

    [Fact]
    public void Analyze_ClassifiesUnsupportedDiagnosticWithoutClaimingMeasuredCoverage()
    {
        var import = new EpubImportResult(
            null,
            [
                new EpubDiagnostic(
                    EpubDiagnosticCodes.UnsupportedResource,
                    EpubDiagnosticSeverity.Warning,
                    "Audio is not representable.",
                    "EPUB/audio/chapter.mp3"),
            ]);

        var report = new EpubFidelityAnalyzer().Analyze(import);

        var finding = Assert.Single(report.Findings);
        Assert.Equal(EpubFidelityStatus.Unsupported, finding.Status);
        Assert.Equal(EpubFidelityImpact.Moderate, finding.Impact);
        Assert.Null(report.Summary.PreservationPercentage);
    }

    [Fact]
    public async Task Analyze_DoesNotDoubleCountNoteReferencesAsOrdinaryInternalLinks()
    {
        const string package = """
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="id">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="id">urn:flow:fidelity:notes</dc:identifier>
                <dc:title>Notas</dc:title><dc:language>pt</dc:language>
              </metadata>
              <manifest><item id="chapter" href="text/chapter-1.xhtml" media-type="application/xhtml+xml" /></manifest>
              <spine><itemref idref="chapter" /></spine>
            </package>
            """;
        const string chapter = """
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
              <head><title>Notas</title></head><body>
                <p>Texto<a id="call" epub:type="noteref" href="#note">1</a>.</p>
                <aside id="note" epub:type="footnote"><p>Nota <a epub:type="backlink" href="#call">voltar</a>.</p></aside>
              </body>
            </html>
            """;
        await using var epub = MinimalEpubFactory.Create(
            package: package,
            chapterOne: chapter,
            includeSecondChapter: false,
            includeImage: false);

        var report = new EpubFidelityAnalyzer().Analyze(await new EpubImporter().ImportAsync(epub));

        Assert.Equal(1, Measurement(report, EpubFidelityMetric.NoteReferences).SourceCount);
        Assert.Equal(1, Measurement(report, EpubFidelityMetric.NoteReferences).DestinationCount);
        Assert.Equal(1, Measurement(report, EpubFidelityMetric.InternalLinks).SourceCount);
        Assert.Equal(1, Measurement(report, EpubFidelityMetric.InternalLinks).DestinationCount);
    }

    private static EpubFidelityMeasurement Measurement(
        EpubFidelityReport report,
        EpubFidelityMetric metric) => report.Measurements.Single(item => item.Metric == metric);
}
