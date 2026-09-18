using Flow.Documents;
using Flow.Layout;
using Flow.Rendering.Html;
using Flow.Security;

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

    [Fact]
    public async Task ImportAsync_PreservesInlineImagesAndMixedParagraphOrderAsValidBlocks()
    {
        var package = Package("<item id=\"inline\" href=\"images/inline.png\" media-type=\"image/png\" />");
        const string body = """
            <p id="mixed">Before <em>middle <span><img id="inline-image" src="../images/inline.png" alt="Inline artwork" /></span> after</em> end.</p>
            <p id="image-only"><img id="only-image" src="../images/inline.png" alt="Standalone artwork" /></p>
            """;

        var result = await ImportAsync(
            package,
            body,
            new Dictionary<string, byte[]> { ["EPUB/images/inline.png"] = Png });

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics));
        var document = Assert.IsType<FlowDocument>(result.Document);
        var chapter = Assert.IsType<Chapter>(Assert.Single(document.Content.Children));
        Assert.Collection(
            chapter.Children,
            first => Assert.Equal("Before middle ", NodeText(Assert.IsType<Paragraph>(first))),
            firstImage => Assert.Equal("Inline artwork", Assert.IsType<Figure>(firstImage).AlternativeText),
            second => Assert.Equal(" after end.", NodeText(Assert.IsType<Paragraph>(second))),
            secondImage => Assert.Equal("Standalone artwork", Assert.IsType<Figure>(secondImage).AlternativeText));

        var figures = chapter.Children.OfType<Figure>().ToArray();
        Assert.Equal(2, figures.Length);
        Assert.Equal(figures[0].AssetId, figures[1].AssetId);
        Assert.Single(document.Assets);
        Assert.True(new DocumentValidator().Validate(document).IsValid);

        var promotion = Assert.Single(result.Diagnostics, static item =>
            item.Code == EpubDiagnosticCodes.UnsupportedElement
            && item.Message.Contains("Inline image promoted", StringComparison.Ordinal));
        Assert.Contains("2 times", promotion.Message, StringComparison.Ordinal);

        Assert.True(result.SourceMap!.TryResolve("EPUB/text/chapter-1.xhtml", "mixed", out var mixedId));
        Assert.Equal(Assert.IsType<Paragraph>(chapter.Children[0]).Id, mixedId);
        Assert.True(result.SourceMap.TryResolve("EPUB/text/chapter-1.xhtml", "inline-image", out var inlineImageId));
        Assert.Equal(figures[0].Id, inlineImageId);
        Assert.True(result.SourceMap.TryResolve("EPUB/text/chapter-1.xhtml", "image-only", out var imageOnlyId));
        Assert.True(result.SourceMap.TryResolve("EPUB/text/chapter-1.xhtml", "only-image", out var onlyImageId));
        Assert.Equal(figures[1].Id, imageOnlyId);
        Assert.Equal(figures[1].Id, onlyImageId);

        var fidelity = new EpubFidelityAnalyzer().Analyze(result);
        var imageMeasurement = Assert.Single(
            fidelity.Measurements,
            static item => item.Metric == EpubFidelityMetric.Images);
        Assert.Equal(2, imageMeasurement.SourceCount);
        Assert.Equal(2, imageMeasurement.DestinationCount);
        Assert.Equal(2, imageMeasurement.TransformedCount);
        Assert.Equal(0, imageMeasurement.LostCount);
    }

    [Fact]
    public async Task ImportAsync_InlineImageRemainsStableThroughJsonLayoutAndHtml()
    {
        var package = Package("<item id=\"inline\" href=\"images/inline.png\" media-type=\"image/png\" />");
        const string body = """
            <p id="ordered">Alpha <strong>beta<img id="inline-image" src="../images/inline.png" alt="Inline artwork" />gamma</strong> omega.</p>
            """;
        var assets = new Dictionary<string, byte[]> { ["EPUB/images/inline.png"] = Png };

        var first = await ImportAsync(package, body, assets);
        var second = await ImportAsync(package, body, assets);
        var document = Assert.IsType<FlowDocument>(first.Document);
        var repeated = Assert.IsType<FlowDocument>(second.Document);
        var originalNodes = Assert.IsType<Chapter>(Assert.Single(document.Content.Children)).Children;
        var repeatedNodes = Assert.IsType<Chapter>(Assert.Single(repeated.Content.Children)).Children;
        Assert.Equal(originalNodes.Select(static node => node.Id), repeatedNodes.Select(static node => node.Id));

        var integrity = new Sha256DocumentIntegrityService(new FlowDocumentCanonicalizer());
        var originalHash = integrity.ComputeHash(document);
        await using var json = new MemoryStream();
        await new FlowJsonDocumentSerializer().SerializeAsync(document, json);
        json.Position = 0;
        var restored = await new FlowJsonDocumentSerializer().DeserializeAsync(json);
        Assert.Equal(originalHash, integrity.ComputeHash(restored));

        var preferences = new UserReadingPreferences();
        var layout = new AdaptiveLayoutEngine().Layout(
            restored,
            new LayoutContext(390, 844, DeviceClass.Phone, userPreferences: preferences));
        var html = new HtmlDocumentRenderer().RenderToString(restored, layout, preferences);
        var restoredNodes = Assert.IsType<Chapter>(Assert.Single(restored.Content.Children)).Children;
        Assert.Collection(
            restoredNodes,
            firstParagraph => Assert.Equal("Alpha beta", NodeText(Assert.IsType<Paragraph>(firstParagraph))),
            image => Assert.IsType<Figure>(image),
            secondParagraph => Assert.Equal("gamma omega.", NodeText(Assert.IsType<Paragraph>(secondParagraph))));
        Assert.True(
            html.IndexOf($"id=\"{restoredNodes[0].Id.Value}\"", StringComparison.Ordinal)
            < html.IndexOf($"id=\"{restoredNodes[1].Id.Value}\"", StringComparison.Ordinal));
        Assert.True(
            html.IndexOf($"id=\"{restoredNodes[1].Id.Value}\"", StringComparison.Ordinal)
            < html.IndexOf($"id=\"{restoredNodes[2].Id.Value}\"", StringComparison.Ordinal));
        Assert.Contains("data:image/png;base64,", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportAsync_PreservesImagesInsideHeadingsInSourceOrder()
    {
        var package = Package("<item id=\"heading-image\" href=\"images/heading.png\" media-type=\"image/png\" />");
        const string body = """
            <h1 id="visual"><img id="leading-image" src="../images/heading.png" alt="Chapter artwork"/><span>Chapter one</span></h1>
            <h2 id="linked-visual"><a href="https://example.invalid/catalog"><img src="../images/heading.png" alt="Catalog artwork"/></a></h2>
            """;

        var imageAssets = new Dictionary<string, byte[]> { ["EPUB/images/heading.png"] = Png };
        var result = await ImportAsync(package, body, imageAssets);
        var repeated = await ImportAsync(package, body, imageAssets);

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics));
        var document = Assert.IsType<FlowDocument>(result.Document);
        var repeatedDocument = Assert.IsType<FlowDocument>(repeated.Document);
        var chapter = Assert.IsType<Chapter>(Assert.Single(document.Content.Children));
        var repeatedChapter = Assert.IsType<Chapter>(Assert.Single(repeatedDocument.Content.Children));
        Assert.Equal(
            chapter.Children.Select(static node => node.Id),
            repeatedChapter.Children.Select(static node => node.Id));
        Assert.Collection(
            chapter.Children,
            first => Assert.Equal("Chapter artwork", Assert.IsType<Figure>(first).AlternativeText),
            second =>
            {
                var heading = Assert.IsType<Heading>(second);
                Assert.Equal(1, heading.Level);
                Assert.Equal("Chapter one", InlineText(heading.Content));
            },
            third =>
            {
                var heading = Assert.IsType<Heading>(third);
                Assert.Equal(2, heading.Level);
                Assert.Equal("Catalog artwork", InlineText(heading.Content));
            },
            fourth =>
            {
                var figure = Assert.IsType<Figure>(fourth);
                Assert.Equal("Catalog artwork", figure.AlternativeText);
                Assert.Equal("https://example.invalid/catalog", figure.Link?.ExternalUri);
            });
        Assert.Single(document.Assets);
        Assert.True(new DocumentValidator().Validate(document).IsValid);
        Assert.True(result.SourceMap!.TryResolve("EPUB/text/chapter-1.xhtml", "visual", out var visualId));
        Assert.Equal(chapter.Children[1].Id, visualId);
        Assert.True(result.SourceMap.TryResolve(
            "EPUB/text/chapter-1.xhtml",
            "leading-image",
            out var imageId));
        Assert.Equal(chapter.Children[0].Id, imageId);

        var imageApproximation = Assert.Single(result.Diagnostics, static item =>
            item.Code == EpubDiagnosticCodes.UnsupportedElement
            && item.Message.Contains("inside a heading", StringComparison.Ordinal));
        Assert.Contains("2 times", imageApproximation.Message, StringComparison.Ordinal);

        var fidelity = new EpubFidelityAnalyzer().Analyze(result);
        var images = Assert.Single(fidelity.Measurements, static item =>
            item.Metric == EpubFidelityMetric.Images);
        Assert.Equal(2, images.SourceCount);
        Assert.Equal(2, images.DestinationCount);
        Assert.Equal(0, images.LostCount);

        var serializer = new FlowJsonDocumentSerializer();
        var integrity = new Sha256DocumentIntegrityService(new FlowDocumentCanonicalizer());
        var hash = integrity.ComputeHash(document);
        Assert.Equal(hash, integrity.ComputeHash(repeatedDocument));
        await using var json = new MemoryStream();
        await serializer.SerializeAsync(document, json);
        json.Position = 0;
        var restored = await serializer.DeserializeAsync(json);
        Assert.Equal(hash, integrity.ComputeHash(restored));

        var preferences = new UserReadingPreferences();
        var layout = new AdaptiveLayoutEngine().Layout(
            restored,
            new LayoutContext(390, 844, DeviceClass.Phone, userPreferences: preferences));
        var html = new HtmlDocumentRenderer().RenderToString(restored, layout, preferences);
        Assert.Equal(2, CountOccurrences(html, "<figure"));
        Assert.Contains("href=\"https://example.invalid/catalog\"", html, StringComparison.Ordinal);
        Assert.True(
            html.IndexOf($"id=\"{chapter.Children[0].Id.Value}\"", StringComparison.Ordinal)
            < html.IndexOf($"id=\"{chapter.Children[1].Id.Value}\"", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true, false, "image/png", "cover.png")]
    [InlineData(false, true, "image/jpeg", "cover.jpg")]
    public async Task ImportAsync_ConvertsEpubCoverSvgImageToPersistentFigure(
        bool epub3,
        bool legacyXlink,
        string mediaType,
        string fileName)
    {
        var imageBytes = mediaType == "image/png" ? Png : Jpeg;
        var package = CoverPackage(epub3, fileName, mediaType);
        var referenceAttribute = legacyXlink
            ? $"xlink:href=\"../images/{fileName.Replace(".", "%2E", StringComparison.Ordinal)}\""
            : $"href=\"../images/./{fileName}\"";
        var body = $"""
            <div class="cover-wrapper">
              <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink"
                   id="cover-svg" viewBox="0 0 100 160" width="100" height="160">
                <title>Solar cover</title>
                <desc>Yellow publication artwork</desc>
                <image id="cover-raster" {referenceAttribute} width="100" height="160" />
              </svg>
            </div>
            """;

        var result = await ImportAsync(
            package,
            body,
            new Dictionary<string, byte[]> { [$"EPUB/images/{fileName}"] = imageBytes });

        var document = Assert.IsType<FlowDocument>(result.Document);
        var figure = Assert.Single(document.Index.Locations.Select(static item => item.Node).OfType<Figure>());
        Assert.Equal(mediaType, Assert.Single(document.Assets).Value.MediaType);
        Assert.Equal(figure.Id, document.Presentation?.Cover?.FigureId);
        Assert.Contains("Solar cover", figure.AlternativeText, StringComparison.Ordinal);
        Assert.Contains("Yellow publication artwork", figure.AlternativeText, StringComparison.Ordinal);
        Assert.Equal(figure.AssetId, Assert.IsType<EpubCoverMetadata>(result.MetadataReport!.Cover).AssetId);
        Assert.True(result.SourceMap!.TryResolve("EPUB/text/chapter-1.xhtml", "cover-svg", out var svgNodeId));
        Assert.True(result.SourceMap.TryResolve("EPUB/text/chapter-1.xhtml", "cover-raster", out var imageNodeId));
        Assert.Equal(figure.Id, svgNodeId);
        Assert.Equal(figure.Id, imageNodeId);
        Assert.True(new DocumentValidator().Validate(document).IsValid);

        var hashService = new Sha256DocumentIntegrityService(new FlowDocumentCanonicalizer());
        var hash = hashService.ComputeHash(document);
        await using var serialized = new MemoryStream();
        await new FlowJsonDocumentSerializer().SerializeAsync(document, serialized);
        serialized.Position = 0;
        var restored = await new FlowJsonDocumentSerializer().DeserializeAsync(serialized);
        Assert.Equal(figure.Id, restored.Presentation?.Cover?.FigureId);
        Assert.Equal(hash, hashService.ComputeHash(restored));

        var withoutCoverIntent = new FlowDocument(
            document.Identity,
            document.Metadata,
            document.Content,
            document.Assets.Values,
            document.Presentation is null
                ? null
                : new DocumentPresentation(nodeTypography: document.Presentation.NodeTypography));
        Assert.Equal(hash, hashService.ComputeHash(withoutCoverIntent));

        foreach (var (width, height, device) in new[]
                 {
                     (390d, 844d, DeviceClass.Phone),
                     (1600d, 1000d, DeviceClass.Desktop),
                 })
        {
            var preferences = new UserReadingPreferences();
            var layout = new AdaptiveLayoutEngine().Layout(
                restored,
                new LayoutContext(width, height, device, userPreferences: preferences));
            var html = new HtmlDocumentRenderer().RenderToString(restored, layout, preferences);
            Assert.Contains(
                $"<figure id=\"{figure.Id.Value}\" data-publication-role=\"cover\"",
                html,
                StringComparison.Ordinal);
            Assert.Contains($"data:{mediaType};base64,", html, StringComparison.Ordinal);
            Assert.DoesNotContain("viewBox", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("xlink:href", html, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task ImportAsync_UsesSafeXhtmlFallbackAndRejectsActiveOrExternalSvgContent()
    {
        var package = CoverPackage(epub3: true, "cover.png", "image/png");
        const string body = """
            <figure id="cover-figure">
              <svg xmlns="http://www.w3.org/2000/svg" onload="steal()">
                <script>steal()</script>
                <image href="https://example.invalid/remote.png" />
                <image href="https://example.invalid/remote.png" />
              </svg>
              <img src="../images/cover.png" alt="Safe raster fallback" />
              <figcaption>Publication cover</figcaption>
            </figure>
            """;

        var result = await ImportAsync(
            package,
            body,
            new Dictionary<string, byte[]> { ["EPUB/images/cover.png"] = Png });

        var document = Assert.IsType<FlowDocument>(result.Document);
        var figure = Assert.Single(document.Index.Locations.Select(static item => item.Node).OfType<Figure>());
        Assert.Equal("Safe raster fallback", figure.AlternativeText);
        Assert.Equal(figure.Id, document.Presentation?.Cover?.FigureId);
        Assert.Contains(result.Diagnostics, static item => item.Code == EpubDiagnosticCodes.UnsafeSvg);
        Assert.Contains(result.Diagnostics, static item =>
            item.Code == EpubDiagnosticCodes.InvalidSvgImageReference
            && item.Message.Contains("2 times", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, static item => item.Code == EpubDiagnosticCodes.SvgImageFallbackUsed);

        var preferences = new UserReadingPreferences();
        var layout = new AdaptiveLayoutEngine().Layout(
            document,
            new LayoutContext(390, 844, DeviceClass.Phone, userPreferences: preferences));
        var html = new HtmlDocumentRenderer().RenderToString(document, layout, preferences);
        Assert.DoesNotContain("steal", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("example.invalid", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Safe raster fallback", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportAsync_DetectsFalseMimeAndCircularFallbackFromSvgReference()
    {
        var package = Package("""
            <item id="false-cover" href="images/cover.jpg" media-type="image/jpeg" properties="cover-image" />
            <item id="cycle-a" href="images/a.bin" media-type="image/avif" fallback="cycle-b" />
            <item id="cycle-b" href="images/b.bin" media-type="image/avif" fallback="cycle-a" />
            """);
        const string body = """
            <svg xmlns="http://www.w3.org/2000/svg"><image href="../images/cover.jpg" alt="Detected PNG" /></svg>
            <svg xmlns="http://www.w3.org/2000/svg"><image href="../images/a.bin" alt="Circular unavailable" /></svg>
            """;

        var result = await ImportAsync(
            package,
            body,
            new Dictionary<string, byte[]>
            {
                ["EPUB/images/cover.jpg"] = Png,
                ["EPUB/images/a.bin"] = [1, 2, 3],
                ["EPUB/images/b.bin"] = [4, 5, 6],
            });

        var document = Assert.IsType<FlowDocument>(result.Document);
        Assert.Equal("image/png", Assert.Single(document.Assets).Value.MediaType);
        Assert.Contains(result.Diagnostics, static item => item.Code == EpubDiagnosticCodes.ImageMediaTypeMismatch);
        Assert.Contains(result.Diagnostics, static item => item.Code == EpubDiagnosticCodes.CircularFallback);
        Assert.Contains(result.Diagnostics, static item => item.Code == EpubDiagnosticCodes.InvalidSvgImageReference);
        Assert.Contains(
            document.Index.Locations.Select(static item => item.Node).OfType<Paragraph>(),
            static paragraph => paragraph.Content.OfType<Text>().Any(static text => text.Value == "Circular unavailable"));
    }

    [Fact]
    public async Task ImportAsync_DeduplicatesCoverSvgAndContentImageAndDiagnosesBrokenAssociation()
    {
        var package = CoverPackage(epub3: true, "cover.png", "image/png");
        const string body = """
            <svg xmlns="http://www.w3.org/2000/svg" id="cover">
              <image href="../images/cover.png" alt="Cover" />
            </svg>
            <img id="repeat" src="../images/cover.png" alt="Repeated cover bytes" />
            <svg xmlns="http://www.w3.org/2000/svg" id="broken">
              <image href="../images/missing.png" alt="Missing diagram" />
            </svg>
            """;

        var result = await ImportAsync(
            package,
            body,
            new Dictionary<string, byte[]> { ["EPUB/images/cover.png"] = Png });

        var document = Assert.IsType<FlowDocument>(result.Document);
        var figures = document.Index.Locations.Select(static item => item.Node).OfType<Figure>().ToArray();
        Assert.Equal(2, figures.Length);
        Assert.Single(document.Assets);
        Assert.Equal(figures[0].AssetId, figures[1].AssetId);
        Assert.Equal(figures[0].Id, document.Presentation?.Cover?.FigureId);
        Assert.Contains(result.Diagnostics, static item =>
            item.Code == EpubDiagnosticCodes.InvalidSvgImageReference
            && item.Resource == "EPUB/text/chapter-1.xhtml");
        Assert.Contains(
            document.Index.Locations.Select(static item => item.Node).OfType<Paragraph>(),
            static paragraph => paragraph.Content.OfType<Text>().Any(static text => text.Value == "Missing diagram"));
    }

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

    private static string CoverPackage(bool epub3, string fileName, string mediaType) => epub3
        ? $"""
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="book-id">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="book-id">urn:flow:test:svg-cover</dc:identifier>
                <dc:title>Isto é filtro solar</dc:title>
              </metadata>
              <manifest>
                <item id="chapter" href="text/chapter-1.xhtml" media-type="application/xhtml+xml" />
                <item id="cover-image" href="images/{fileName}" media-type="{mediaType}" properties="cover-image" />
              </manifest>
              <spine><itemref idref="chapter" /></spine>
            </package>
            """
        : $"""
            <package xmlns="http://www.idpf.org/2007/opf" version="2.0" unique-identifier="book-id">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="book-id">urn:flow:test:svg-cover-epub2</dc:identifier>
                <dc:title>Isto é filtro solar</dc:title>
                <meta name="cover" content="cover-image" />
              </metadata>
              <manifest>
                <item id="chapter" href="text/chapter-1.xhtml" media-type="application/xhtml+xml" />
                <item id="cover-image" href="images/{fileName}" media-type="{mediaType}" />
              </manifest>
              <spine><itemref idref="chapter" /></spine>
            </package>
            """;

    private static string NodeText(Paragraph paragraph) => InlineText(paragraph.Content);

    private static int CountOccurrences(string value, string pattern)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(pattern, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += pattern.Length;
        }

        return count;
    }

    private static string InlineText(IEnumerable<InlineNode> nodes) => string.Concat(nodes.Select(static node => node switch
    {
        Text text => text.Value,
        InlineCode code => code.Code,
        InlineContainerNode container => InlineText(container.Children),
        FootnoteReference reference => InlineText(reference.Label),
        LineBreak => "\n",
        _ => string.Empty,
    }));

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
