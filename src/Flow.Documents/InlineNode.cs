using System.Collections.Immutable;

namespace Flow.Documents;

public abstract record InlineNode;

public abstract record InlineContainerNode : InlineNode
{
    protected InlineContainerNode(IEnumerable<InlineNode> children)
    {
        Children = ImmutableCollections.CopyOf(children, nameof(children));
    }

    public ImmutableArray<InlineNode> Children { get; }
}
