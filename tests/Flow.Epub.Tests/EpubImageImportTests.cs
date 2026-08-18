using Flow.Documents;
using Flow.Layout;
using Flow.Rendering.Html;

namespace Flow.Epub.Tests;

public sealed class EpubImageImportTests
{
    private const string PackagePrefix = """
        <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="book-id">
          <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
            <dc:identifier id="book-id">urn:flow:test:images</dc:identifier>
            <dc:title>Image test</dc:title>
          </metadata>
          <manifest>
            <item id="chapter" href="text/chapter-1.xhtml" media-type="application/xhtml+xml" />
        """;

    private const string PackageSuffix = """
          </manifest>
          <spine><itemref idref="chapter" /></spine>
        </package>
        """;

    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    private static readonly byte[] Gif = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==");

    private static readonly byte[] Jpeg =
    [
        0xff, 0xd8, 0xff, 0xc0, 0x00, 0x11, 0x08, 0x00, 0x01, 0x00, 0x01,
        0x03, 0x01, 0x11, 0x00, 0x02, 0x11, 0x00, 0x03, 0x11, 0x00, 0xff, 0xd9,
    ];

    private static readonly byte[] WebP =
    [
        (byte)'R', (byte)'I', (byte)'F', (byte)'F', 22, 0, 0, 0,
        (byte)'W', (byte)'E', (byte)'B', (byte)'P',
        (byte)'V', (byte)'P', (byte)'8', (byte)'X', 10, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    ];

    [Theory]
    [InlineData("image/jpeg", "image/jpeg", "cover.jpg")]
    [InlineData("image/png", "image/png", "cover.png")]
    [InlineData("image/gif", "image/gif", "cover.gif")]
    [InlineData("image/webp", "image/webp", "cover.webp")]
    public async Task ImportAsync_LoadsSupportedCoverFormats(
        string declaredMediaType,
        string expectedMediaType,
        string fileName)
    {
        var bytes = expectedMediaType switch
        {
            "image/jpeg" => Jpeg,
            "image/png" => Png,
            "image/gif" => Gif,
            "image/webp" => WebP,
            _ => throw new InvalidOperationException(),
        };
        var package = Package($"<item id=\"cover\" href=\"images/{fileName}\" media-type=\"{declaredMediaType}\" properties=\"cover-image\" />");

        var result = await ImportAsync(
            package,
            "<h1>Book</h1>",
            new Dictionary<string, byte[]> { [$"EPUB/images/{fileName}"] = bytes });

        var document = Assert.IsType<FlowDocument>(result.Document);
        var asset = Assert.Single(document.Assets).Value;
        Assert.Equal(expectedMediaType, asset.MediaType);
        Assert.Equal(asset.Id, Assert.IsType<EpubCoverMetadata>(result.MetadataReport!.Cover).AssetId);
    }

