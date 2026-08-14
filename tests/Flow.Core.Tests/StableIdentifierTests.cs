using Flow.Core;

namespace Flow.Core.Tests;

public sealed class StableIdentifierTests
{
    [Fact]
    public void NodeId_PreservesValueAndUsesValueEquality()
    {
        var first = new NodeId("chapter-introduction");
        var second = new NodeId("chapter-introduction");

        Assert.Equal(first, second);
        Assert.Equal("chapter-introduction", first.Value);
        Assert.Equal(first.Value, first.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Chapter")]
    [InlineData("1-chapter")]
    [InlineData("chapter/introduction")]
    [InlineData("chapter introduction")]
    public void NodeId_RejectsInvalidValues(string value)
    {
        Assert.Throws<ArgumentException>(() => new NodeId(value));
    }

    [Fact]
    public void NodeId_EnforcesMaximumLengthAndOrdinalCase()
    {
        var maximumLengthId = new NodeId('a' + new string('1', 127));

        Assert.Equal(128, maximumLengthId.Value.Length);
        Assert.NotEqual(new NodeId("chapter"), new NodeId("chapter-2"));
        Assert.Throws<ArgumentException>(() => new NodeId('a' + new string('1', 128)));
        Assert.False(NodeId.TryParse("CHAPTER", out _));
    }

    [Fact]
    public void AssetId_UsesTheSameStableIdentifierRules()
    {
        var assetId = new AssetId("figure-flow-model.svg");

        Assert.Equal("figure-flow-model.svg", assetId.Value);
        Assert.Throws<ArgumentException>(() => new AssetId("Figure 1"));
    }

    [Fact]
    public void DocumentId_AcceptsAnAbsoluteFlowUrn()
    {
        const string value = "urn:flow:document:550e8400-e29b-41d4-a716-446655440000";

        var documentId = new DocumentId(value);

        Assert.Equal(value, documentId.Value);
        Assert.Equal(value, documentId.ToString());
    }

    [Theory]
    [InlineData("document-1")]
    [InlineData(" urn:flow:document:1")]
    [InlineData("urn:flow:document:1 ")]
    public void DocumentId_RejectsNonAbsoluteOrUntrimmedValues(string value)
    {
        Assert.Throws<ArgumentException>(() => new DocumentId(value));
    }

    [Fact]
    public void DocumentAnchor_ParsesHierarchicalStableIds()
    {
        var anchor = DocumentAnchor.Parse("flow:chapter-introduction/section-identity/p-002");

        Assert.Equal(3, anchor.Segments.Length);
        Assert.Equal(new NodeId("p-002"), anchor.TargetId);
        Assert.Equal("flow:chapter-introduction/section-identity/p-002", anchor.Value);
        Assert.Equal(anchor, DocumentAnchor.Parse(anchor.Value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("flow:")]
    [InlineData("FLOW:chapter")]
    [InlineData("flow:/chapter")]
    [InlineData("flow:chapter/")]
    [InlineData("flow:chapter//paragraph")]
    [InlineData("flow:Chapter")]
    [InlineData("https://example.org/chapter")]
    public void DocumentAnchor_TryParseRejectsInvalidValues(string? value)
    {
        Assert.False(DocumentAnchor.TryParse(value, out var anchor));
        Assert.Null(anchor);
    }

    [Fact]
    public void DocumentAnchor_ParseReportsMalformedInput()
    {
        Assert.Throws<FormatException>(() => DocumentAnchor.Parse("flow:chapter/P-001"));
        Assert.Throws<ArgumentNullException>(() => DocumentAnchor.Parse(null!));
    }
}
