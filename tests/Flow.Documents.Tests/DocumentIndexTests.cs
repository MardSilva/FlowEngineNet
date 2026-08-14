using Flow.Core;

namespace Flow.Documents.Tests;

public sealed class DocumentIndexTests
{
    [Fact]
    public void Index_ResolvesHierarchicalAndGloballyUniqueAnchors()
    {
        var paragraph = new Paragraph(new NodeId("p-002"), [new Text("Stable target")]);
        var section = new Section(new NodeId("section-identity"), [paragraph]);
        var chapter = new Chapter(new NodeId("chapter-introduction"), [section]);
        var document = CreateDocument([chapter]);

        var hierarchical = DocumentAnchor.Parse("flow:chapter-introduction/section-identity/p-002");
        var shortAnchor = DocumentAnchor.Parse("flow:p-002");

        Assert.Same(paragraph, document.ResolveAnchor(hierarchical));
        Assert.Same(paragraph, document.ResolveAnchor(shortAnchor));
        Assert.Equal(3, document.Index.NodeCount);
        Assert.False(document.TryResolveAnchor(DocumentAnchor.Parse("flow:missing"), out _));
    }

    [Fact]
    public void Index_DoesNotResolveAnAmbiguousShortAnchor()
    {
        var duplicateId = new NodeId("p-duplicate");
        var document = CreateDocument(
        [
            new Chapter(new NodeId("chapter-one"), [new Paragraph(duplicateId, [new Text("First")])]),
            new Chapter(new NodeId("chapter-two"), [new Paragraph(duplicateId, [new Text("Second")])]),
        ]);

        Assert.False(document.TryResolveAnchor(DocumentAnchor.Parse("flow:p-duplicate"), out _));
        Assert.Equal(2, document.Index.GetLocations(duplicateId).Length);
        Assert.Contains(duplicateId, document.Index.DuplicateIds);
    }

    [Fact]
    public void Index_IncludesNestedListItemsAndFigureCaptions()
    {
        var caption = new Caption(new NodeId("caption-figure"), [new Text("Caption")]);
        var document = CreateDocument(
        [
            new Chapter(
                new NodeId("chapter-content"),
                [
                    new UnorderedList(
                        new NodeId("list-points"),
                        [new ListItem(new NodeId("item-point"), [new Paragraph(new NodeId("p-point"), [new Text("Point")])])]),
                    new Figure(new NodeId("figure-model"), new AssetId("model.svg"), caption),
                ]),
        ],
        [new FlowAsset(new AssetId("model.svg"), "image/svg+xml", "model.svg", ReadOnlyMemory<byte>.Empty)]);

        Assert.Same(caption, document.ResolveAnchor(
            DocumentAnchor.Parse("flow:chapter-content/figure-model/caption-figure")));
        Assert.IsType<ListItem>(document.ResolveAnchor(
            DocumentAnchor.Parse("flow:chapter-content/list-points/item-point")));
    }

    private static FlowDocument CreateDocument(
        IEnumerable<DocumentNode> children,
        IEnumerable<FlowAsset>? assets = null) =>
        new(
            new DocumentIdentity(new DocumentId("urn:flow:document:index-tests")),
            new DocumentMetadata("Index tests"),
            new DocumentContent(children),
            assets);
}
