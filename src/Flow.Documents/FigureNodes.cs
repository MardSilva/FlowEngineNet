using System.Collections.Immutable;
using Flow.Core;

namespace Flow.Documents;

public sealed record Figure : DocumentNode
{
    public Figure(NodeId id, AssetId assetId, Caption? caption = null, string? alternativeText = null)
        : base(id)
    {
        ArgumentNullException.ThrowIfNull(assetId);

        AssetId = assetId;
        Caption = caption;
        AlternativeText = alternativeText;
    }

    public AssetId AssetId { get; }

    public Caption? Caption { get; }

    public string? AlternativeText { get; }
}

public sealed record Caption : DocumentNode
{
    public Caption(NodeId id, IEnumerable<InlineNode> content)
        : base(id)
    {
        Content = ImmutableCollections.CopyOf(content, nameof(content));
    }

    public ImmutableArray<InlineNode> Content { get; }
}
