using Flow.Core;

namespace Flow.Documents;

public sealed record Chapter : BlockContainerNode
{
    public Chapter(NodeId id, IEnumerable<DocumentNode> children)
        : base(id, children)
    {
    }
}

public sealed record Section : BlockContainerNode
{
    public Section(NodeId id, IEnumerable<DocumentNode> children)
        : base(id, children)
    {
    }
}