    [Fact]
    public async Task ImportAsync_ImportsPictureAndDeduplicatesRepeatedBytes()
    {
        var package = Package("""
            <item id="webp" href="images/preferred.webp" media-type="image/webp" />
            <item id="png-one" href="images/one.png" media-type="image/png" />
            <item id="png-two" href="images/two.png" media-type="image/png" />
            """);
        const string body = """
            <picture id="hero"><source type="image/webp" srcset="../images/preferred.webp"/><img src="../images/one.png" alt="Hero"/></picture>
            <figure id="one"><img src="../images/one.png" alt="Repeated"/><figcaption>One</figcaption></figure>
            <figure id="two"><img src="../images/two.png" alt="Repeated again"/><figcaption>Two</figcaption></figure>
            """;

        var result = await ImportAsync(
            package,
            body,
            new Dictionary<string, byte[]>
            {
                ["EPUB/images/preferred.webp"] = WebP,
                ["EPUB/images/one.png"] = Png,
                ["EPUB/images/two.png"] = Png,
            });

        var document = Assert.IsType<FlowDocument>(result.Document);
        var figures = document.Index.Locations.Select(static location => location.Node).OfType<Figure>().ToArray();
        Assert.Equal(3, figures.Length);
        Assert.Equal("image/webp", document.Assets[figures[0].AssetId].MediaType);
        Assert.Equal(figures[1].AssetId, figures[2].AssetId);
        Assert.Equal(2, document.Assets.Count);
        Assert.Contains(result.Diagnostics, static item => item.Code == EpubDiagnosticCodes.ImageDeduplicated);

        var preferences = new UserReadingPreferences();
        var layout = new AdaptiveLayoutEngine().Layout(
            document,
            new LayoutContext(390, 844, DeviceClass.Phone, userPreferences: preferences));
        var html = new HtmlDocumentRenderer().RenderToString(document, layout, preferences);
        Assert.Contains("data:image/webp;base64,", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportAsync_UsesDetectedMimeAndReportsMissingAltAndBrokenReference()
    {
        var package = Package("<item id=\"false-jpeg\" href=\"images/false.jpg\" media-type=\"image/jpeg\" />");
        const string body = """
            <img id="false" src="../images/false.jpg" />
            <img id="missing" src="../images/missing.png" alt="Preserved fallback" />
            <img id="external" src="https://example.com/not-loaded.png" alt="Remote fallback" />
            """;

        var result = await ImportAsync(
            package,
            body,
            new Dictionary<string, byte[]> { ["EPUB/images/false.jpg"] = Png });

        var document = Assert.IsType<FlowDocument>(result.Document);
        Assert.Equal("image/png", Assert.Single(document.Assets).Value.MediaType);
        Assert.Contains(result.Diagnostics, static item => item.Code == EpubDiagnosticCodes.ImageMediaTypeMismatch);
        Assert.Contains(result.Diagnostics, static item => item.Code == EpubDiagnosticCodes.MissingImageAlternativeText);
        Assert.Contains(result.Diagnostics, static item => item.Code == EpubDiagnosticCodes.MissingResource);
        Assert.Contains(
            document.Index.Locations.Select(static location => location.Node).OfType<Paragraph>(),
            static paragraph => Assert.IsType<Text>(paragraph.Content.Single()).Value == "Preserved fallback");
        Assert.Contains(
            document.Index.Locations.Select(static location => location.Node).OfType<Paragraph>(),
            static paragraph => Assert.IsType<Text>(paragraph.Content.Single()).Value == "Remote fallback");
    }

    [Fact]
    public async Task ImportAsync_UsesManifestImageFallback()
    {
        var package = Package("""
            <item id="broken" href="images/broken.bin" media-type="image/avif" fallback="fallback" />
            <item id="fallback" href="images/fallback.png" media-type="image/png" />
            """);

        var result = await ImportAsync(
            package,
            "<img src=\"../images/broken.bin\" alt=\"Fallback\"/>",
            new Dictionary<string, byte[]>
            {
                ["EPUB/images/broken.bin"] = [1, 2, 3, 4],
                ["EPUB/images/fallback.png"] = Png,
            });

        var document = Assert.IsType<FlowDocument>(result.Document);
        Assert.Equal("image/png", Assert.Single(document.Assets).Value.MediaType);
        Assert.Contains(result.Diagnostics, static item => item.Code == EpubDiagnosticCodes.UnsupportedImageFormat);
        Assert.Contains(result.Diagnostics, static item => item.Code == EpubDiagnosticCodes.ImageFallbackUsed);
    }

    [Fact]
    public async Task ImportAsync_AcceptsPassiveSvgAndSanitizesActiveSvg()
    {
        var package = Package("""
            <item id="safe" href="images/safe.svg" media-type="image/svg+xml" />
            <item id="active" href="images/active.svg" media-type="image/svg+xml" />
            """);
        const string body = """
            <img src="../images/safe.svg" alt="Safe"/>
            <img src="../images/active.svg" alt="Active fallback"/>
            """;

        var result = await ImportAsync(
            package,
            body,
            new Dictionary<string, byte[]>
            {
                ["EPUB/images/safe.svg"] = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><path d=\"M0 0L1 1\"/></svg>"u8.ToArray(),
                ["EPUB/images/active.svg"] = "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"u8.ToArray(),
            });

        var document = Assert.IsType<FlowDocument>(result.Document);
        Assert.Equal(2, document.Assets.Count);
        Assert.All(document.Assets.Values, static asset => Assert.Equal("image/svg+xml", asset.MediaType));
        Assert.Contains(result.Diagnostics, static item => item.Code == EpubDiagnosticCodes.SanitizedSvg);
        Assert.DoesNotContain(
            document.Assets.Values.SelectMany(static asset => asset.Data.ToArray()),
            static value => value == (byte)'!');
        Assert.DoesNotContain(
            document.Assets.Values.Select(static asset => System.Text.Encoding.UTF8.GetString(asset.Data.ToArray())),
            static value => value.Contains("script", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ImportAsync_RejectsAnimatedGifAndPreservesAltText()
    {
        var package = Package("<item id=\"animated\" href=\"images/animated.gif\" media-type=\"image/gif\" />");
        var firstFrame = Gif.AsSpan(19, Gif.Length - 20).ToArray();
        var animated = Gif[..^1].Concat(firstFrame).Append((byte)0x3b).ToArray();

        var result = await ImportAsync(
            package,
            "<img src=\"../images/animated.gif\" alt=\"Animation unavailable\"/>",
            new Dictionary<string, byte[]> { ["EPUB/images/animated.gif"] = animated });

        var document = Assert.IsType<FlowDocument>(result.Document);
        Assert.Empty(document.Assets);
        Assert.Contains(result.Diagnostics, static item => item.Code == EpubDiagnosticCodes.UnsupportedImageFormat);
        Assert.Contains(
            document.Index.Locations.Select(static location => location.Node).OfType<Paragraph>(),
            static paragraph => Assert.IsType<Text>(paragraph.Content.Single()).Value == "Animation unavailable");
    }

    [Fact]
    public async Task ImportAsync_InvalidSvgUsesRasterFallbackOrAlternativeText()
    {
        var package = Package("""
            <item id="bad-svg" href="images/bad.svg" media-type="image/svg+xml" fallback="png" />
            <item id="png" href="images/fallback.png" media-type="image/png" />
            <item id="bad-only" href="images/bad-only.svg" media-type="image/svg+xml" />
            """);
        const string body = """
            <img src="../images/bad.svg" alt="Raster fallback"/>
            <img src="../images/bad-only.svg" alt="Text fallback"/>
            """;

        var result = await ImportAsync(
            package,
            body,
            new Dictionary<string, byte[]>
            {
                ["EPUB/images/bad.svg"] = "<svg xmlns=\"http://www.w3.org/2000/svg\"><path>"u8.ToArray(),
                ["EPUB/images/bad-only.svg"] = "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>"u8.ToArray(),
                ["EPUB/images/fallback.png"] = Png,
            });

        var document = Assert.IsType<FlowDocument>(result.Document);
        Assert.Equal("image/png", Assert.Single(document.Assets).Value.MediaType);
        Assert.Contains(result.Diagnostics, static item => item.Code == EpubDiagnosticCodes.UnsafeSvg);
        Assert.Contains(result.Diagnostics, static item => item.Code == EpubDiagnosticCodes.ImageFallbackUsed);
        Assert.Contains(
            document.Index.Locations.Select(static item => item.Node).OfType<Paragraph>(),
            static paragraph => paragraph.Content.OfType<Text>().Any(text => text.Value == "Text fallback"));
    }

    [Fact]
    public async Task ImportAsync_EnforcesImageDimensionAndByteLimits()
    {
        var package = Package("""
            <item id="wide" href="images/wide.png" media-type="image/png" />
            <item id="large" href="images/large.png" media-type="image/png" />
            """);
        var wide = Png.ToArray();
        wide[19] = 2;
        var limits = new EpubImportLimits(maximumImageBytes: Png.Length, maximumImageWidth: 1);

        var result = await ImportAsync(
            package,
            "<img src=\"../images/wide.png\" alt=\"Wide\"/><img src=\"../images/large.png\" alt=\"Large\"/>",
            new Dictionary<string, byte[]>
            {
                ["EPUB/images/wide.png"] = wide,
                ["EPUB/images/large.png"] = Png.Concat(new byte[] { 0 }).ToArray(),
            },
            limits);

        Assert.Empty(Assert.IsType<FlowDocument>(result.Document).Assets);
        Assert.Contains(result.Diagnostics, static item => item.Code == EpubDiagnosticCodes.ImageDimensionsExceeded);
        Assert.Contains(result.Diagnostics, static item => item.Code == EpubDiagnosticCodes.ImageBytesExceeded);
    }

    private static string Package(string imageItems) => PackagePrefix + imageItems + PackageSuffix;

    private static async Task<EpubImportResult> ImportAsync(
        string package,
        string body,
        IReadOnlyDictionary<string, byte[]> images,
        EpubImportLimits? limits = null)
    {
        var chapter = $"""
            <html xmlns="http://www.w3.org/1999/xhtml"><body>{body}</body></html>
            """;
        await using var epub = MinimalEpubFactory.Create(
            package: package,
            chapterOne: chapter,
            includeSecondChapter: false,
            includeImage: false,
            additionalBinaryEntries: images);
        return await new EpubImporter(limits).ImportAsync(epub);
    }
}
