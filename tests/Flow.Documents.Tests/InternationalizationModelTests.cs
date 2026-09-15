using Flow.Core;

namespace Flow.Documents.Tests;

public sealed class InternationalizationModelTests
{
    [Theory]
    [InlineData("pt-pt", "pt-PT")]
    [InlineData("EN-us", "en-US")]
    [InlineData("ar", "ar")]
    [InlineData("ja-jpan-jp", "ja-Jpan-JP")]
    public void LanguageTag_NormalizesStructurallyValidBcp47Values(string source, string expected)
    {
        var tag = new LanguageTag(source);

        Assert.Equal(expected, tag.Value);
        Assert.Equal(expected, tag.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("en_US")]
    [InlineData("12")]
    [InlineData("en--US")]
    public void LanguageTag_RejectsInvalidValues(string value)
    {
        Assert.False(LanguageTag.TryParse(value, out _));
        Assert.Throws<ArgumentException>(() => new LanguageTag(value));
    }

    [Fact]
    public void InternationalizationNodes_CopyCollectionsAndEnforceOverrideDirection()
    {
        var source = new List<InlineNode> { new Text("Hello") };
        var language = new LanguageSpan(new LanguageTag("en"), source);
        var direction = new BidirectionalSpan(TextDirection.RightToLeft, BidirectionalMode.Isolation, source);
        var ruby = new Ruby(
        [
            new Text("漢"),
            new RubyFallbackParenthesis([new Text("(")]),
            new RubyAnnotation([new Text("かん")]),
            new RubyFallbackParenthesis([new Text(")")]),
        ]);

        source.Add(new Text("changed"));

        Assert.Single(language.Children);
        Assert.Single(direction.Children);
        Assert.Equal(4, ruby.Children.Length);
        Assert.Throws<ArgumentException>(() => new BidirectionalSpan(
            TextDirection.Auto,
            BidirectionalMode.Override,
            [new Text("invalid")]));
    }

    [Fact]
    public async Task JsonRoundTrip_PreservesLanguageDirectionAndRuby()
    {
        var document = CreateDocument(
        [
            new LanguageSpan(
                new LanguageTag("pt-PT"),
                [
                    new Text("Olá "),
                    new LanguageSpan(new LanguageTag("en"), [new Text("hello")]),
                    new BidirectionalSpan(
                        TextDirection.RightToLeft,
                        BidirectionalMode.Isolation,
                        [new Text("مرحبا")]),
                    new Ruby([new Text("本"), new RubyAnnotation([new Text("ほん")])]),
                ]),
        ]);
        var serializer = new FlowJsonDocumentSerializer();
        await using var json = new MemoryStream();

        await serializer.SerializeAsync(document, json);
        json.Position = 0;
        var restored = await serializer.DeserializeAsync(json);

        var content = Assert.Single(restored.Index.Locations.Select(static item => item.Node).OfType<Paragraph>()).Content;
        var portuguese = Assert.IsType<LanguageSpan>(Assert.Single(content));
        Assert.Equal("pt-PT", portuguese.Language.Value);
        Assert.Contains(portuguese.Children, static node => node is LanguageSpan { Language.Value: "en" });
        Assert.Contains(portuguese.Children, static node => node is BidirectionalSpan
        {
            Direction: TextDirection.RightToLeft,
            Mode: BidirectionalMode.Isolation,
        });
        Assert.Contains(portuguese.Children, static node => node is Ruby);
        Assert.True(new DocumentValidator().Validate(restored).IsValid);
    }

    [Fact]
    public void Validator_RejectsRubyPartsOutsideRubyAndRubyWithoutAnnotation()
    {
        var document = CreateDocument(
        [
            new RubyAnnotation([new Text("orphan")]),
            new Ruby([new Text("base only")]),
        ]);

        var result = new DocumentValidator().Validate(document);

        Assert.False(result.IsValid);
        Assert.Equal(
            2,
            result.Diagnostics.Count(static item => item.Code == ValidationDiagnosticCodes.InvalidRubyStructure));
    }

    private static FlowDocument CreateDocument(IEnumerable<InlineNode> content) => new(
        new DocumentIdentity(new DocumentId("urn:flow:test:internationalization")),
        new DocumentMetadata("Internationalization", "pt-PT"),
        new DocumentContent(
        [
            new Chapter(
                new NodeId("chapter"),
                [new Paragraph(new NodeId("paragraph"), content)]),
        ]));
}
