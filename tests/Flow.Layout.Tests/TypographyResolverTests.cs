using Flow.Core;
using Flow.Documents;

namespace Flow.Layout.Tests;

public sealed class TypographyResolverTests
{
    private readonly TypographyResolver _resolver = new();

    [Fact]
    public void Resolve_InheritsUnspecifiedPropertiesFromFlowDefaults()
    {
        var declaredHeading = new TypographyStyle(fontSize: Length.Px(40));
        var presentation = Presentation(
            TypographyRole.Heading1,
            declaredHeading);

        var result = _resolver.Resolve(presentation);
        var heading = result.Typography[TypographyRole.Heading1];

        Assert.Equal(Length.Px(40), heading.FontSize);
        Assert.Equal("System Sans Serif", heading.FontFamily);
        Assert.Equal(FontWeight.Bold, heading.FontWeight);
        Assert.Equal(1.5, heading.LineHeight);
    }

    [Fact]
    public void Resolve_AppliesAllFourLayersInOrder()
    {
        var presentation = new DocumentPresentation(
            new TypographySet(
            [
                KeyValuePair.Create(
                    TypographyRole.Body,
                    new TypographyStyle(
                        fontFamily: "Author Serif",
                        fontSize: Length.Px(20),
                        lineHeight: 1.2)),
            ]),
            theme: ReadingTheme.Dark);
        var preferences = new UserReadingPreferences(
            preferredBodyFont: "Reader Serif",
            fontScale: 1.5,
            lineHeightScale: 1.5,
            theme: ReadingTheme.Sepia);
        var constraints = new RendererSafetyConstraints(
            maximumFontSize: Length.Px(24),
            maximumLineHeight: 1.6,
            requiredFontFamily: "Accessible Sans",
            requiredTheme: ReadingTheme.HighContrast);

        var result = _resolver.Resolve(presentation, preferences, constraints);
        var body = result.Typography[TypographyRole.Body];

        Assert.Equal("Accessible Sans", body.FontFamily);
        Assert.Equal(Length.Px(24), body.FontSize);
        Assert.Equal(1.6, body.LineHeight);
        Assert.Equal(ReadingTheme.HighContrast, result.Theme);
    }

    [Fact]
    public void Resolve_UserFontOverridesBodyAndHeadingFamiliesIndependently()
    {
        var preferences = new UserReadingPreferences(
            preferredBodyFont: "Reader Serif",
            preferredHeadingFont: "Reader Sans");

        var result = _resolver.Resolve(documentPresentation: null, preferences);

        Assert.Equal("Reader Serif", result.Typography[TypographyRole.Body].FontFamily);
        Assert.Equal("Reader Serif", result.Typography[TypographyRole.Caption].FontFamily);
        Assert.Equal("Reader Sans", result.Typography[TypographyRole.ChapterTitle].FontFamily);
        Assert.Equal("Reader Sans", result.Typography[TypographyRole.Heading3].FontFamily);
        Assert.Equal("System Monospace", result.Typography[TypographyRole.Code].FontFamily);
    }

    [Fact]
    public void Resolve_HeadingScaleIsAdditionalToGlobalFontScale()
    {
        var preferences = new UserReadingPreferences(fontScale: 1.25, headingScale: 1.5);

        var result = _resolver.Resolve(documentPresentation: null, preferences);

        Assert.Equal(Length.Px(22.5), result.Typography[TypographyRole.Body].FontSize);
        Assert.Equal(Length.Px(60), result.Typography[TypographyRole.Heading1].FontSize);
        Assert.Equal(Length.Px(45), result.Typography[TypographyRole.Heading3].FontSize);
        Assert.Equal(Length.Px(20), result.Typography[TypographyRole.Code].FontSize);
    }

    [Fact]
    public void Resolve_AppliesReadingSpacingMarginsLineHeightAndTheme()
    {
        var preferences = new UserReadingPreferences(
            lineHeightScale: 1.2,
            paragraphSpacing: Length.Rem(1.75),
            contentMargin: Length.Rem(2.5),
            theme: ReadingTheme.Sepia);

        var result = _resolver.Resolve(documentPresentation: null, preferences);

        Assert.Equal(1.8, result.Typography[TypographyRole.Body].LineHeight, precision: 10);
        Assert.Equal(Length.Rem(1.75), result.Typography[TypographyRole.Body].MarginAfter);
        Assert.Equal(Length.Rem(2.5), result.ContentMargin);
        Assert.Equal(ReadingTheme.Sepia, result.Theme);
    }

