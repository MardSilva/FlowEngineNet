using Flow.Core;

namespace Flow.Documents.Tests;

public sealed class SemanticModelTests
{
    [Fact]
    public void FlowDocument_CanRepresentEveryBasicSemanticNode()
    {
        var footnoteId = new NodeId("footnote-identity");
        var richParagraph = new Paragraph(
            new NodeId("p-rich-content"),
            [
                new Text("Flow keeps "),
                new Strong([new Text("identity")]),
                new Text(" and "),
                new Emphasis([new Text("meaning")]),
                new Underline([new Text("independent")]),
                new Strikethrough([new Text("fixed")]),
                new InlineCode("Document != Layout"),
                new Link("https://example.org/flow", [new Text("reference")]),
                new FootnoteReference(footnoteId),
                new LineBreak(),
            ]);

        var caption = new Caption(new NodeId("caption-model"), [new Text("The Flow model")]);
        var chapter = new Chapter(
            new NodeId("chapter-foundations"),
            [
                new TableOfContents(new NodeId("toc-main"), [new Text("Contents")]),
                new Heading(new NodeId("heading-foundations"), 1, [new Text("Foundations")]),
                richParagraph,
                new BlockQuote(
                    new NodeId("quote-identity"),
                    [new Paragraph(new NodeId("p-quote"), [new Text("Content is not layout.")])]),
                new OrderedList(
                    new NodeId("list-ordered"),
                    [new ListItem(new NodeId("item-first"), [Paragraph("p-first", "Identity")])]),
                new UnorderedList(
                    new NodeId("list-unordered"),
                    [new ListItem(new NodeId("item-second"), [Paragraph("p-second", "Structure")])]),
                new Figure(
                    new NodeId("figure-model"),
                    new AssetId("flow-model.svg"),
                    caption,
                    "A semantic document feeding a layout engine"),
                new Footnote(footnoteId, [Paragraph("p-footnote", "An explanatory note.")]),
                new HorizontalRule(new NodeId("rule-transition")),
                new CodeBlock(new NodeId("code-invariant"), "Document != Layout", "text"),
                new Section(
                    new NodeId("section-conclusion"),
                    [new Heading(new NodeId("heading-conclusion"), 2, [new Text("Conclusion")])]),
            ]);

        var document = new FlowDocument(
            new DocumentIdentity(
                new DocumentId("urn:flow:document:550e8400-e29b-41d4-a716-446655440000"),
                "0.1"),
            new DocumentMetadata(
                "The Flow Experiment",
                "en",
                ["Flow contributors"],
                "An adaptive document model"),
            new DocumentContent([chapter]),
            [new FlowAsset(new AssetId("flow-model.svg"), "image/svg+xml", "flow-model.svg", "<svg/>"u8.ToArray())],
            new DocumentPresentation(),
            new DocumentIntegrity("SHA-256", "ABC123", "flow-c14n-0.1"));

        Assert.Equal("The Flow Experiment", document.Metadata.Title);
        Assert.Equal("0.1", document.Identity.Version);
        Assert.Single(document.Content.Children);
        Assert.Equal(11, chapter.Children.Length);
        Assert.Same(caption, Assert.IsType<Figure>(chapter.Children[6]).Caption);
        Assert.Equal(10, richParagraph.Content.Length);
        Assert.Contains(document.Assets, pair => pair.Key == new AssetId("flow-model.svg"));
    }

    [Fact]
    public void Constructors_CopyMutableInputCollectionsAndAssetBytes()
    {
        var authors = new List<string> { "Original author" };
        var paragraphSource = new List<InlineNode> { new Text("Original text") };
        var blockSource = new List<DocumentNode>
        {
            new Paragraph(new NodeId("p-original"), paragraphSource),
        };
        var bytes = new byte[] { 1, 2, 3 };
        var assets = new List<FlowAsset>
        {
            new(new AssetId("image.bin"), "application/octet-stream", "image.bin", bytes),
        };

        var document = new FlowDocument(
            new DocumentIdentity(new DocumentId("urn:flow:document:immutable")),
            new DocumentMetadata("Immutable", authors: authors),
            new DocumentContent(blockSource),
            assets);

        authors[0] = "Changed author";
        paragraphSource.Clear();
        blockSource.Clear();
        bytes[0] = 99;
        assets.Clear();

        Assert.Equal("Original author", document.Metadata.Authors[0]);
        var paragraph = Assert.IsType<Paragraph>(Assert.Single(document.Content.Children));
        Assert.Equal("Original text", Assert.IsType<Text>(Assert.Single(paragraph.Content)).Value);
        Assert.Equal((byte)1, document.Assets[new AssetId("image.bin")].Data[0]);
    }

