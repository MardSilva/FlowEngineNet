using System.Collections.Immutable;
using Flow.Core;

namespace Flow.Documents;

public sealed record Heading : DocumentNode
{
    public Heading(NodeId id, int level, IEnumerable<InlineNode> content)
        : base(id)
    {
        if (level is < 1 or > 6)
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, "A heading level must be between 1 and 6.");
        }

        Level = level;
        Content = ImmutableCollections.CopyOf(content, nameof(content));
    }

    public int Level { get; }

    public ImmutableArray<InlineNode> Content { get; }
}

public sealed record Paragraph : DocumentNode
{
    public Paragraph(NodeId id, IEnumerable<InlineNode> content)
        : base(id)
    {
        Content = ImmutableCollections.CopyOf(content, nameof(content));
    }

    public ImmutableArray<InlineNode> Content { get; }
}

public sealed record BlockQuote : BlockContainerNode
{
    public BlockQuote(NodeId id, IEnumerable<DocumentNode> children)
        : base(id, children)
    {
    }
}

public sealed record CodeBlock : DocumentNode
{
    public CodeBlock(NodeId id, string code, string? language = null)
        : base(id)
    {
        ArgumentNullException.ThrowIfNull(code);

        if (language is not null && string.IsNullOrWhiteSpace(language))
        {
            throw new ArgumentException("A code language cannot be empty or whitespace.", nameof(language));
        }

        Code = code;
        Language = language;
    }

    public string Code { get; }

    public string? Language { get; }
}
