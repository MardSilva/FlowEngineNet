using System.Text;
using Flow.Core;
using Flow.Documents;

namespace Flow.Cli;

public static class SampleBookFactory
{
    public const string DocumentUrn = "urn:flow:sample:the-flow-experiment";

    public static FlowDocument Create()
    {
        var experimentFigure = new FlowAsset(
            new AssetId("flow-experiment.svg"),
            "image/svg+xml",
            "flow-experiment.svg",
            Encoding.UTF8.GetBytes(CreateFigureSvg()));

        return new FlowDocument(
            new DocumentIdentity(new DocumentId(DocumentUrn), version: "1.0"),
            new DocumentMetadata(
                "The Flow Experiment",
                language: "en",
                authors: ["Flow Contributors"],
                subtitle: "A document that changes shape without changing identity",
                description: "A five-chapter reference book exercising the Flow 0.1 semantic model."),
            new DocumentContent(
            [
                CreateTableOfContents(),
                CreateIdentityChapter(),
                CreateStructureChapter(),
                CreateReadingChapter(),
                CreateFigureChapter(),
                CreateVerificationChapter(),
            ]),
            [experimentFigure],
            CreatePresentation());
    }

    private static TableOfContents CreateTableOfContents() =>
        new(
            new NodeId("table-of-contents"),
            [new Text("Contents")],
            [
                TocEntry("Identity Before Appearance", "flow:chapter-identity/identity-title"),
                TocEntry("Structure Carries Meaning", "flow:chapter-structure/structure-title"),
                TocEntry("One Book, Many Readers", "flow:chapter-reading/reading-title"),
                TocEntry("Figures That Adapt", "flow:chapter-figures/figures-title"),
                TocEntry("Evidence, Not Promises", "flow:chapter-verification/verification-title"),
            ]);

    private static TableOfContentsEntry TocEntry(string label, string target) =>
        new([new Text(label)], DocumentAnchor.Parse(target), level: 1);

    private static Chapter CreateIdentityChapter()
    {
        var footnoteId = new NodeId("identity-footnote");
        return new Chapter(
            new NodeId("chapter-identity"),
            [
                new Heading(new NodeId("identity-title"), 1, [new Text("Identity Before Appearance")]),
                new Paragraph(
                    new NodeId("identity-opening"),
                    [
                        new Text("Flow begins with a "),
                        new Strong([new Text("stable document identity")]),
                        new Text(", then keeps that identity separate from screens, fonts, and pages."),
                    ]),
                new Paragraph(
                    new NodeId("identity-principle"),
                    [
                        new Emphasis([new Text("Document is not layout")]),
                        new Text(" is the experiment's central constraint. A narrow phone and a wide desktop may disagree about columns, but not about what the document says."),
                        new FootnoteReference(footnoteId),
                    ]),
                new Paragraph(
                    new NodeId("identity-link"),
                    [
                        new Text("Jump ahead to "),
                        new Link("flow:chapter-verification/verification-title", [new Text("the verification chapter")]),
                        new Text(" to see how the claim is tested."),
                    ]),
                new Footnote(
                    footnoteId,
                    [
                        new Paragraph(
                            new NodeId("identity-footnote-text"),
                            [new Text("Viewport, layout, theme, and renderer state are excluded from the canonical hash.")]),
                    ]),
            ]);
    }

