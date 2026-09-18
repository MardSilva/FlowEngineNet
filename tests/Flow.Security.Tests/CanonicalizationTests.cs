using System.Security.Cryptography;
using Flow.Core;
using Flow.Documents;
using Flow.Layout;

namespace Flow.Security.Tests;

public sealed class CanonicalizationTests
{
    private readonly FlowDocumentCanonicalizer _canonicalizer = new();

    [Fact]
    public void Canonicalize_SameDocumentAlwaysProducesSameBytes()
    {
        var document = CreateDocument("Canonical content");

        var first = _canonicalizer.Canonicalize(document);
        var second = _canonicalizer.Canonicalize(document);

        Assert.Equal(first, second);
        Assert.Equal(FlowDocumentCanonicalizer.Version, _canonicalizer.CanonicalizationVersion);
    }

    [Fact]
    public void Canonicalize_SortsAssetsByStableId()
    {
        var first = CreateDocument(
            "Assets",
            assets:
            [
                Asset("z.bin", [3]),
                Asset("a.bin", [1]),
            ]);
        var second = CreateDocument(
            "Assets",
            assets:
            [
                Asset("a.bin", [1]),
                Asset("z.bin", [3]),
            ]);

        Assert.Equal(_canonicalizer.Canonicalize(first), _canonicalizer.Canonicalize(second));
    }

    [Fact]
    public void ComputeHash_ContentChangeChangesHash()
    {
        var service = CreateIntegrityService();

        var original = service.ComputeHash(CreateDocument("The amount is €1,000."));
        var changed = service.ComputeHash(CreateDocument("The amount is €10,000."));

        Assert.NotEqual(original.Hash, changed.Hash);
    }

    [Fact]
    public void ComputeHash_FootnoteReferenceLabelChangeChangesHash()
    {
        var service = CreateIntegrityService();

        var first = service.ComputeHash(CreateFootnoteDocument("1"));
        var changed = service.ComputeHash(CreateFootnoteDocument("a"));

        Assert.NotEqual(first.Hash, changed.Hash);
    }

    [Fact]
    public void ComputeHash_TableSemanticsChangeHash()
    {
        var service = CreateIntegrityService();

        var first = service.ComputeHash(CreateTableDocument(columnSpan: 1));
        var changed = service.ComputeHash(CreateTableDocument(columnSpan: 2));

        Assert.NotEqual(first.Hash, changed.Hash);
    }

    [Fact]
    public void ComputeHash_MathStructureChangeChangesHash()
    {
        FlowDocument MathDocument(string operatorName) => new(
            new DocumentIdentity(new DocumentId("urn:flow:test:canonical-math")),
            new DocumentMetadata("Math"),
            new DocumentContent(
            [
                new MathExpression(
                    new NodeId("equation"),
                    new MathElement("math",
                    [
                        new MathElement(operatorName, [new MathText("1"), new MathText("2")]),
                    ])),
            ]));

        var service = CreateIntegrityService();

        Assert.NotEqual(
            service.ComputeHash(MathDocument("mfrac")),
            service.ComputeHash(MathDocument("mrow")));
    }

    [Fact]
    public void ComputeHash_InternationalizationSemanticsChangeHash()
    {
        FlowDocument InternationalDocument(InlineNode content) => new(
            new DocumentIdentity(new DocumentId("urn:flow:test:canonical-i18n")),
            new DocumentMetadata("Internationalization"),
            new DocumentContent(
            [
                new Paragraph(new NodeId("paragraph"), [content]),
            ]));
        var service = CreateIntegrityService();
        var english = InternationalDocument(new LanguageSpan(new LanguageTag("en"), [new Text("Flow")]));
        var portuguese = InternationalDocument(new LanguageSpan(new LanguageTag("pt"), [new Text("Flow")]));
        var rtl = InternationalDocument(new BidirectionalSpan(
            TextDirection.RightToLeft,
            BidirectionalMode.Isolation,
            [new Text("Flow")]));
        var ruby = InternationalDocument(new Ruby([new Text("本"), new RubyAnnotation([new Text("ほん")])]));

        Assert.NotEqual(service.ComputeHash(english), service.ComputeHash(portuguese));
        Assert.NotEqual(service.ComputeHash(english), service.ComputeHash(rtl));
        Assert.NotEqual(service.ComputeHash(english), service.ComputeHash(ruby));
    }

    [Fact]
    public void ComputeHash_CanonicalMetadataAndAssetBytesChangeHash()
    {
        var service = CreateIntegrityService();
        var original = CreateDocument("Content", title: "Original", assets: [Asset("asset.bin", [1])]);
        var metadataChanged = CreateDocument("Content", title: "Changed", assets: [Asset("asset.bin", [1])]);
        var assetChanged = CreateDocument("Content", title: "Original", assets: [Asset("asset.bin", [2])]);

        var originalHash = service.ComputeHash(original).Hash;

        Assert.NotEqual(originalHash, service.ComputeHash(metadataChanged).Hash);
        Assert.NotEqual(originalHash, service.ComputeHash(assetChanged).Hash);
    }