    [Fact]
    public void Resolve_RendererSafetyConstraintsClampAccessibilityValuesLast()
    {
        var presentation = new DocumentPresentation(
            new TypographySet(
            [
                KeyValuePair.Create(
                    TypographyRole.Body,
                    new TypographyStyle(fontSize: Length.Px(8), lineHeight: 0.8)),
                KeyValuePair.Create(
                    TypographyRole.Heading1,
                    new TypographyStyle(fontSize: Length.Px(80), lineHeight: 4)),
            ]));
        var preferences = new UserReadingPreferences(
            fontScale: 0.5,
            lineHeightScale: 0.5,
            paragraphSpacing: Length.Rem(0.25),
            contentMargin: Length.Rem(0.5));
        var constraints = new RendererSafetyConstraints(
            minimumFontSize: Length.Px(16),
            maximumFontSize: Length.Px(40),
            minimumLineHeight: 1.4,
            maximumLineHeight: 2,
            minimumParagraphSpacing: Length.Rem(1),
            minimumContentMargin: Length.Rem(2));

        var result = _resolver.Resolve(presentation, preferences, constraints);

        Assert.Equal(Length.Px(16), result.Typography[TypographyRole.Body].FontSize);
        Assert.Equal(Length.Px(40), result.Typography[TypographyRole.Heading1].FontSize);
        Assert.Equal(1.4, result.Typography[TypographyRole.Body].LineHeight);
        Assert.Equal(2, result.Typography[TypographyRole.Heading1].LineHeight);
        Assert.Equal(Length.Rem(1), result.Typography[TypographyRole.Body].MarginAfter);
        Assert.Equal(Length.Rem(2), result.ContentMargin);
    }

    [Fact]
    public void Resolve_WorksWithoutAuthorPresentation()
    {
        var result = _resolver.Resolve(
            documentPresentation: null,
            new UserReadingPreferences(preferredBodyFont: "Reader Serif"));

        Assert.Equal(Enum.GetValues<TypographyRole>().Length, result.Typography.Styles.Count);
        Assert.Equal("Reader Serif", result.Typography[TypographyRole.Body].FontFamily);
        Assert.Equal(FontWeight.Bold, result.Typography[TypographyRole.Heading1].FontWeight);
        Assert.Equal(ReadingTheme.System, result.Theme);
    }

    [Fact]
    public void Resolve_DoesNotMutateFlowDocumentOrAuthorStyles()
    {
        var authorStyle = new TypographyStyle(fontFamily: "Author Serif", fontSize: Length.Px(20));
        var presentation = Presentation(TypographyRole.Body, authorStyle);
        var document = new FlowDocument(
            new DocumentIdentity(new DocumentId("urn:flow:document:pure-resolution")),
            new DocumentMetadata("Pure resolution"),
            new DocumentContent([]),
            presentation: presentation);

        var result = _resolver.Resolve(
            document,
            new UserReadingPreferences(preferredBodyFont: "Reader Serif", fontScale: 2));

        Assert.Same(presentation, document.Presentation);
        Assert.Same(authorStyle, document.Presentation?.Typography[TypographyRole.Body]);
        Assert.Equal("Author Serif", authorStyle.FontFamily);
        Assert.Equal(Length.Px(20), authorStyle.FontSize);
        Assert.Equal("Reader Serif", result.Typography[TypographyRole.Body].FontFamily);
        Assert.Equal(Length.Px(40), result.Typography[TypographyRole.Body].FontSize);
    }

    [Fact]
    public void ResolveNode_AppliesRoleThenNodeThenReaderAndSafetyPrecedence()
    {
        var nodeId = new Flow.Core.NodeId("styled-node");
        var presentation = new DocumentPresentation(
            new TypographySet(
            [
                KeyValuePair.Create(
                    TypographyRole.Body,
                    new TypographyStyle(fontFamily: "Role Serif", fontSize: Length.Px(18))),
            ]),
            nodeTypography:
            [
                KeyValuePair.Create(
                    nodeId,
                    new TypographyStyle(
                        fontFamily: "Node Serif",
                        fontSize: Length.Px(20),
                        textDecoration: TextDecoration.Underline)),
            ]);

        var resolved = _resolver.ResolveNode(
            presentation,
            nodeId,
            TypographyRole.Body,
            new UserReadingPreferences(preferredBodyFont: "Reader Serif", fontScale: 1.5),
            new RendererSafetyConstraints(maximumFontSize: Length.Px(24)));

        Assert.NotNull(resolved);
        Assert.Equal("Reader Serif", resolved.FontFamily);
        Assert.Equal(Length.Px(24), resolved.FontSize);
        Assert.Equal(TextDecoration.Underline, resolved.TextDecoration);
        Assert.Equal("Node Serif", presentation.NodeTypography[nodeId].FontFamily);
    }

    [Fact]
    public void PreferencesAndConstraintsRejectUnsafeValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new UserReadingPreferences(fontScale: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UserReadingPreferences(headingScale: 11));
        Assert.Throws<ArgumentException>(() => new UserReadingPreferences(preferredBodyFont: "Georgia, serif"));
        Assert.Throws<ArgumentException>(() => new RendererSafetyConstraints(
            minimumFontSize: Length.Rem(1),
            maximumFontSize: Length.Px(40)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RendererSafetyConstraints(
            minimumLineHeight: 2,
            maximumLineHeight: 1.5));
    }

    [Fact]
    public void FlowDocumentDoesNotContainReaderState()
    {
        Assert.DoesNotContain(
            typeof(FlowDocument).GetProperties(),
            property => property.PropertyType == typeof(UserReadingPreferences));
        Assert.DoesNotContain(
            typeof(FlowDocument).GetConstructors().SelectMany(static constructor => constructor.GetParameters()),
            parameter => parameter.ParameterType == typeof(UserReadingPreferences));
    }

    private static DocumentPresentation Presentation(
        TypographyRole role,
        TypographyStyle style) =>
        new(new TypographySet([KeyValuePair.Create(role, style)]));
}
