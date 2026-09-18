using Flow.Core;

namespace Flow.Documents.Tests;

public sealed class DocumentValidatorTests
{
    private readonly DocumentValidator _validator = new();

    [Fact]
    public void Validate_ReportsCoverIntentThatDoesNotReferenceAFigure()
    {
        var paragraphId = new NodeId("not-a-figure");
        var document = new FlowDocument(
            new DocumentIdentity(new DocumentId("urn:flow:test:invalid-cover")),
            new DocumentMetadata("Invalid cover"),
            new DocumentContent([new Paragraph(paragraphId, [new Text("Text")])]),
            presentation: new DocumentPresentation(cover: new CoverPresentation(paragraphId)));

        var result = _validator.Validate(document);

        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Code == ValidationDiagnosticCodes.InvalidCoverFigure);
    }

    [Fact]
    public void Validate_AcceptsAConsistentDocument()
    {
        var footnoteId = new NodeId("footnote-one");
        var headingAnchor = DocumentAnchor.Parse("flow:chapter-one/heading-one");
        var document = CreateDocument(
        [
            new TableOfContents(
                new NodeId("toc-main"),
                [new Text("Contents")],
                [new TableOfContentsEntry([new Text("Chapter one")], headingAnchor, 1)]),
            new Chapter(
                new NodeId("chapter-one"),
                [
                    new Heading(new NodeId("heading-one"), 1, [new Text("Chapter one")]),
                    new Paragraph(
                        new NodeId("p-one"),
                        [
                            new Link(headingAnchor.Value, [new Text("Heading")]),
                            new FootnoteReference(footnoteId),
                        ]),
                    new Figure(
                        new NodeId("figure-one"),
                        new AssetId("figure.svg"),
                        new Caption(new NodeId("caption-one"), [new Text("Figure one")])),
                    new Footnote(footnoteId, [new Paragraph(new NodeId("p-note"), [new Text("Note")])]),
                ]),
        ],
        [new FlowAsset(new AssetId("figure.svg"), "image/svg+xml", "figure.svg", ReadOnlyMemory<byte>.Empty)]);

        var result = _validator.Validate(document);

        Assert.True(result.IsValid);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Validate_ReportsDuplicateIdsAndInvalidHierarchy()
    {
        var duplicateId = new NodeId("duplicate");
        var document = CreateDocument(
        [
            new Section(duplicateId, [new Paragraph(duplicateId, [new Text("Duplicate")])]),
            new Caption(new NodeId("orphan-caption"), [new Text("Orphan")]),
        ]);

        var result = _validator.Validate(document);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == ValidationDiagnosticCodes.DuplicateNodeId);
        Assert.Equal(
            2,
            result.Diagnostics.Count(diagnostic => diagnostic.Code == ValidationDiagnosticCodes.InvalidHierarchy));
    }

    [Fact]
    public void Validate_ReportsHeadingLevelJumpsAndMissingFigureAssets()
    {
        var document = CreateDocument(
        [
            new Chapter(
                new NodeId("chapter-one"),
                [
                    new Heading(new NodeId("heading-one"), 1, [new Text("One")]),
                    new Heading(new NodeId("heading-three"), 3, [new Text("Three")]),
                    new Figure(new NodeId("figure-missing"), new AssetId("missing.svg")),
                ]),
        ]);

        var result = _validator.Validate(document);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == ValidationDiagnosticCodes.InvalidHeadingLevel);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == ValidationDiagnosticCodes.MissingFigureAsset);
    }

    [Fact]
    public void Validate_TreatsHeadingSequencesInSeparateChaptersIndependently()
    {
        var document = CreateDocument(
        [
            new Chapter(
                new NodeId("chapter-one"),
                [new Heading(new NodeId("heading-one"), 1, [new Text("One")])]),
            new Chapter(
                new NodeId("chapter-two"),
                [new Heading(new NodeId("heading-three"), 3, [new Text("Independent")])]),
        ]);

        var result = _validator.Validate(document);

        Assert.DoesNotContain(
            result.Diagnostics,
            diagnostic => diagnostic.Code == ValidationDiagnosticCodes.InvalidHeadingLevel);
    }

    [Fact]
    public void Validate_ReportsInvalidAndUnresolvedFlowLinks()
    {
        var document = CreateDocument(
        [
            new Chapter(
                new NodeId("chapter-one"),
                [
                    new Paragraph(
                        new NodeId("p-links"),
                        [
                            new Link("FLOW:chapter-one", [new Text("Wrong case")]),
                            new Link("flow:chapter-one/missing", [new Text("Missing")]),
                        ]),
                ]),
        ]);

        var result = _validator.Validate(document);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == ValidationDiagnosticCodes.InvalidAnchor);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == ValidationDiagnosticCodes.UnresolvedAnchor);
    }

    [Fact]
    public void Validate_ReportsWrongFootnoteAndTableOfContentsTargets()
    {
        var paragraphId = new NodeId("p-target");
        var document = CreateDocument(
        [
            new TableOfContents(
                new NodeId("toc-main"),
                [new Text("Contents")],
                [new TableOfContentsEntry([new Text("Paragraph")], DocumentAnchor.Parse("flow:chapter-one/p-target"), 1)]),
            new Chapter(
                new NodeId("chapter-one"),
                [
                    new Paragraph(paragraphId, [new FootnoteReference(paragraphId)]),
                ]),
        ]);

        var result = _validator.Validate(document);

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == ValidationDiagnosticCodes.UnresolvedFootnoteReference);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == ValidationDiagnosticCodes.InvalidTableOfContentsTarget);
    }

    private static FlowDocument CreateDocument(
        IEnumerable<DocumentNode> children,
        IEnumerable<FlowAsset>? assets = null) =>
        new(
            new DocumentIdentity(new DocumentId("urn:flow:document:validation-tests")),
            new DocumentMetadata("Validation tests"),
            new DocumentContent(children),
            assets);
}
