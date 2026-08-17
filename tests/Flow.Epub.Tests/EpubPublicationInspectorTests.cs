using Flow.Epub;

namespace Flow.Epub.Tests;

public sealed class EpubPublicationInspectorTests
{
    [Fact]
    public async Task InspectAsync_ReportsEpub3PackageManifestSpineNavigationAndSizes()
    {
        const string package = """
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="book-id">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="book-id">urn:isbn:9780000000001</dc:identifier>
                <dc:title>EPUB 3 inspected</dc:title>
                <dc:language>en</dc:language>
                <dc:creator>Flow author</dc:creator>
                <dc:publisher>Flow Press</dc:publisher>
                <meta property="dcterms:modified">2026-08-17T00:00:00Z</meta>
              </metadata>
              <manifest>
                <item id="chapter-one" href="text/chapter-1.xhtml" media-type="application/xhtml+xml" />
                <item id="chapter-two" href="text/chapter-2.xhtml" media-type="application/xhtml+xml" />
                <item id="navigation" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav" />
                <item id="cover-image" href="images/flow.png" media-type="image/png" properties="cover-image" />
                <item id="styles" href="styles/book.css" media-type="text/css" />
              </manifest>
              <spine>
                <itemref idref="chapter-one" />
                <itemref idref="chapter-two" linear="no" />
              </spine>
            </package>
            """;
        await using var epub = MinimalEpubFactory.Create(
            package: package,
            additionalTextEntries: new Dictionary<string, string>
            {
                ["EPUB/nav.xhtml"] = "<html xmlns=\"http://www.w3.org/1999/xhtml\"><body /></html>",
                ["EPUB/styles/book.css"] = "body { color: black; }",
            });

        var inspection = await new EpubPublicationInspector().InspectAsync(epub);

        Assert.True(inspection.IsSuccess, string.Join(Environment.NewLine, inspection.Diagnostics));
        var packageInfo = Assert.IsType<EpubPackageInfo>(inspection.Package);
        Assert.Equal(EpubVersionFamily.Epub3, packageInfo.VersionFamily);
        Assert.Equal("3.0", packageInfo.DeclaredVersion);
        Assert.Equal("EPUB 3 inspected", packageInfo.Title);
        Assert.Equal("urn:isbn:9780000000001", packageInfo.Identifier);
        Assert.Equal("Flow author", Assert.Single(packageInfo.Creators));
        Assert.Equal("Flow Press", packageInfo.Publisher);
        Assert.Equal("2026-08-17T00:00:00Z", packageInfo.Modified);
        Assert.Equal(5, inspection.Manifest.Length);
        Assert.Equal(2, inspection.Spine.Length);
        Assert.True(inspection.Spine[0].IsLinear);
        Assert.False(inspection.Spine[1].IsLinear);
        Assert.Equal("EPUB/nav.xhtml", Assert.Single(inspection.NavigationDocumentPaths));
        Assert.Equal(5, inspection.Resources.ManifestItemCount);
        Assert.Equal(5, inspection.Resources.ExistingManifestItemCount);
        Assert.Equal(1, inspection.Resources.UnsupportedManifestItemCount);
        Assert.Equal(1, inspection.Resources.NavigationDocumentCount);
        Assert.True(inspection.Resources.TotalCompressedBytes > 0);
        Assert.True(inspection.Resources.TotalUncompressedBytes > 0);
        Assert.Equal(3, inspection.Resources.MediaTypeCounts["application/xhtml+xml"]);
        Assert.Contains(inspection.Diagnostics, static diagnostic =>
            diagnostic.Code == EpubDiagnosticCodes.UnsupportedResource
            && diagnostic.Resource == "EPUB/styles/book.css");
    }