    private static Chapter CreateStructureChapter() =>
        new(
            new NodeId("chapter-structure"),
            [
                new Heading(new NodeId("structure-title"), 1, [new Text("Structure Carries Meaning")]),
                new Paragraph(
                    new NodeId("structure-opening"),
                    [
                        new Text("A Flow document records chapters, sections, lists, quotations, figures, notes, and links as semantic nodes rather than visual coordinates."),
                    ]),
                new Section(
                    new NodeId("structure-checklist"),
                    [
                        new Heading(new NodeId("structure-checklist-title"), 2, [new Text("What the model preserves")]),
                        new UnorderedList(
                            new NodeId("structure-unordered-list"),
                            [
                                Item("structure-item-identity", "Stable IDs and anchors"),
                                Item("structure-item-order", "Canonical reading order"),
                                Item("structure-item-assets", "Explicit asset relationships"),
                            ]),
                        new OrderedList(
                            new NodeId("structure-ordered-list"),
                            [
                                Item("structure-step-model", "Create the semantic document"),
                                Item("structure-step-layout", "Resolve layout for a reading context"),
                                Item("structure-step-render", "Render through an adapter"),
                            ]),
                    ]),
            ]);

    private static ListItem Item(string id, string value) =>
        new(
            new NodeId(id),
            [new Paragraph(new NodeId($"{id}-text"), [new Text(value)])]);

    private static Chapter CreateReadingChapter() =>
        new(
            new NodeId("chapter-reading"),
            [
                new Heading(new NodeId("reading-title"), 1, [new Text("One Book, Many Readers")]),
                new Paragraph(
                    new NodeId("reading-opening"),
                    [
                        new Text("Authors can suggest typography while readers retain control over body fonts, heading fonts, scale, spacing, margins, and theme."),
                    ]),
                new BlockQuote(
                    new NodeId("reading-quote"),
                    [
                        new Paragraph(
                            new NodeId("reading-quote-text"),
                            [new Text("Adaptation is useful only when the document's meaning survives the adaptation.")]),
                    ]),
                new Paragraph(
                    new NodeId("reading-inline"),
                    [
                        new Text("This sentence demonstrates "),
                        new Underline([new Text("underline")]),
                        new Text(", "),
                        new Strikethrough([new Text("discarded wording")]),
                        new Text(", and the inline token "),
                        new InlineCode("ReadingMode.Flow"),
                        new Text(" without storing CSS in the document."),
                    ]),
            ]);

    private static Chapter CreateFigureChapter() =>
        new(
            new NodeId("chapter-figures"),
            [
                new Heading(new NodeId("figures-title"), 1, [new Text("Figures That Adapt")]),
                new Paragraph(
                    new NodeId("figures-opening"),
                    [new Text("A figure points to a canonical asset while layout decides how it fits the available reading surface.")]),
                new Figure(
                    new NodeId("adaptive-figure"),
                    new AssetId("flow-experiment.svg"),
                    new Caption(
                        new NodeId("adaptive-figure-caption"),
                        [new Text("The same semantic document flowing through mobile and desktop layouts.")]),
                    "One Flow document branching into mobile and desktop reading layouts"),
                new Paragraph(
                    new NodeId("figures-external-link"),
                    [
                        new Text("The standalone renderer uses standard "),
                        new Link("https://html.spec.whatwg.org/", [new Text("HTML5")]),
                        new Text(" rather than inventing a new display language."),
                    ]),
            ]);

    private static Chapter CreateVerificationChapter() =>
        new(
            new NodeId("chapter-verification"),
            [
                new Heading(new NodeId("verification-title"), 1, [new Text("Evidence, Not Promises")]),
                new Paragraph(
                    new NodeId("verification-opening"),
                    [
                        new Text("The experiment is successful only when different presentations preserve the same IDs, anchors, and canonical hash."),
                    ]),
                new CodeBlock(
                    new NodeId("verification-code"),
                    "var hash = integrity.ComputeHash(document);\nConsole.WriteLine(hash.Hash);",
                    "csharp"),
                new Paragraph(
                    new NodeId("verification-closing"),
                    [
                        new Strong([new Text("Change presentation")]),
                        new Text(", and the hash stays fixed. "),
                        new Strong([new Text("Change content")]),
                        new Text(", and the hash changes. That is a claim the test suite can falsify."),
                    ]),
            ]);

