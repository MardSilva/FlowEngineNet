using System.Collections.Immutable;
using Flow.Core;

namespace Flow.Layout;

public sealed record LayoutDocument
{
    public LayoutDocument(
        DocumentId documentId,
        string? documentVersion,
        ReadingMode readingMode,
        LayoutProfile profile,
        ResolvedReadingStyle readingStyle,
        IEnumerable<LayoutNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(documentId);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(readingStyle);
        ArgumentNullException.ThrowIfNull(nodes);

        if (!Enum.IsDefined(readingMode))
        {
            throw new ArgumentOutOfRangeException(nameof(readingMode), readingMode, "The reading mode is not defined.");
        }

        DocumentId = documentId;
        DocumentVersion = documentVersion;
        ReadingMode = readingMode;
        Profile = profile;
        ReadingStyle = readingStyle;
        Nodes = nodes.ToImmutableArray();

        if (Nodes.Any(static node => node is null))
        {
            throw new ArgumentException("Layout nodes cannot contain null values.", nameof(nodes));
        }
    }

    public DocumentId DocumentId { get; }

    public string? DocumentVersion { get; }

    public ReadingMode ReadingMode { get; }

    public LayoutProfile Profile { get; }

    public ResolvedReadingStyle ReadingStyle { get; }

    public ImmutableArray<LayoutNode> Nodes { get; }
}
