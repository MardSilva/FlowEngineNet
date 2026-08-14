using System.Collections.Immutable;
using Flow.Core;

namespace Flow.Documents;

public sealed record TableOfContentsEntry
{
    public TableOfContentsEntry(IEnumerable<InlineNode> label, DocumentAnchor target, int level)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (level is < 1 or > 6)
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, "A table of contents level must be between 1 and 6.");
        }

        Label = ImmutableCollections.CopyOf(label, nameof(label));
        Target = target;
        Level = level;
    }

    public ImmutableArray<InlineNode> Label { get; }

    public DocumentAnchor Target { get; }

    public int Level { get; }
}
