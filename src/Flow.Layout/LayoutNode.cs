using System.Collections.Immutable;
using Flow.Core;
using Flow.Documents;

namespace Flow.Layout;

public sealed record LayoutNode
{
    public LayoutNode(
        NodeId semanticId,
        DocumentNode semanticNode,
        NodeLayoutIntent intent,
        IEnumerable<LayoutNode>? children = null,
        ResolvedTypographyStyle? typography = null)
    {
        ArgumentNullException.ThrowIfNull(semanticId);
        ArgumentNullException.ThrowIfNull(semanticNode);
        ArgumentNullException.ThrowIfNull(intent);

        if (semanticId != semanticNode.Id)
        {
            throw new ArgumentException("The layout node ID must match its semantic node ID.", nameof(semanticId));
        }

        SemanticId = semanticId;
        SemanticNode = semanticNode;
        Intent = intent;
        Children = (children ?? []).ToImmutableArray();
        Typography = typography;

        if (Children.Any(static child => child is null))
        {
            throw new ArgumentException("Layout children cannot contain null values.", nameof(children));
        }
    }

    public NodeId SemanticId { get; }

    public DocumentNode SemanticNode { get; }

    public NodeLayoutIntent Intent { get; }

    public ImmutableArray<LayoutNode> Children { get; }

    public ResolvedTypographyStyle? Typography { get; }
}
