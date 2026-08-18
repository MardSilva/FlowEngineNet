using System.Text;
using System.Xml.Linq;
using Flow.Core;
using Flow.Documents;
using Flow.Layout;
using Flow.Rendering.Html;
using Flow.Security;

namespace Flow.Epub.Tests;

public sealed class EpubSourceMapTests
{
    private const string ChapterWithReferences = """
        <html xmlns="http://www.w3.org/1999/xhtml">
          <body>
            <h1 id="start">Start</h1>
            <p id="self">Self</p>
            <p id="café">Encoded fragment</p>
            <p id="dup">First duplicate</p>
            <p id="dup">Second duplicate</p>
            <p id="bad id">Invalid source ID</p>
            <p id="a:b">First normalized collision</p>
            <p id="a-b">Second normalized collision</p>
            <p>
              <a href="#self">fragment</a>
              <a href="chapter-1.xhtml#self">same resource</a>
              <a href="chapter-2.xhtml#end">other chapter</a>
              <a href="./chapter-2.xhtml#end">dot segment</a>
              <a href="../text/./chapter-2.xhtml#end">parent segment</a>
              <a href="chapter-2.xhtml">resource root</a>
              <a href="#caf%C3%A9">encoded</a>
              <a href="#bad%20id">invalid but retained</a>
              <a href="https://example.com/book">external</a>
            </p>
          </body>
        </html>
        """;

    [Fact]
    public async Task ImportAsync_BuildsDeterministicSourceMapAndResolvesEverySupportedReference()
    {
        var first = await ImportAsync();
        var second = await ImportAsync();

        var firstDocument = Assert.IsType<FlowDocument>(first.Document);
        var secondDocument = Assert.IsType<FlowDocument>(second.Document);
        var sourceMap = Assert.IsType<EpubSourceMap>(first.SourceMap);
        var secondSourceMap = Assert.IsType<EpubSourceMap>(second.SourceMap);

        Assert.Equal(NodeIds(firstDocument), NodeIds(secondDocument));
        Assert.Equal(
            sourceMap.Locations.Select(LocationIdentity),
            secondSourceMap.Locations.Select(LocationIdentity));

        AssertResolves(sourceMap, "EPUB/text/chapter-1.xhtml", "self", out var selfId);
        Assert.True(sourceMap.TryResolveReference("EPUB/text/chapter-1.xhtml", "#self", out var fragmentId));
        Assert.Equal(selfId, fragmentId);
        Assert.True(sourceMap.TryResolveReference("EPUB/text/chapter-1.xhtml", "chapter-1.xhtml#self", out var sameId));
        Assert.Equal(selfId, sameId);

        AssertResolves(sourceMap, "EPUB/text/chapter-2.xhtml", "end", out var endId);
        foreach (var reference in new[]
                 {
                     "chapter-2.xhtml#end",
                     "./chapter-2.xhtml#end",
                     "../text/./chapter-2.xhtml#end",
                 })
        {
            Assert.True(sourceMap.TryResolveReference("EPUB/text/chapter-1.xhtml", reference, out var resolved));
            Assert.Equal(endId, resolved);
        }

        Assert.True(sourceMap.TryResolveReference(
            "EPUB/text/chapter-1.xhtml",
            "chapter-2.xhtml",
            out var chapterId));
        Assert.IsType<Chapter>(AssertResolved(firstDocument, chapterId));

        Assert.True(sourceMap.TryResolveReference("EPUB/text/chapter-1.xhtml", "#caf%C3%A9", out var encodedId));
        AssertResolves(sourceMap, "EPUB/text/chapter-1.xhtml", "café", out var decodedId);
        Assert.Equal(decodedId, encodedId);
        Assert.True(sourceMap.TryResolveReference("EPUB/text/chapter-1.xhtml", "#bad%20id", out _));
        Assert.False(sourceMap.TryResolveReference("EPUB/text/chapter-1.xhtml", "https://example.com/book", out _));

        var links = firstDocument.Index.Locations.Select(static location => location.Node)
            .OfType<Paragraph>()
            .SelectMany(static paragraph => paragraph.Content)
            .OfType<Link>()
            .ToArray();
        Assert.Equal(9, links.Length);
        Assert.Equal("https://example.com/book", links[^1].Target);
        Assert.All(links[..^1], link => AssertResolved(firstDocument, DocumentAnchor.Parse(link.Target).TargetId));
    }

