using Flow.Core;
using Flow.Documents;

namespace Flow.Layout.Tests;

public sealed class AdaptiveLayoutEngineTests
{
    private readonly AdaptiveLayoutEngine _engine = new();

    [Fact]
    public void Layout_SmallViewportUsesOneColumnCompactMarginsAndBlockFigures()
    {
        var result = _engine.Layout(CreateDocument(), new LayoutContext(390, 844, DeviceClass.Phone));

        Assert.Equal(ViewportCategory.Small, result.Profile.ViewportCategory);
        Assert.Equal(1, result.Profile.ColumnCount);
        Assert.Equal(Length.Px(16), result.Profile.ContentMargin);
        Assert.Equal(Length.Percent(100), result.Profile.MaximumContentWidth);
        Assert.Equal(Length.Px(0), result.Profile.ColumnGap);

        var figure = Assert.IsType<FigureLayoutIntent>(FindNode(result, "figure-cover").Intent);
        Assert.True(figure.ScaleDownToFit);
        Assert.Equal(Length.Percent(100), figure.MaximumWidth);
        Assert.Equal(PreferredPlacement.Block, figure.PreferredPlacement);
    }

    [Fact]
    public void Layout_MediumViewportUsesOneBalancedColumn()
    {
        var result = _engine.Layout(
            CreateDocument(),
            new LayoutContext(900, 1000, DeviceClass.Tablet, allowTwoColumns: true));

        Assert.Equal(ViewportCategory.Medium, result.Profile.ViewportCategory);
        Assert.Equal(1, result.Profile.ColumnCount);
        Assert.Equal(Length.Px(32), result.Profile.ContentMargin);
        Assert.Equal(Length.Px(800), result.Profile.MaximumContentWidth);
        Assert.Equal(Length.Px(0), result.Profile.ColumnGap);
    }

    [Fact]
    public void Layout_LargeViewportRemainsOneColumnWithoutExplicitPermission()
    {
        var result = _engine.Layout(CreateDocument(), new LayoutContext(1600, 1000, DeviceClass.Desktop));

        Assert.Equal(ViewportCategory.Large, result.Profile.ViewportCategory);
        Assert.Equal(1, result.Profile.ColumnCount);
        Assert.Equal(Length.Px(64), result.Profile.ContentMargin);
        Assert.Equal(Length.Px(1120), result.Profile.MaximumContentWidth);
        Assert.Equal(Length.Px(0), result.Profile.ColumnGap);
    }

    [Fact]
    public void Layout_LargeViewportUsesTwoColumnsOnlyWithExplicitPermission()
    {
        var result = _engine.Layout(
            CreateDocument(),
            new LayoutContext(1600, 1000, DeviceClass.Desktop, allowTwoColumns: true));

        Assert.Equal(2, result.Profile.ColumnCount);
        Assert.Equal(Length.Px(48), result.Profile.ColumnGap);
    }

    [Fact]
    public void Layout_PreservesSemanticIdsHierarchyAndIdentity()
    {
        var document = CreateDocument();

        var result = _engine.Layout(document, new LayoutContext(900, 1000));

        Assert.Equal(document.Identity.Id, result.DocumentId);
        Assert.Equal(document.Identity.Version, result.DocumentVersion);
        Assert.Equal(new NodeId("chapter-one"), result.Nodes.Single().SemanticId);

        var chapter = result.Nodes.Single();
        var figure = chapter.Children.Single(node => node.SemanticId == new NodeId("figure-cover"));
        Assert.Same(document.ResolveAnchor(DocumentAnchor.Parse("flow:chapter-one/figure-cover")), figure.SemanticNode);
        Assert.Equal(new NodeId("caption-cover"), figure.Children.Single().SemanticId);
    }

    [Fact]
    public void Layout_ResolvesRequiredDefaultIntents()
    {
        var result = _engine.Layout(CreateDocument(), new LayoutContext(900, 1000));

        Assert.Equal(new HeadingLayoutIntent(true, true), FindNode(result, "heading-one").Intent);
        Assert.Equal(
            new FigureLayoutIntent(true, Length.Percent(100), true, PreferredPlacement.RendererChoice),
            FindNode(result, "figure-cover").Intent);
        Assert.Equal(new CodeBlockLayoutIntent(true, true), FindNode(result, "code-one").Intent);
        Assert.Equal(
            new FootnoteLayoutIntent(FootnotePresentationMode.RendererChoice),
            FindNode(result, "footnote-one").Intent);
    }

