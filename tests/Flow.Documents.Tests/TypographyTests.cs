using Flow.Core;

namespace Flow.Documents.Tests;

public sealed class TypographyTests
{
    [Fact]
    public void Length_PreservesTypedUnitsAndValueEquality()
    {
        Assert.Equal(Length.Px(16), Length.Px(16));
        Assert.NotEqual(Length.Px(16), Length.Rem(16));
        Assert.Equal(LengthUnit.Pixel, Length.Px(16).Unit);
        Assert.Equal(LengthUnit.RootEm, Length.Rem(1.25).Unit);
        Assert.Equal(LengthUnit.Em, Length.Em(-0.02).Unit);
        Assert.Equal(LengthUnit.Percent, Length.Percent(100).Unit);
        Assert.Throws<ArgumentOutOfRangeException>(() => Length.Px(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => Length.Rem(double.PositiveInfinity));
    }

    [Fact]
    public void TypographyRole_ContainsEveryRequiredIndependentRole()
    {
        TypographyRole[] expected =
        [
            TypographyRole.Body,
            TypographyRole.ChapterTitle,
            TypographyRole.Heading1,
            TypographyRole.Heading2,
            TypographyRole.Heading3,
            TypographyRole.Heading4,
            TypographyRole.Heading5,
            TypographyRole.Heading6,
            TypographyRole.Subtitle,
            TypographyRole.TableOfContentsTitle,
            TypographyRole.TableOfContentsLevel1,
            TypographyRole.TableOfContentsLevel2,
            TypographyRole.TableOfContentsLevel3,
            TypographyRole.Caption,
            TypographyRole.Footnote,
            TypographyRole.BlockQuote,
            TypographyRole.Code,
        ];

        Assert.Equal(expected, Enum.GetValues<TypographyRole>());
    }

    [Fact]
    public void TypographyStyle_StoresTypedAuthorIntent()
    {
        var style = new TypographyStyle(
            fontFamily: "Literata",
            fontSize: Length.Px(34),
            fontWeight: FontWeight.Bold,
            fontStyle: FontStyle.Italic,
            lineHeight: 1.2,
            letterSpacing: Length.Em(0.01),
            textAlignment: TextAlignment.Center,
            textTransform: TextTransform.Capitalize,
            marginBefore: Length.Rem(2),
            marginAfter: Length.Rem(1.25),
            indent: Length.Rem(0.5));

        Assert.Equal("Literata", style.FontFamily);
        Assert.Equal(Length.Px(34), style.FontSize);
        Assert.Equal(FontWeight.Bold, style.FontWeight);
        Assert.Equal(FontStyle.Italic, style.FontStyle);
        Assert.Equal(1.2, style.LineHeight);
        Assert.Equal(TextAlignment.Center, style.TextAlignment);
        Assert.Equal(TextTransform.Capitalize, style.TextTransform);
        Assert.False(style.IsEmpty);
        Assert.True(new TypographyStyle().IsEmpty);
    }

    [Fact]
    public void TypographyStyle_RejectsInvalidValuesAndCssExpressions()
    {
        Assert.Throws<ArgumentException>(() => new TypographyStyle(fontFamily: "Georgia, serif"));
        Assert.Throws<ArgumentException>(() => new TypographyStyle(fontFamily: "font; color:red"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TypographyStyle(fontSize: Length.Px(0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TypographyStyle(lineHeight: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TypographyStyle(lineHeight: double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TypographyStyle(fontWeight: (FontWeight)450));
    }

    [Fact]
    public void TypographySet_KeepsRolesIndependentAndCopiesItsInput()
    {
        var body = new TypographyStyle(fontFamily: "Georgia", fontSize: Length.Px(18));
        var heading = new TypographyStyle(fontFamily: "Arial", fontSize: Length.Px(32));
        var source = new List<KeyValuePair<TypographyRole, TypographyStyle>>
        {
            KeyValuePair.Create(TypographyRole.Body, body),
            KeyValuePair.Create(TypographyRole.Heading1, heading),
        };

        var set = new TypographySet(source);
        source.Clear();

        Assert.Same(body, set[TypographyRole.Body]);
        Assert.Same(heading, set[TypographyRole.Heading1]);
        Assert.NotEqual(set[TypographyRole.Body], set[TypographyRole.Heading1]);
        Assert.Null(set[TypographyRole.Caption]);
        Assert.Equal(2, set.Styles.Count);
    }

    [Fact]
    public void TypographySet_RejectsDuplicateRoles()
    {
        var style = new TypographyStyle();

        Assert.Throws<ArgumentException>(() => new TypographySet(
        [
            KeyValuePair.Create(TypographyRole.Body, style),
            KeyValuePair.Create(TypographyRole.Body, style),
        ]));
    }

    [Fact]
    public void DocumentPresentation_ComposesEveryTypedIntent()
    {
        var typography = new TypographySet(
        [
            KeyValuePair.Create(
                TypographyRole.Body,
                new TypographyStyle(fontFamily: "Georgia", fontSize: Length.Px(18))),
        ]);
        var presentation = new DocumentPresentation(
            typography,
            headings: new HeadingPresentation(keepWithNext: true, avoidBreakAfter: true),
            paragraphs: new ParagraphPresentation(keepTogether: false, keepWithNext: false),
            figures: new FigurePresentation(
                FigureImportance.Essential,
                keepWithCaption: true,
                PreferredPlacement.Block,
                Length.Percent(100)),
            captions: new CaptionPresentation(keepWithFigure: true),
            footnotes: new FootnotePresentation(FootnotePresentationMode.EndOfSection),
            codeBlocks: new CodeBlockPresentation(avoidSplit: true, preserveWhitespace: true),
            tableOfContents: new TableOfContentsPresentation(
                generateFromDocumentStructure: true,
                TableOfContentsLeaderStyle.Dots),
            cover: new CoverPresentation(new NodeId("cover-figure")));

        Assert.Same(typography, presentation.Typography);
        Assert.True(presentation.Headings?.KeepWithNext);
        Assert.False(presentation.Paragraphs?.KeepTogether);
        Assert.Equal(FigureImportance.Essential, presentation.Figures?.Importance);
        Assert.Equal(Length.Percent(100), presentation.Figures?.MaximumWidth);
        Assert.True(presentation.Captions?.KeepWithFigure);
        Assert.Equal(FootnotePresentationMode.EndOfSection, presentation.Footnotes?.PreferredPresentation);
        Assert.True(presentation.CodeBlocks?.PreserveWhitespace);
        Assert.Equal(TableOfContentsLeaderStyle.Dots, presentation.TableOfContents?.LeaderStyle);
        Assert.Equal(new NodeId("cover-figure"), presentation.Cover?.FigureId);
    }

    [Fact]
    public void Presentation_IsOptionalAndContainsNoRenderedCoordinates()
    {
        var document = new FlowDocument(
            new DocumentIdentity(new DocumentId("urn:flow:document:no-presentation")),
            new DocumentMetadata("No presentation"),
            new DocumentContent([]));
        var presentationTypes = new[]
        {
            typeof(DocumentPresentation),
            typeof(CoverPresentation),
            typeof(HeadingPresentation),
            typeof(ParagraphPresentation),
            typeof(FigurePresentation),
            typeof(CaptionPresentation),
            typeof(FootnotePresentation),
            typeof(CodeBlockPresentation),
            typeof(TableOfContentsPresentation),
        };
        var forbiddenPropertyNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "X", "Y", "Top", "Left", "Right", "Bottom", "PageNumber",
            "ViewportWidth", "ViewportHeight", "RenderedWidth", "RenderedHeight",
        };

        Assert.Null(document.Presentation);
        Assert.DoesNotContain(
            presentationTypes.SelectMany(static type => type.GetProperties()),
            property => forbiddenPropertyNames.Contains(property.Name));
    }
}