    [Fact]
    public async Task ImportAsync_DiagnosesDuplicateInvalidAndCollidingSourceIdsWithFirstOccurrencePrecedence()
    {
        var result = await ImportAsync();
        var sourceMap = Assert.IsType<EpubSourceMap>(result.SourceMap);

        Assert.Contains(result.Diagnostics, static item => item.Code == EpubDiagnosticCodes.DuplicateSourceId);
        Assert.Contains(result.Diagnostics, static item => item.Code == EpubDiagnosticCodes.InvalidSourceId);
        Assert.Contains(result.Diagnostics, static item => item.Code == EpubDiagnosticCodes.SourceIdCollision);

        var duplicates = sourceMap.Locations
            .Where(static location => location.Fragment == "dup")
            .ToArray();
        Assert.Equal(2, duplicates.Length);
        Assert.Equal([1, 2], duplicates.Select(static location => location.Occurrence));
        Assert.NotEqual(duplicates[0].NodeId, duplicates[1].NodeId);
        Assert.True(sourceMap.TryResolve("EPUB/text/chapter-1.xhtml", "dup", out var primary));
        Assert.Equal(duplicates[0].NodeId, primary);
        Assert.Contains(sourceMap.GetLocations(primary), static location => location.Fragment == "dup");

        AssertResolves(sourceMap, "EPUB/text/chapter-1.xhtml", "a:b", out var firstCollision);
        AssertResolves(sourceMap, "EPUB/text/chapter-1.xhtml", "a-b", out var secondCollision);
        Assert.NotEqual(firstCollision, secondCollision);
    }

    [Fact]
    public async Task SourceIds_SurviveJsonLayoutAndHtml_WithoutSourceMapEnteringJsonOrHash()
    {
        var imported = await ImportAsync();
        var document = Assert.IsType<FlowDocument>(imported.Document);
        var sourceMap = Assert.IsType<EpubSourceMap>(imported.SourceMap);
        var originalIds = NodeIds(document);

        var serializer = new FlowJsonDocumentSerializer();
        await using var json = new MemoryStream();
        await serializer.SerializeAsync(document, json);
        var jsonText = Encoding.UTF8.GetString(json.ToArray());
        Assert.DoesNotContain("sourceMap", jsonText, StringComparison.OrdinalIgnoreCase);
        json.Position = 0;
        var roundTripped = await serializer.DeserializeAsync(json);
        Assert.Equal(originalIds, NodeIds(roundTripped));

        var integrity = new Sha256DocumentIntegrityService(new FlowDocumentCanonicalizer());
        Assert.Equal(integrity.ComputeHash(document), integrity.ComputeHash(roundTripped));

        var preferences = new UserReadingPreferences();
        var layout = new AdaptiveLayoutEngine().Layout(
            roundTripped,
            new LayoutContext(390, 844, DeviceClass.Phone, userPreferences: preferences));
        Assert.Equal(originalIds, Flatten(layout.Nodes).Select(static node => node.SemanticId.Value).ToArray());

        var html = new HtmlDocumentRenderer().RenderToString(roundTripped, layout, preferences);
        var renderedIds = XDocument.Parse(html).Descendants()
            .Select(static element => (string?)element.Attribute("id"))
            .Where(static id => id is not null)
            .ToHashSet(StringComparer.Ordinal);
        Assert.All(sourceMap.Locations, location => Assert.Contains(location.NodeId.Value, renderedIds));
        Assert.Equal(originalIds, NodeIds(roundTripped));
    }

    private static async Task<EpubImportResult> ImportAsync()
    {
        await using var epub = MinimalEpubFactory.Create(chapterOne: ChapterWithReferences, includeImage: false);
        return await new EpubImporter().ImportAsync(epub);
    }

    private static string[] NodeIds(FlowDocument document) =>
        document.Index.Locations.Select(static location => location.Node.Id.Value).ToArray();

    private static (string Path, string? Fragment, string NodeId, int Occurrence) LocationIdentity(
        EpubSourceLocation location) =>
        (location.ResourcePath, location.Fragment, location.NodeId.Value, location.Occurrence);

    private static void AssertResolves(
        EpubSourceMap sourceMap,
        string path,
        string? fragment,
        out NodeId nodeId)
    {
        Assert.True(sourceMap.TryResolve(path, fragment, out var resolved));
        nodeId = resolved;
    }

    private static DocumentNode AssertResolved(FlowDocument document, NodeId nodeId)
    {
        Assert.True(document.Index.TryGetUniqueNode(nodeId, out var node));
        return node;
    }

    private static IEnumerable<LayoutNode> Flatten(IEnumerable<LayoutNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children))
            {
                yield return child;
            }
        }
    }
}