    [Fact]
    public void Layout_AppliesAuthorIntentsWithoutMutatingPresentation()
    {
        var presentation = new DocumentPresentation(
            headings: new HeadingPresentation(keepWithNext: false, avoidBreakAfter: false),
            figures: new FigurePresentation(
                keepWithCaption: false,
                preferredPlacement: PreferredPlacement.FloatEnd,
                maximumWidth: Length.Percent(75)),
            footnotes: new FootnotePresentation(FootnotePresentationMode.Popover),
            codeBlocks: new CodeBlockPresentation(avoidSplit: false, preserveWhitespace: false));
        var document = CreateDocument(presentation);

        var result = _engine.Layout(document, new LayoutContext(1600, 1000));

        Assert.Equal(new HeadingLayoutIntent(false, false), FindNode(result, "heading-one").Intent);
        Assert.Equal(
            new FigureLayoutIntent(false, Length.Percent(75), true, PreferredPlacement.FloatEnd),
            FindNode(result, "figure-cover").Intent);
        Assert.Equal(new CodeBlockLayoutIntent(false, false), FindNode(result, "code-one").Intent);
        Assert.Equal(
            new FootnoteLayoutIntent(FootnotePresentationMode.Popover),
            FindNode(result, "footnote-one").Intent);
        Assert.Same(presentation, document.Presentation);
        Assert.Equal(PreferredPlacement.FloatEnd, presentation.Figures?.PreferredPlacement);
    }

    [Fact]
    public void Layout_ResolvesReaderPreferencesWithoutChangingDocument()
    {
        var document = CreateDocument();
        var preferences = new UserReadingPreferences(preferredBodyFont: "Reader Serif", fontScale: 1.25);

        var result = _engine.Layout(
            document,
            new LayoutContext(900, 1000, userPreferences: preferences));

        Assert.Equal("Reader Serif", result.ReadingStyle.Typography[TypographyRole.Body].FontFamily);
        Assert.Null(document.Presentation);
    }

    [Fact]
    public void Layout_ResolvesUserMarginAndRendererSafetyIntoOneEffectiveValue()
    {
        var preferences = new UserReadingPreferences(contentMargin: Length.Px(24));
        var constraints = new RendererSafetyConstraints(minimumContentMargin: Length.Px(36));

        var result = _engine.Layout(
            CreateDocument(),
            new LayoutContext(
                390,
                844,
                userPreferences: preferences,
                rendererConstraints: constraints));

        Assert.Equal(Length.Px(36), result.Profile.ContentMargin);
        Assert.Equal(result.Profile.ContentMargin, result.ReadingStyle.ContentMargin);
    }

    [Theory]
    [InlineData(ReadingMode.Paged)]
    [InlineData(ReadingMode.Print)]
    public void Layout_RejectsModesNotImplementedInThisMilestone(ReadingMode mode)
    {
        var exception = Assert.Throws<NotSupportedException>(
            () => _engine.Layout(CreateDocument(), new LayoutContext(900, 1000, readingMode: mode)));

        Assert.Contains("Flow is the only mode", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void LayoutContext_RejectsInvalidViewportDimensions(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LayoutContext(value, 800));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LayoutContext(800, value));
    }

    [Fact]
    public void Layout_RejectsInvalidSemanticDocument()
    {
        var duplicateId = new NodeId("duplicate");
        var document = new FlowDocument(
            new DocumentIdentity(new DocumentId("urn:flow:layout:invalid")),
            new DocumentMetadata("Invalid"),
            new DocumentContent(
            [
                new Paragraph(duplicateId, [new Text("One")]),
                new Paragraph(duplicateId, [new Text("Two")]),
            ]));

        var exception = Assert.Throws<ArgumentException>(
            () => _engine.Layout(document, new LayoutContext(900, 1000)));

        Assert.Contains(ValidationDiagnosticCodes.DuplicateNodeId, exception.Message, StringComparison.Ordinal);
    }

    private static LayoutNode FindNode(LayoutDocument document, string id) =>
        Flatten(document.Nodes).Single(node => node.SemanticId == new NodeId(id));

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

    private static FlowDocument CreateDocument(DocumentPresentation? presentation = null)
    {
        var caption = new Caption(new NodeId("caption-cover"), [new Text("Cover")]);
        var chapter = new Chapter(
            new NodeId("chapter-one"),
            [
                new Heading(new NodeId("heading-one"), 1, [new Text("Chapter")]),
                new Paragraph(new NodeId("paragraph-one"), [new Text("Body")]),
                new Figure(new NodeId("figure-cover"), new AssetId("cover.png"), caption),
                new CodeBlock(new NodeId("code-one"), "var answer = 42;", "csharp"),
                new Footnote(
                    new NodeId("footnote-one"),
                    [new Paragraph(new NodeId("footnote-paragraph"), [new Text("Note")])]),
            ]);

        return new FlowDocument(
            new DocumentIdentity(new DocumentId("urn:flow:layout:test"), "1"),
            new DocumentMetadata("Layout test"),
            new DocumentContent([chapter]),
            [new FlowAsset(new AssetId("cover.png"), "image/png", "cover.png", ReadOnlyMemory<byte>.Empty)],
            presentation);
    }
}
