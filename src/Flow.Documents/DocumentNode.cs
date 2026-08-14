using System.Collections.Immutable;
using Flow.Core;

namespace Flow.Documents;

public abstract record DocumentNode
{
    protected DocumentNode(NodeId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        Id = id;
    }

    public NodeId Id { get; }
}

public abstract record BlockContainerNode : DocumentNode
{
    protected BlockContainerNode(NodeId id, IEnumerable<DocumentNode> children)
        : base(id)
    {
        Children = ImmutableCollections.CopyOf(children, nameof(children));
    }

    public ImmutableArray<DocumentNode> Children { get; }
}
