using System.Collections.Immutable;

namespace Flow.Documents;

public sealed record DocumentContent
{
    public DocumentContent(IEnumerable<DocumentNode> children)
    {
        Children = ImmutableCollections.CopyOf(children, nameof(children));
    }

    public ImmutableArray<DocumentNode> Children { get; }
}
