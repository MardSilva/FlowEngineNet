using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Flow.Core;

namespace Flow.Documents;

public sealed record FlowDocument
{
    public FlowDocument(
        DocumentIdentity identity,
        DocumentMetadata metadata,
        DocumentContent content,
        IEnumerable<FlowAsset>? assets = null,
        DocumentPresentation? presentation = null,
        DocumentIntegrity? integrity = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(content);

        Identity = identity;
        Metadata = metadata;
        Content = content;
        Assets = ImmutableCollections.CopyOf(assets ?? [], nameof(assets))
            .ToImmutableDictionary(static asset => asset.Id);
        Presentation = presentation;
        Integrity = integrity;
        Index = new FlowDocumentIndex(content);
    }

    public DocumentIdentity Identity { get; }

    public DocumentMetadata Metadata { get; }

    public DocumentContent Content { get; }

    public ImmutableDictionary<AssetId, FlowAsset> Assets { get; }

    public DocumentPresentation? Presentation { get; }

    public DocumentIntegrity? Integrity { get; }

    public FlowDocumentIndex Index { get; }

    public bool TryResolveAnchor(DocumentAnchor anchor, [NotNullWhen(true)] out DocumentNode? node) =>
        Index.TryResolve(anchor, out node);

    public DocumentNode ResolveAnchor(DocumentAnchor anchor)
    {
        if (!TryResolveAnchor(anchor, out var node))
        {
            throw new KeyNotFoundException($"The anchor '{anchor}' does not resolve to one unique document node.");
        }

        return node;
    }
}