    [Fact]
    public void ComputeHash_PresentationThemeAndExistingIntegrityDoNotChangeHash()
    {
        var firstPresentation = new DocumentPresentation(
            new TypographySet(
            [
                KeyValuePair.Create(
                    TypographyRole.Body,
                    new TypographyStyle(fontFamily: "Georgia", fontSize: Length.Px(16))),
            ]),
            theme: ReadingTheme.Light);
        var secondPresentation = new DocumentPresentation(
            new TypographySet(
            [
                KeyValuePair.Create(
                    TypographyRole.Body,
                    new TypographyStyle(fontFamily: "Arial", fontSize: Length.Px(28))),
            ]),
            theme: ReadingTheme.Dark);
        var first = CreateDocument(
            "Content",
            firstPresentation,
            integrity: new DocumentIntegrity("SHA-256", "OLD", "old-c14n"));
        var second = CreateDocument(
            "Content",
            secondPresentation,
            integrity: new DocumentIntegrity("SHA-256", "OTHER", "other-c14n"));

        var service = CreateIntegrityService();

        Assert.Equal(service.ComputeHash(first), service.ComputeHash(second));
    }

    [Fact]
    public void ComputeHash_UserPreferencesAndResolvedStylesRemainOutsideDocumentHash()
    {
        var document = CreateDocument("Reader state");
        var service = CreateIntegrityService();
        var before = service.ComputeHash(document);

        var resolved = new TypographyResolver().Resolve(
            document,
            new UserReadingPreferences(
                preferredBodyFont: "Reader Serif",
                fontScale: 1.8,
                theme: ReadingTheme.Sepia));
        var after = service.ComputeHash(document);

        Assert.Equal(before, after);
        Assert.Equal("Reader Serif", resolved.Typography[TypographyRole.Body].FontFamily);
        Assert.Null(document.Presentation);
    }

    [Fact]
    public void ComputeHash_UsesStandardSha256AndReportsProfile()
    {
        var document = CreateDocument("Hash profile");
        var canonicalBytes = _canonicalizer.Canonicalize(document);

        var result = CreateIntegrityService().ComputeHash(document);

        Assert.Equal("SHA-256", result.Algorithm);
        Assert.Equal(64, result.Hash.Length);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(canonicalBytes)), result.Hash);
        Assert.Equal("flow-c14n-0.2", result.CanonicalizationVersion);
    }

    [Fact]
    public void ComputeHash_FigureLinkChangesCurrentProfileButLegacyProfileRemainsStable()
    {
        var target = new Heading(new NodeId("target"), 1, [new Text("Target")]);
        FlowDocument Document(FigureLink? link) => new(
            new DocumentIdentity(new DocumentId("urn:flow:test:figure-link")),
            new DocumentMetadata("Figure link"),
            new DocumentContent(
            [
                new Chapter(
                    new NodeId("chapter"),
                    [
                        target,
                        new Figure(new NodeId("figure"), new AssetId("image"), alternativeText: "Image", link: link),
                    ]),
            ]),
            [Asset("image", [1])]);
        var withoutLink = Document(null);
        var withLink = Document(FigureLink.Internal(DocumentAnchor.Create([target.Id])));

        Assert.NotEqual(_canonicalizer.Canonicalize(withoutLink), _canonicalizer.Canonicalize(withLink));

        var legacy = new FlowDocumentCanonicalizerV01();
        Assert.Equal("flow-c14n-0.1", legacy.CanonicalizationVersion);
        Assert.Equal(legacy.Canonicalize(withoutLink), legacy.Canonicalize(withLink));
    }

    private Sha256DocumentIntegrityService CreateIntegrityService() => new(_canonicalizer);

    private static FlowDocument CreateDocument(
        string text,
        DocumentPresentation? presentation = null,
        string title = "Canonical document",
        IEnumerable<FlowAsset>? assets = null,
        DocumentIntegrity? integrity = null) =>
        new(
            new DocumentIdentity(new DocumentId("urn:flow:document:canonicalization-tests"), "1"),
            new DocumentMetadata(title, "en", ["Flow contributors"]),
            new DocumentContent(
            [
                new Chapter(
                    new NodeId("chapter-one"),
                    [new Paragraph(new NodeId("p-one"), [new Text(text)])]),
            ]),
            assets,
            presentation,
            integrity);

    private static FlowAsset Asset(string id, byte[] bytes) =>
        new(new AssetId(id), "application/octet-stream", id, bytes);

    private static FlowDocument CreateFootnoteDocument(string label)
    {
        var footnoteId = new NodeId("footnote-one");
        return new FlowDocument(
            new DocumentIdentity(new DocumentId("urn:flow:document:canonical-footnote")),
            new DocumentMetadata("Canonical footnote"),
            new DocumentContent(
            [
                new Chapter(
                    new NodeId("chapter-one"),
                    [
                        new Paragraph(
                            new NodeId("paragraph-one"),
                            [new FootnoteReference(footnoteId, [new Text(label)])]),
                        new Footnote(
                            footnoteId,
                            [new Paragraph(new NodeId("footnote-text"), [new Text("Note")])]),
                    ]),
            ]));
    }

    private static FlowDocument CreateTableDocument(int columnSpan)
    {
        var headerId = new NodeId("header-one");
        return new FlowDocument(
            new DocumentIdentity(new DocumentId("urn:flow:document:canonical-table")),
            new DocumentMetadata("Canonical table"),
            new DocumentContent(
            [
                new Chapter(
                    new NodeId("chapter-one"),
                    [
                        new Table(
                            new NodeId("table-one"),
                            [
                                new TableBody(
                                    new NodeId("body-one"),
                                    [
                                        new TableRow(
                                            new NodeId("row-one"),
                                            [
                                                new TableHeaderCell(
                                                    headerId,
                                                    [new Paragraph(new NodeId("header-text"), [new Text("Header")])],
                                                    columnSpan: columnSpan,
                                                    scope: TableHeaderScope.Column),
                                                new TableCell(
                                                    new NodeId("cell-one"),
                                                    [],
                                                    headers: [headerId]),
                                            ]),
                                    ]),
                            ]),
                    ]),
            ]));
    }
}
