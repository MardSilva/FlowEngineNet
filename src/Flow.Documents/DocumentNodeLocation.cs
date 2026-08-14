using Flow.Core;

namespace Flow.Documents;

public sealed record DocumentNodeLocation
{
    internal DocumentNodeLocation(DocumentNode node, DocumentAnchor anchor, DocumentNode? parent)
    {
        Node = node;
        Anchor = anchor;
        Parent = parent;
    }

    public DocumentNode Node { get; }

    public DocumentAnchor Anchor { get; }

    public DocumentNode? Parent { get; }
}
