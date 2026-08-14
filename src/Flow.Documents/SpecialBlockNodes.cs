using System.Collections.Immutable;
using Flow.Core;

namespace Flow.Documents;

public sealed record Footnote : BlockContainerNode
{
    public Footnote(NodeId id, IEnumerable<DocumentNode> children)
        : base(id, children)
    {
    }
}

public sealed record HorizontalRule : DocumentNode
{
    public HorizontalRule(NodeId id)
        : base(id)
    {
    }
}

public sealed record TableOfContents : DocumentNode
{
    public TableOfContents(NodeId id, IEnumerable<InlineNode> title, int maximumDepth = 3)
        : this(id, title, [], maximumDepth)
    {
    }

    public TableOfContents(
        NodeId id,
        IEnumerable<InlineNode> title,
        IEnumerable<TableOfContentsEntry> entries,
        int maximumDepth = 3)
        : base(id)
    {
        if (maximumDepth is < 1 or > 6)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumDepth),
                maximumDepth,
                "A table of contents depth must be between 1 and 6.");
        }

        Title = ImmutableCollections.CopyOf(title, nameof(title));
        Entries = ImmutableCollections.CopyOf(entries, nameof(entries));
        MaximumDepth = maximumDepth;
    }

    public ImmutableArray<InlineNode> Title { get; }

    public ImmutableArray<TableOfContentsEntry> Entries { get; }

    public int MaximumDepth { get; }
}
