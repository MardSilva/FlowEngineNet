using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Flow.Core;
using Flow.Documents;
using Flow.Layout;
using Flow.Rendering.Html;

namespace Flow.Rendering.Tests;

public sealed class HtmlBookPackageRendererTests
{
    private static readonly HtmlBookIntegrity Integrity = new("SHA-256", new string('A', 64), "flow-c14n-0.1");
    private readonly HtmlBookPackageRenderer renderer = new();

    [Fact]
    public void Render_SeparatesTocAndChaptersAndResolvesEveryInternalLink()
    {
        var document = CreateTwoChapterDocument();

        var package = Render(document, 1024, 768);

        Assert.Equal(
            ["chapters/chapter-001.html", "chapters/chapter-002.html", "index.html", "manifest.json", "styles/book.css", "toc.html"],
            package.Files.Select(static file => file.Path).ToArray());
        var toc = Parse(package, "toc.html");
        Assert.Equal(
            "chapters/chapter-001.html#chapter-one-heading",
            (string?)toc.Descendants("a").Single(element => element.Value == "One").Attribute("href"));
        Assert.Equal(
            "chapters/chapter-002.html#chapter-two-heading",
            (string?)toc.Descendants("a").Single(element => element.Value == "Two").Attribute("href"));

        var first = Parse(package, "chapters/chapter-001.html");
        var second = Parse(package, "chapters/chapter-002.html");
        Assert.Equal("chapter-002.html#footnote-two", (string?)first.Descendants("a").Single(element => (string?)element.Attribute("role") == "doc-noteref").Attribute("href"));
        Assert.Equal("chapter-001.html#paragraph-one", (string?)second.Descendants("a").Single(element => element.Value == "back").Attribute("href"));
        Assert.DoesNotContain(first.Descendants("nav"), element => (string?)element.Attribute("aria-label") == "Table of contents");
        AssertAllInternalLinksResolve(package);
        AssertAllLocalResourcesResolveWithoutScripts(package);
    }

    [Fact]
    public void Render_ZeroAndOneChapterFollowTheDocumentedPlacementPolicy()
    {
        var zero = CreateDocument(
            [new Paragraph(new NodeId("only-content"), [new Text("Without chapter")])],
            "urn:flow:html-book:zero");
        var one = CreateDocument(
            [
                new Paragraph(new NodeId("front"), [new Text("Front")]),
                new Chapter(
                    new NodeId("only-chapter"),
                    [new Heading(new NodeId("only-heading"), 1, [new Text("Only")])]),
                new Paragraph(new NodeId("after"), [new Text("After")]),
            ],
            "urn:flow:html-book:one");

        var zeroPackage = Render(zero, 390, 844);
        var onePackage = Render(one, 1600, 1000);

        Assert.DoesNotContain(zeroPackage.Files, static file => file.Path.StartsWith("chapters/", StringComparison.Ordinal));
        Assert.Single(Parse(zeroPackage, "index.html").Descendants("p"), element => (string?)element.Attribute("id") == "only-content");
        Assert.Single(Parse(onePackage, "index.html").Descendants("p"), element => (string?)element.Attribute("id") == "front");
        var chapter = Parse(onePackage, "chapters/chapter-001.html");
        Assert.Single(chapter.Descendants("section"), element => (string?)element.Attribute("id") == "only-chapter");
        Assert.Single(chapter.Descendants("p"), element => (string?)element.Attribute("id") == "after");
    }

    [Fact]
    public void Render_ExternalizesAndDeduplicatesAssetsAndPreservesComplexSemantics()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var firstAsset = new AssetId("first.png");
        var secondAsset = new AssetId("second.png");
        var math = new MathExpression(
            new NodeId("math"),
            new MathElement("math", [new MathElement("mi", [new MathText("x")])]));
        var table = new Table(
            new NodeId("table"),
            [
                new TableBody(
                    new NodeId("body"),
                    [
                        new TableRow(
                            new NodeId("row"),
                            [new TableCell(new NodeId("cell"), [new Paragraph(new NodeId("cell-p"), [new Text("Cell")])])]),
                    ]),
            ]);
        var paragraph = new Paragraph(
            new NodeId("international"),
            [
                new BidirectionalSpan(TextDirection.RightToLeft, BidirectionalMode.Isolation, [new Text("مرحبا")]),
                new Ruby([new Text("日"), new RubyAnnotation([new Text("にち")])]),
            ]);
        var chapter = new Chapter(
            new NodeId("complex-chapter"),
            [
                new Heading(new NodeId("complex-heading"), 1, [new Text("Complex")]),
                new Figure(new NodeId("figure-one"), firstAsset, alternativeText: "One"),
                new Figure(new NodeId("figure-two"), secondAsset, alternativeText: "Two"),
                table,
                math,
                paragraph,
            ]);
        var document = new FlowDocument(
            new DocumentIdentity(new DocumentId("urn:flow:html-book:complex")),
            new DocumentMetadata("Complex"),
            new DocumentContent([chapter]),
            [
                new FlowAsset(firstAsset, "image/png", "../unsafe-one.png", bytes),
                new FlowAsset(secondAsset, "image/png", "unsafe-two.png", bytes),
            ],
            new DocumentPresentation(cover: new CoverPresentation(new NodeId("figure-one"))));

