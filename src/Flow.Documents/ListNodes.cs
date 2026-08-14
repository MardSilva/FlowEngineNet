using System.Collections.Immutable;
using Flow.Core;

namespace Flow.Documents;

public sealed record OrderedList : DocumentNode
{
    public OrderedList(NodeId id, IEnumerable<ListItem> items, int start = 1)
        : base(id)
    {
        if (start < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(start), start, "An ordered list must start at one or greater.");
        }

        Items = ImmutableCollections.CopyOf(items, nameof(items));
        Start = start;
    }

    public ImmutableArray<ListItem> Items { get; }

    public int Start { get; }
}

public sealed record UnorderedList : DocumentNode
{
    public UnorderedList(NodeId id, IEnumerable<ListItem> items)
        : base(id)
    {
        Items = ImmutableCollections.CopyOf(items, nameof(items));
    }

    public ImmutableArray<ListItem> Items { get; }
}

public sealed record ListItem : BlockContainerNode
{
    public ListItem(NodeId id, IEnumerable<DocumentNode> children)
        : base(id, children)
    {
    }
}
