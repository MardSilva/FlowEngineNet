using Flow.Documents;

namespace Flow.Epub.Tests;

public sealed class EpubImporterTests
{
    [Fact]
    public async Task ImportAsync_ImportsMetadataSpineContentLinksListsAndImages()
    {
        await using var epub = MinimalEpubFactory.Create();

        var result = await new EpubImporter().ImportAsync(epub);

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics));
        var document = Assert.IsType<FlowDocument>(result.Document);
        Assert.Equal("EPUB mínimo do Flow", document.Metadata.Title);
        Assert.Equal("pt-PT", document.Metadata.Language);
        Assert.Equal("Flow contributors", Assert.Single(document.Metadata.Authors));
        Assert.Equal("urn:isbn:9780000000001", document.Identity.Id.Value);

        var chapters = document.Content.Children.Cast<Chapter>().ToArray();
        Assert.Equal(2, chapters.Length);
        Assert.Equal("Começo", InlineText(Assert.IsType<Heading>(chapters[0].Children[0]).Content));
        Assert.Equal("Fim", InlineText(Assert.IsType<Heading>(chapters[1].Children[0]).Content));
        Assert.Contains(chapters[0].Children, static node => node is UnorderedList);
        Assert.Contains(chapters[1].Children, static node => node is OrderedList { Start: 2 });

        var link = document.Index.Locations
            .Select(static location => location.Node)
            .OfType<Paragraph>()
            .SelectMany(static paragraph => paragraph.Content)
            .SelectMany(DescendantsAndSelf)
            .OfType<Link>()
            .Single();
        Assert.StartsWith("flow:", link.Target, StringComparison.Ordinal);
        Assert.True(document.TryResolveAnchor(Flow.Core.DocumentAnchor.Parse(link.Target), out var target));
        Assert.IsType<Heading>(target);

        var figure = document.Index.Locations.Select(static location => location.Node).OfType<Figure>().Single();
        Assert.Equal("Um pixel do Flow", figure.AlternativeText);
        Assert.Equal("Figura mínima", InlineText(figure.Caption!.Content));
        Assert.Single(document.Assets);
        Assert.Equal("image/png", document.Assets[figure.AssetId].MediaType);
    }

    [Fact]
    public async Task ImportAsync_NormalizesSkippedHeadingLevelsPerChapterDeterministically()
    {
        const string chapterOne = """
            <html xmlns="http://www.w3.org/1999/xhtml">
              <body>
                <h1 id="one">One</h1>
                <h3 id="two">Two</h3>
                <h3 id="three">Three</h3>
                <h4 id="four">Four</h4>
              </body>
            </html>
            """;
        const string chapterTwo = """
            <html xmlns="http://www.w3.org/1999/xhtml">
              <body><h3 id="independent">Independent chapter</h3></body>
            </html>
            """;
        await using var source = MinimalEpubFactory.Create(chapterOne: chapterOne, chapterTwo: chapterTwo);
        var bytes = source.ToArray();

        var first = await new EpubImporter().ImportAsync(new MemoryStream(bytes, writable: false));
        var second = await new EpubImporter().ImportAsync(new MemoryStream(bytes, writable: false));

        Assert.True(first.IsSuccess, string.Join(Environment.NewLine, first.Diagnostics));
        Assert.True(second.IsSuccess, string.Join(Environment.NewLine, second.Diagnostics));
        var firstDocument = Assert.IsType<FlowDocument>(first.Document);
        var secondDocument = Assert.IsType<FlowDocument>(second.Document);
        var firstChapters = firstDocument.Content.Children.Cast<Chapter>().ToArray();
        Assert.Equal([1, 2, 3, 4], firstChapters[0].Children.OfType<Heading>().Select(static heading => heading.Level));
        Assert.Equal([3], firstChapters[1].Children.OfType<Heading>().Select(static heading => heading.Level));
        Assert.Equal(
            firstDocument.Index.Locations.Select(static location => location.Node.Id),
            secondDocument.Index.Locations.Select(static location => location.Node.Id));
        Assert.Equal(first.Diagnostics.ToArray(), second.Diagnostics.ToArray());

        var normalization = Assert.Single(first.Diagnostics, static diagnostic =>
            diagnostic.Code == EpubDiagnosticCodes.HeadingLevelNormalized);
        Assert.Equal(EpubDiagnosticSeverity.Warning, normalization.Severity);
        Assert.Equal("EPUB/text/chapter-1.xhtml", normalization.Resource);
        Assert.DoesNotContain(
            first.Diagnostics,
            static diagnostic => diagnostic.Code == EpubDiagnosticCodes.DocumentValidation);

        var fidelity = new EpubFidelityAnalyzer().Analyze(first);
        Assert.Contains(fidelity.Findings, static finding =>
            finding.RelatedDiagnosticCode == EpubDiagnosticCodes.HeadingLevelNormalized
            && finding.Status == EpubFidelityStatus.Approximated);
        Assert.Equal(0, fidelity.Summary.LostCount);
    }

    private static IEnumerable<InlineNode> DescendantsAndSelf(InlineNode node)
    {
        yield return node;
        if (node is InlineContainerNode container)
        {
            foreach (var child in container.Children.SelectMany(DescendantsAndSelf))
            {
                yield return child;
            }
        }
    }

    private static string InlineText(IEnumerable<InlineNode> nodes) => string.Concat(nodes.Select(static node => node switch
    {
        Text text => text.Value,
        InlineContainerNode container => InlineText(container.Children),
        InlineCode code => code.Code,
        _ => string.Empty,
    }));

    [Fact]
    public async Task ImportAsync_RejectsDtdAndExternalEntities()
    {
        const string maliciousContainer = """
            <?xml version="1.0"?>
            <!DOCTYPE container [<!ENTITY xxe SYSTEM "file:///not-allowed">]>
            <container xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
              <rootfiles><rootfile full-path="&xxe;" /></rootfiles>
            </container>
            """;
        await using var epub = MinimalEpubFactory.Create(container: maliciousContainer);

        var result = await new EpubImporter().ImportAsync(epub);

        Assert.Null(result.Document);
        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Code == EpubDiagnosticCodes.InvalidXml
            && diagnostic.Severity == EpubDiagnosticSeverity.Error);
    }

    [Fact]
    public async Task ImportAsync_RejectsPackagePathThatEscapesArchiveRoot()
    {
        const string unsafeContainer = """
            <container xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
              <rootfiles><rootfile full-path="../package.opf" /></rootfiles>
            </container>
            """;
        await using var epub = MinimalEpubFactory.Create(container: unsafeContainer);

        var result = await new EpubImporter().ImportAsync(epub);

        Assert.Null(result.Document);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == EpubDiagnosticCodes.UnsafePath);
    }

    [Fact]
    public async Task ImportAsync_ReportsUnsupportedElementAndPreservesItsText()
    {
        const string chapter = """
            <html xmlns="http://www.w3.org/1999/xhtml">
              <body><h1 id="start">Título</h1><form><label>Texto importante</label></form></body>
            </html>
            """;
        await using var epub = MinimalEpubFactory.Create(chapterOne: chapter);

        var result = await new EpubImporter().ImportAsync(epub);

        var document = Assert.IsType<FlowDocument>(result.Document);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == EpubDiagnosticCodes.UnsupportedElement);
        Assert.Contains(
            document.Index.Locations.Select(static location => location.Node).OfType<Paragraph>(),
            static paragraph => Assert.IsType<Text>(paragraph.Content.Single()).Value == "Texto importante");
    }

    [Fact]
    public async Task ImportAsync_ReportsMissingSpineResourceExplicitly()
    {
        await using var epub = MinimalEpubFactory.Create(includeSecondChapter: false);

        var result = await new EpubImporter().ImportAsync(epub);

        Assert.NotNull(result.Document);
        Assert.False(result.IsSuccess);
        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Code == EpubDiagnosticCodes.MissingResource
            && diagnostic.Resource == "EPUB/text/chapter-2.xhtml");
    }

    [Fact]
    public async Task ImportAsync_EnforcesConfiguredArchiveLimit()
    {
        await using var epub = MinimalEpubFactory.Create();
        var limits = new EpubImportLimits(maximumEntries: 3);

        var result = await new EpubImporter(limits).ImportAsync(epub);

        Assert.Null(result.Document);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == EpubDiagnosticCodes.ArchiveLimitExceeded);
    }
}
