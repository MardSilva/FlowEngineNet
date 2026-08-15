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
        Assert.Equal("Começo", Assert.IsType<Text>(Assert.IsType<Heading>(chapters[0].Children[0]).Content[0]).Value);
        Assert.Equal("Fim", Assert.IsType<Text>(Assert.IsType<Heading>(chapters[1].Children[0]).Content[0]).Value);
        Assert.Contains(chapters[0].Children, static node => node is UnorderedList);
        Assert.Contains(chapters[1].Children, static node => node is OrderedList { Start: 2 });

        var link = document.Index.Locations
            .Select(static location => location.Node)
            .OfType<Paragraph>()
            .SelectMany(static paragraph => paragraph.Content)
            .OfType<Link>()
            .Single();
        Assert.StartsWith("flow:", link.Target, StringComparison.Ordinal);
        Assert.True(document.TryResolveAnchor(Flow.Core.DocumentAnchor.Parse(link.Target), out var target));
        Assert.IsType<Heading>(target);

        var figure = document.Index.Locations.Select(static location => location.Node).OfType<Figure>().Single();
        Assert.Equal("Um pixel do Flow", figure.AlternativeText);
        Assert.Equal("Figura mínima", Assert.IsType<Text>(figure.Caption!.Content[0]).Value);
        Assert.Single(document.Assets);
        Assert.Equal("image/png", document.Assets[figure.AssetId].MediaType);
    }

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
              <body><h1 id="start">Título</h1><table><tr><td>Texto importante</td></tr></table></body>
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