    [Fact]
    public async Task InspectAsync_IdentifiesEpub2AndNcxNavigation()
    {
        const string package = """
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="2.0" unique-identifier="book-id">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="book-id">urn:flow:test:epub2</dc:identifier>
                <dc:title>EPUB 2 inspected</dc:title>
              </metadata>
              <manifest>
                <item id="chapter-one" href="text/chapter-1.xhtml" media-type="application/xhtml+xml" />
                <item id="ncx" href="toc.ncx" media-type="application/x-dtbncx+xml" />
              </manifest>
              <spine toc="ncx"><itemref idref="chapter-one" /></spine>
            </package>
            """;
        await using var epub = MinimalEpubFactory.Create(
            package: package,
            includeSecondChapter: false,
            includeImage: false,
            additionalTextEntries: new Dictionary<string, string>
            {
                ["EPUB/toc.ncx"] = "<ncx xmlns=\"http://www.daisy.org/z3986/2005/ncx/\" />",
            });

        var inspection = await new EpubPublicationInspector().InspectAsync(epub);

        Assert.True(inspection.IsSuccess, string.Join(Environment.NewLine, inspection.Diagnostics));
        Assert.Equal(EpubVersionFamily.Epub2, inspection.Package?.VersionFamily);
        Assert.Equal("EPUB/toc.ncx", Assert.Single(inspection.NavigationDocumentPaths));
        Assert.True(inspection.Manifest.Single(static item => item.Id == "ncx").IsNavigationDocument);
    }

    [Fact]
    public async Task InspectAsync_InvalidZipProducesExplicitDiagnostic()
    {
        await using var source = new MemoryStream("not a zip"u8.ToArray());

        var inspection = await new EpubPublicationInspector().InspectAsync(source);

        Assert.False(inspection.IsSuccess);
        Assert.Null(inspection.Package);
        Assert.Contains(inspection.Diagnostics, static diagnostic =>
            diagnostic.Code == EpubDiagnosticCodes.InvalidArchive
            && diagnostic.Severity == EpubDiagnosticSeverity.Error);
    }

    [Fact]
    public async Task InspectAsync_UnsafeArchivePathStopsInspection()
    {
        await using var epub = MinimalEpubFactory.Create(
            additionalTextEntries: new Dictionary<string, string> { ["../escape.txt"] = "unsafe" });

        var inspection = await new EpubPublicationInspector().InspectAsync(epub);

        Assert.False(inspection.IsSuccess);
        Assert.Null(inspection.Package);
        Assert.Contains(inspection.Diagnostics, static diagnostic =>
            diagnostic.Code == EpubDiagnosticCodes.UnsafePath
            && diagnostic.Resource == "../escape.txt");
    }

    [Fact]
    public async Task InspectAsync_MissingManifestResourceIsRetainedAndDiagnosed()
    {
        await using var epub = MinimalEpubFactory.Create(includeSecondChapter: false);

        var inspection = await new EpubPublicationInspector().InspectAsync(epub);

        Assert.False(inspection.IsSuccess);
        Assert.NotNull(inspection.Package);
        var missing = inspection.Manifest.Single(static item => item.Id == "chapter-two");
        Assert.False(missing.ExistsInArchive);
        Assert.Equal(1, inspection.Resources.MissingManifestItemCount);
        Assert.Contains(inspection.Diagnostics, static diagnostic =>
            diagnostic.Code == EpubDiagnosticCodes.MissingResource
            && diagnostic.Resource == "EPUB/text/chapter-2.xhtml");
    }

    [Fact]
    public async Task InspectAsync_EnforcesTheSameConfiguredArchiveLimitsAsImporter()
    {
        await using var epub = MinimalEpubFactory.Create();
        var inspector = new EpubPublicationInspector(new EpubImportLimits(maximumEntries: 3));

        var inspection = await inspector.InspectAsync(epub);

        Assert.False(inspection.IsSuccess);
        Assert.Null(inspection.Package);
        Assert.Contains(inspection.Diagnostics, static diagnostic =>
            diagnostic.Code == EpubDiagnosticCodes.ArchiveLimitExceeded);
    }
}
