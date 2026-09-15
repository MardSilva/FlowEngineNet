using Flow.Core;

namespace Flow.Documents.Tests;

public sealed class MathModelTests
{
    [Fact]
    public void MathTree_IsImmutableAndRestrictsElementsAndAttributes()
    {
        var children = new List<MathNode> { new MathText("x") };
        var attributes = new Dictionary<string, string> { ["mathvariant"] = "italic" };
        var identifier = new MathElement("mi", children, attributes);
        var root = new MathElement("math", [identifier]);
        var expression = new MathExpression(new NodeId("equation"), root, "x");

        children.Add(new MathText("changed"));
        attributes["mathvariant"] = "bold";

        Assert.Equal("x", Assert.IsType<MathText>(identifier.Children.Single()).Value);
        Assert.Equal("italic", identifier.Attributes["mathvariant"]);
        Assert.Equal("x", expression.AlternativeText);
        Assert.Throws<ArgumentException>(() => new MathElement("script"));
        Assert.Throws<ArgumentException>(() => new MathElement(
            "mi",
            attributes: [KeyValuePair.Create("href", "https://example.invalid")]));
    }

    [Fact]
    public async Task FlowJson_RoundTripPreservesBlockAndInlineMath()
    {
        var inline = new InlineMath(new MathElement("math", [new MathElement("mi", [new MathText("x")])]));
        var block = new MathExpression(
            new NodeId("equation-block"),
            new MathElement("math", [new MathElement("mfrac", [new MathText("1"), new MathText("2")])]),
            "one half");
        var document = new FlowDocument(
            new DocumentIdentity(new DocumentId("urn:flow:test:math-document"), "1"),
            new DocumentMetadata("Math test", "en"),
            new DocumentContent(
            [
                new Chapter(new NodeId("chapter"),
                [
                    new Paragraph(new NodeId("paragraph"), [new Text("Value "), inline]),
                    block,
                ]),
            ]));
        var serializer = new FlowJsonDocumentSerializer();
        await using var json = new MemoryStream();

        await serializer.SerializeAsync(document, json);
        json.Position = 0;
        var restored = await serializer.DeserializeAsync(json);

        var restoredBlock = Assert.Single(restored.Index.Locations.Select(static item => item.Node).OfType<MathExpression>());
        Assert.Equal(block.Id, restoredBlock.Id);
        Assert.Equal(block.AlternativeText, restoredBlock.AlternativeText);
        Assert.Equal("12", PlainText(restoredBlock.Root));
        var restoredInline = Assert.Single(restored.Index.Locations.Select(static item => item.Node)
            .OfType<Paragraph>().SelectMany(static paragraph => paragraph.Content).OfType<InlineMath>());
        Assert.Equal("x", PlainText(restoredInline.Root));
    }

    private static string PlainText(MathNode node) => node switch
    {
        MathText text => text.Value,
        MathElement element => string.Concat(element.Children.Select(PlainText)),
        _ => string.Empty,
    };
}