    [Fact]
    public void FootnoteReference_CopiesItsOptionalInlineLabel()
    {
        var source = new List<InlineNode> { new Strong([new Text("12")]) };

        var reference = new FootnoteReference(new NodeId("note-twelve"), source);
        source.Clear();

        Assert.IsType<Strong>(Assert.Single(reference.Label));
        Assert.Empty(new FootnoteReference(new NodeId("note-with-renderer-label")).Label);
    }

    [Fact]
    public void PublicModelPropertiesAreReadOnly()
    {
        Type[] modelTypes =
        [
            typeof(FlowDocument), typeof(DocumentIdentity), typeof(DocumentMetadata),
            typeof(DocumentContent), typeof(FlowAsset), typeof(DocumentPresentation),
            typeof(DocumentIntegrity), typeof(DocumentNode), typeof(InlineNode),
            typeof(Chapter), typeof(Section), typeof(Heading), typeof(Paragraph),
            typeof(BlockQuote), typeof(OrderedList), typeof(UnorderedList), typeof(ListItem),
            typeof(Figure), typeof(Caption), typeof(Footnote), typeof(HorizontalRule),
            typeof(CodeBlock), typeof(TableOfContents), typeof(Text), typeof(Strong),
            typeof(Emphasis), typeof(Underline), typeof(Strikethrough), typeof(InlineCode),
            typeof(Link), typeof(FootnoteReference), typeof(LineBreak),
            typeof(TypographyStyle), typeof(TypographySet),
            typeof(HeadingPresentation), typeof(ParagraphPresentation),
            typeof(FigurePresentation), typeof(CaptionPresentation),
            typeof(FootnotePresentation), typeof(CodeBlockPresentation),
            typeof(TableOfContentsPresentation),
        ];

        var writableProperties = modelTypes
            .SelectMany(static type => type.GetProperties())
            .Where(static property => property.SetMethod is not null)
            .Select(static property => $"{property.DeclaringType?.Name}.{property.Name}")
            .ToArray();

        Assert.Empty(writableProperties);
    }

    [Fact]
    public void SemanticConstructorsEnforceLocalInvariants()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Heading(new NodeId("heading-invalid"), 7, [new Text("Invalid")]));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new OrderedList(new NodeId("list-invalid"), [], 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TableOfContents(new NodeId("toc-invalid"), [], 0));
        Assert.Throws<ArgumentException>(() => new DocumentMetadata(" "));
    }

    [Fact]
    public void FlowDocument_RejectsDuplicateAssetIds()
    {
        var first = new FlowAsset(new AssetId("cover.png"), "image/png", "cover.png", ReadOnlyMemory<byte>.Empty);
        var second = new FlowAsset(new AssetId("cover.png"), "image/png", "other.png", ReadOnlyMemory<byte>.Empty);

        Assert.Throws<ArgumentException>(
            () => new FlowDocument(
                new DocumentIdentity(new DocumentId("urn:flow:document:duplicate-assets")),
                new DocumentMetadata("Duplicate assets"),
                new DocumentContent([]),
                [first, second]));
    }

    [Fact]
    public void DocumentPresentation_CopiesTypedNodeTypographyAndSupportsDecorationFlags()
    {
        var nodeId = new NodeId("styled-paragraph");
        var source = new List<KeyValuePair<NodeId, TypographyStyle>>
        {
            KeyValuePair.Create(
                nodeId,
                new TypographyStyle(
                    textDecoration: TextDecoration.Underline | TextDecoration.LineThrough)),
        };

        var presentation = new DocumentPresentation(nodeTypography: source);
        source.Clear();

        Assert.Single(presentation.NodeTypography);
        Assert.Equal(
            TextDecoration.Underline | TextDecoration.LineThrough,
            presentation.NodeTypography[nodeId].TextDecoration);
        Assert.Throws<ArgumentOutOfRangeException>(() => new TypographyStyle(textDecoration: (TextDecoration)8));
    }

    private static Paragraph Paragraph(string id, string text) =>
        new(new NodeId(id), [new Text(text)]);
}
