using Flow.Core;
using Flow.Documents;
using Flow.Layout;

namespace Flow.Conformance.Tests;

public sealed class SemanticConformanceTests
{
    [Fact]
    public void Conformance_002_DuplicateNodeIdIsInvalid()
    {
        var repeatedId = new NodeId("p-repeated");
        var document = CreateDocument(
        [
            new Paragraph(repeatedId, [new Text("First")]),
            new Paragraph(repeatedId, [new Text("Second")]),
        ]);

        var result = new DocumentValidator().Validate(document);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            diagnostic => diagnostic.Code == ValidationDiagnosticCodes.DuplicateNodeId);
    }

    [Fact]
    public void Conformance_003_AnchorResolution()
    {
        var paragraph = new Paragraph(new NodeId("p-002"), [new Text("Target")]);
        var document = CreateDocument(
        [
            new Chapter(new NodeId("chapter-introduction"), [paragraph]),
        ]);

        var resolved = document.ResolveAnchor(DocumentAnchor.Parse("flow:chapter-introduction/p-002"));

        Assert.Same(paragraph, resolved);
    }

    [Fact]
    public void Conformance_006_HeadingCustomTypography()
    {
        var bodyStyle = new TypographyStyle(fontFamily: "Georgia", fontSize: Length.Px(18));
        var headingStyle = new TypographyStyle(
            fontFamily: "Arial",
            fontSize: Length.Px(32),
            fontWeight: FontWeight.Bold);
        var presentation = new DocumentPresentation(
            new TypographySet(
            [
                KeyValuePair.Create(TypographyRole.Body, bodyStyle),
                KeyValuePair.Create(TypographyRole.Heading1, headingStyle),
            ]));

        Assert.Same(bodyStyle, presentation.Typography[TypographyRole.Body]);
        Assert.Same(headingStyle, presentation.Typography[TypographyRole.Heading1]);
        Assert.NotEqual(
            presentation.Typography[TypographyRole.Body]?.FontSize,
            presentation.Typography[TypographyRole.Heading1]?.FontSize);
    }

    [Fact]
    public void Conformance_009_UserFontOverride()
    {
        var presentation = new DocumentPresentation(
            new TypographySet(
            [
                KeyValuePair.Create(
                    TypographyRole.Body,
                    new TypographyStyle(fontFamily: "Author Serif")),
            ]));

        var resolved = new TypographyResolver().Resolve(
            presentation,
            new UserReadingPreferences(preferredBodyFont: "Reader Serif"));

        Assert.Equal("Reader Serif", resolved.Typography[TypographyRole.Body].FontFamily);
        Assert.Equal("Author Serif", presentation.Typography[TypographyRole.Body]?.FontFamily);
    }

    [Fact]
    public void Conformance_010_FigureCaptionRelationship()
    {
        var caption = new Caption(new NodeId("caption-model"), [new Text("Flow model")]);
        var validDocument = CreateDocument(
        [new Figure(new NodeId("figure-model"), new AssetId("model.svg"), caption)],
        [new FlowAsset(new AssetId("model.svg"), "image/svg+xml", "model.svg", ReadOnlyMemory<byte>.Empty)]);
        var invalidDocument = CreateDocument([caption]);

        var validResult = new DocumentValidator().Validate(validDocument);
        var invalidResult = new DocumentValidator().Validate(invalidDocument);

        Assert.True(validResult.IsValid);
        Assert.Contains(
            invalidResult.Errors,
            diagnostic => diagnostic.Code == ValidationDiagnosticCodes.InvalidHierarchy);
    }

    [Fact]
    public void Conformance_011_FootnoteReferenceResolution()
    {
        var footnoteId = new NodeId("footnote-one");
        var validDocument = CreateDocument(
        [
            new Paragraph(new NodeId("p-reference"), [new FootnoteReference(footnoteId)]),
            new Footnote(footnoteId, [new Paragraph(new NodeId("p-note"), [new Text("Note")])]),
        ]);
        var invalidDocument = CreateDocument(
        [new Paragraph(new NodeId("p-broken-reference"), [new FootnoteReference(new NodeId("footnote-missing"))])]);

        var validResult = new DocumentValidator().Validate(validDocument);
        var invalidResult = new DocumentValidator().Validate(invalidDocument);

        Assert.True(validResult.IsValid);
        Assert.Contains(
            invalidResult.Errors,
            diagnostic => diagnostic.Code == ValidationDiagnosticCodes.UnresolvedFootnoteReference);
    }

    private static FlowDocument CreateDocument(
        IEnumerable<DocumentNode> children,
        IEnumerable<FlowAsset>? assets = null) =>
        new(
            new DocumentIdentity(new DocumentId("urn:flow:document:conformance")),
            new DocumentMetadata("Conformance document"),
            new DocumentContent(children),
            assets);
}