    private static DocumentPresentation CreatePresentation() =>
        new(
            new TypographySet(
            [
                Style(TypographyRole.Body, "Source Serif 4", 19, lineHeight: 1.65),
                Style(TypographyRole.ChapterTitle, "Source Sans 3", 42, FontWeight.Bold, 1.15),
                Style(TypographyRole.Heading1, "Source Sans 3", 34, FontWeight.Bold, 1.2),
                Style(TypographyRole.Heading2, "Source Sans 3", 27, FontWeight.SemiBold, 1.25),
                Style(TypographyRole.TableOfContentsTitle, "Source Sans 3", 30, FontWeight.Bold, 1.2),
                Style(TypographyRole.TableOfContentsLevel1, "Source Sans 3", 18, FontWeight.Medium, 1.5),
                Style(TypographyRole.Caption, "Source Sans 3", 14, FontWeight.Normal, 1.4),
                Style(TypographyRole.Footnote, "Source Serif 4", 13, FontWeight.Normal, 1.45),
                Style(TypographyRole.BlockQuote, "Source Serif 4", 20, FontWeight.Normal, 1.6),
                Style(TypographyRole.Code, "System Monospace", 15, FontWeight.Normal, 1.45),
            ]),
            headings: new HeadingPresentation(keepWithNext: true, avoidBreakAfter: true),
            paragraphs: new ParagraphPresentation(keepTogether: false),
            figures: new FigurePresentation(
                importance: FigureImportance.Essential,
                keepWithCaption: true,
                preferredPlacement: PreferredPlacement.Block,
                maximumWidth: Length.Percent(100)),
            captions: new CaptionPresentation(keepWithFigure: true),
            footnotes: new FootnotePresentation(FootnotePresentationMode.EndOfSection),
            codeBlocks: new CodeBlockPresentation(avoidSplit: true, preserveWhitespace: true),
            tableOfContents: new TableOfContentsPresentation(
                generateFromDocumentStructure: false,
                leaderStyle: TableOfContentsLeaderStyle.Dots),
            theme: ReadingTheme.Sepia);

    private static KeyValuePair<TypographyRole, TypographyStyle> Style(
        TypographyRole role,
        string fontFamily,
        double fontSize,
        FontWeight fontWeight = FontWeight.Normal,
        double lineHeight = 1.5) =>
        KeyValuePair.Create(
            role,
            new TypographyStyle(
                fontFamily: fontFamily,
                fontSize: Length.Px(fontSize),
                fontWeight: fontWeight,
                lineHeight: lineHeight));

    private static string CreateFigureSvg() =>
        """
        <svg xmlns="http://www.w3.org/2000/svg" width="960" height="480" viewBox="0 0 960 480" role="img" aria-label="Flow document adapting to two viewports">
          <rect width="960" height="480" rx="32" fill="#f4ecd8"/>
          <rect x="72" y="88" width="240" height="304" rx="18" fill="#fff" stroke="#3b2f24" stroke-width="8"/>
          <rect x="648" y="88" width="240" height="304" rx="18" fill="#fff" stroke="#3b2f24" stroke-width="8"/>
          <path d="M312 240H648" stroke="#a65032" stroke-width="12"/>
          <path d="M594 198L648 240 594 282" fill="none" stroke="#a65032" stroke-width="12"/>
          <g stroke="#8b7965" stroke-width="8">
            <path d="M112 144H272M112 184H272M112 224H272M112 264H272M112 304H232"/>
            <path d="M688 144H848M688 184H848M688 224H848M688 264H848M688 304H808"/>
          </g>
          <text x="192" y="60" text-anchor="middle" font-family="sans-serif" font-size="28" fill="#3b2f24">Mobile</text>
          <text x="768" y="60" text-anchor="middle" font-family="sans-serif" font-size="28" fill="#3b2f24">Desktop</text>
          <text x="480" y="438" text-anchor="middle" font-family="sans-serif" font-size="28" font-weight="bold" fill="#a65032">same identity · same content</text>
        </svg>
        """;
}