        var package = Render(document, 1024, 768);

        var asset = Assert.Single(package.Files, static file => file.Path.StartsWith("assets/", StringComparison.Ordinal));
        Assert.Matches("^assets/[0-9a-f]{64}\\.png$", asset.Path);
        var index = Parse(package, "index.html");
        Assert.Single(index.Descendants("figure"), element => (string?)element.Attribute("data-publication-role") == "cover-preview");
        var chapterHtml = Parse(package, "chapters/chapter-001.html");
        Assert.Equal(2, chapterHtml.Descendants("img").Count());
        Assert.All(chapterHtml.Descendants("img"), image => Assert.StartsWith("../assets/", (string?)image.Attribute("src"), StringComparison.Ordinal));
        Assert.NotEmpty(chapterHtml.Descendants("table"));
        Assert.Contains(chapterHtml.Descendants(), static element => element.Name.LocalName == "math");
        Assert.NotEmpty(chapterHtml.Descendants("bdi"));
        Assert.NotEmpty(chapterHtml.Descendants("ruby"));
        Assert.DoesNotContain(package.Files, static file => file.Path.Contains("..", StringComparison.Ordinal));
    }

    [Fact]
    public void Render_IsByteDeterministicAndManifestHashesEveryPayloadFile()
    {
        var document = CreateTwoChapterDocument();

        var first = Render(document, 1024, 768);
        var second = Render(document, 1024, 768);

        Assert.Equal(first.Files.Select(static file => file.Path), second.Files.Select(static file => file.Path));
        foreach (var firstFile in first.Files)
        {
            Assert.True(firstFile.Content.AsSpan().SequenceEqual(second.GetFile(firstFile.Path).Content.AsSpan()));
        }

        using var manifest = JsonDocument.Parse(first.GetFile("manifest.json").Content.ToArray());
        Assert.Equal(HtmlBookPackage.Format, manifest.RootElement.GetProperty("format").GetString());
        Assert.Equal(Integrity.Hash, manifest.RootElement.GetProperty("canonicalIntegrity").GetProperty("hash").GetString());
        Assert.True(manifest.RootElement.GetProperty("manifestSelfHashExcluded").GetBoolean());
        foreach (var listed in manifest.RootElement.GetProperty("files").EnumerateArray())
        {
            var file = first.GetFile(listed.GetProperty("path").GetString()!);
            Assert.Equal(file.Content.Length, listed.GetProperty("bytes").GetInt32());
            Assert.Equal(
                Convert.ToHexString(SHA256.HashData(file.Content.AsSpan())).ToLowerInvariant(),
                listed.GetProperty("sha256").GetString());
        }
    }

    [Fact]
    public void Render_MobileAndDesktopKeepIdentityCanonicalHashAndSemanticIds()
    {
        var document = CreateTwoChapterDocument();

        var mobile = Render(document, 390, 844);
        var desktop = Render(document, 1600, 1000);

        Assert.Equal(AllSemanticIds(mobile), AllSemanticIds(desktop));
        foreach (var package in new[] { mobile, desktop })
        {
            foreach (var html in package.Files.Where(static file => file.Path.EndsWith(".html", StringComparison.Ordinal)))
            {
                var parsed = XDocument.Parse(Encoding.UTF8.GetString(html.Content.AsSpan()));
                var article = parsed.Descendants("article").Single();
                Assert.Equal(document.Identity.Id.Value, (string?)article.Attribute("data-document-id"));
                Assert.Equal(Integrity.Hash, (string?)article.Attribute("data-canonical-hash"));
            }

            AssertAllInternalLinksResolve(package);
            AssertAllLocalResourcesResolveWithoutScripts(package);
        }
    }

    [Theory]
    [InlineData("../escape.html")]
    [InlineData("chapters/../../escape.html")]
    [InlineData("/absolute.html")]
    [InlineData("C:\\escape.html")]
    [InlineData("chapters\\escape.html")]
    [InlineData("chapters/unsafe:name.html")]
    public void HtmlBookFile_RejectsUnsafePackagePaths(string path)
    {
        Assert.Throws<ArgumentException>(() => new HtmlBookFile(path, "text/html", ReadOnlyMemory<byte>.Empty));
    }

    private HtmlBookPackage Render(FlowDocument document, double width, double height)
    {
        var preferences = new UserReadingPreferences();
        var layout = new AdaptiveLayoutEngine().Layout(document, new LayoutContext(width, height, userPreferences: preferences));
        return renderer.Render(document, layout, preferences, Integrity);
    }

    private static FlowDocument CreateTwoChapterDocument()
    {
        var firstHeading = new NodeId("chapter-one-heading");
        var secondHeading = new NodeId("chapter-two-heading");
        var footnote = new NodeId("footnote-two");
        var toc = new TableOfContents(
            new NodeId("toc"),
            [new Text("Contents")],
            [
                new TableOfContentsEntry([new Text("One")], DocumentAnchor.Parse($"flow:chapter-one/{firstHeading}"), 1),
                new TableOfContentsEntry([new Text("Two")], DocumentAnchor.Parse($"flow:chapter-two/{secondHeading}"), 1),
            ]);
        var first = new Chapter(
            new NodeId("chapter-one"),
            [
                new Heading(firstHeading, 1, [new Text("One")]),
                new Paragraph(
                    new NodeId("paragraph-one"),
                    [new Text("Call"), new FootnoteReference(footnote, [new Text("1")])]),
            ]);
        var second = new Chapter(
            new NodeId("chapter-two"),
            [
                new Heading(secondHeading, 1, [new Text("Two")]),
                new Footnote(
                    footnote,
                    [
                        new Paragraph(
                            new NodeId("footnote-text"),
                            [new Text("Note "), new Link("flow:chapter-one/paragraph-one", [new Text("back")])]),
                    ]),
            ]);
        return CreateDocument([toc, first, second], "urn:flow:html-book:two");
    }

    private static FlowDocument CreateDocument(IEnumerable<DocumentNode> nodes, string id) => new(
        new DocumentIdentity(new DocumentId(id), "1"),
        new DocumentMetadata("HTML book", "en", ["Flow"]),
        new DocumentContent(nodes));

    private static XDocument Parse(HtmlBookPackage package, string path) =>
        XDocument.Parse(Encoding.UTF8.GetString(package.GetFile(path).Content.AsSpan()));

    private static string[] AllSemanticIds(HtmlBookPackage package) => package.Files
        .Where(static file => file.Path.EndsWith(".html", StringComparison.Ordinal))
        .SelectMany(file => XDocument.Parse(Encoding.UTF8.GetString(file.Content.AsSpan()))
            .Descendants()
            .Attributes("id")
            .Select(static attribute => attribute.Value))
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static void AssertAllInternalLinksResolve(HtmlBookPackage package)
    {
        var htmlFiles = package.Files
            .Where(static file => file.Path.EndsWith(".html", StringComparison.Ordinal))
            .ToDictionary(static file => file.Path, file => Parse(package, file.Path), StringComparer.Ordinal);
        foreach (var (sourcePath, source) in htmlFiles)
        {
            foreach (var href in source.Descendants("a").Attributes("href").Select(static attribute => attribute.Value))
            {
                if (Uri.TryCreate(href, UriKind.Absolute, out _))
                {
                    continue;
                }

                var parts = href.Split('#', 2);
                var targetPath = parts[0].Length == 0 ? sourcePath : ResolveRelative(sourcePath, parts[0]);
                Assert.True(htmlFiles.TryGetValue(targetPath, out var target), $"Missing target file '{targetPath}' from '{sourcePath}'.");
                if (parts.Length == 2)
                {
                    Assert.Contains(target!.Descendants(), element => (string?)element.Attribute("id") == parts[1]);
                }
            }
        }
    }

    private static string ResolveRelative(string sourcePath, string reference)
    {
        var sourceUri = new Uri("https://flow.invalid/" + sourcePath, UriKind.Absolute);
        return new Uri(sourceUri, reference).AbsolutePath.TrimStart('/');
    }

    private static void AssertAllLocalResourcesResolveWithoutScripts(HtmlBookPackage package)
    {
        var paths = package.Files.Select(static file => file.Path).ToHashSet(StringComparer.Ordinal);
        foreach (var htmlFile in package.Files.Where(static file => file.Path.EndsWith(".html", StringComparison.Ordinal)))
        {
            var html = Parse(package, htmlFile.Path);
            Assert.DoesNotContain(html.Descendants(), static element => element.Name.LocalName == "script");
            var resources = html.Descendants("link").Attributes("href")
                .Concat(html.Descendants("img").Attributes("src"));
            foreach (var resource in resources)
            {
                Assert.True(
                    paths.Contains(ResolveRelative(htmlFile.Path, resource.Value)),
                    $"Missing local resource '{resource.Value}' from '{htmlFile.Path}'.");
            }
        }
    }
}
