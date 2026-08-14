using Flow.Core;

namespace Flow.Documents;

public sealed record Text : InlineNode
{
    public Text(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    public string Value { get; }
}

public sealed record Strong : InlineContainerNode
{
    public Strong(IEnumerable<InlineNode> children)
        : base(children)
    {
    }
}

public sealed record Emphasis : InlineContainerNode
{
    public Emphasis(IEnumerable<InlineNode> children)
        : base(children)
    {
    }
}

public sealed record Underline : InlineContainerNode
{
    public Underline(IEnumerable<InlineNode> children)
        : base(children)
    {
    }
}

public sealed record Strikethrough : InlineContainerNode
{
    public Strikethrough(IEnumerable<InlineNode> children)
        : base(children)
    {
    }
}

public sealed record InlineCode : InlineNode
{
    public InlineCode(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        Code = code;
    }

    public string Code { get; }
}

public sealed record Link : InlineContainerNode
{
    public Link(string target, IEnumerable<InlineNode> children)
        : base(children)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        Target = target;
    }

    public string Target { get; }
}

public sealed record FootnoteReference : InlineNode
{
    public FootnoteReference(NodeId targetId)
    {
        ArgumentNullException.ThrowIfNull(targetId);
        TargetId = targetId;
    }

    public NodeId TargetId { get; }
}

public sealed record LineBreak : InlineNode;
