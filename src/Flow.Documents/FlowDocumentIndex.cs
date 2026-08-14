using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Flow.Core;

namespace Flow.Documents;

public sealed class FlowDocumentIndex
{
    private readonly ImmutableDictionary<string, ImmutableArray<DocumentNodeLocation>> _locationsByAnchor;
    private readonly ImmutableDictionary<NodeId, ImmutableArray<DocumentNodeLocation>> _locationsById;
    private readonly ImmutableArray<DocumentNodeLocation> _locationsInReadingOrder;

    internal FlowDocumentIndex(DocumentContent content)
    {
        var byAnchor = new Dictionary<string, List<DocumentNodeLocation>>(StringComparer.Ordinal);
        var byId = new Dictionary<NodeId, List<DocumentNodeLocation>>();
        var inReadingOrder = ImmutableArray.CreateBuilder<DocumentNodeLocation>();

        foreach (var node in content.Children)
        {
            IndexNode(node, [], null, byAnchor, byId, inReadingOrder);
        }

        _locationsByAnchor = byAnchor.ToImmutableDictionary(
            static pair => pair.Key,
            static pair => pair.Value.ToImmutableArray(),
            StringComparer.Ordinal);
        _locationsById = byId.ToImmutableDictionary(
            static pair => pair.Key,
            static pair => pair.Value.ToImmutableArray());
        _locationsInReadingOrder = inReadingOrder.ToImmutable();
    }

    public int NodeCount => _locationsById.Sum(static pair => pair.Value.Length);

    public IEnumerable<NodeId> DuplicateIds =>
        _locationsById
            .Where(static pair => pair.Value.Length > 1)
            .Select(static pair => pair.Key)
            .OrderBy(static id => id.Value, StringComparer.Ordinal);

    public ImmutableArray<DocumentNodeLocation> Locations => _locationsInReadingOrder;

    public bool TryGetUniqueNode(NodeId id, [NotNullWhen(true)] out DocumentNode? node)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (_locationsById.TryGetValue(id, out var locations) && locations.Length == 1)
        {
            node = locations[0].Node;
            return true;
        }

        node = null;
        return false;
    }

    public bool TryResolve(DocumentAnchor anchor, [NotNullWhen(true)] out DocumentNode? node)
    {
        ArgumentNullException.ThrowIfNull(anchor);

        if (_locationsByAnchor.TryGetValue(anchor.Value, out var exactLocations) && exactLocations.Length == 1)
        {
            node = exactLocations[0].Node;
            return true;
        }

        if (anchor.Segments.Length == 1)
        {
            return TryGetUniqueNode(anchor.TargetId, out node);
        }

        node = null;
        return false;
    }

    public ImmutableArray<DocumentNodeLocation> GetLocations(NodeId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _locationsById.TryGetValue(id, out var locations) ? locations : [];
    }

    private static void IndexNode(
        DocumentNode node,
        ImmutableArray<NodeId> parentSegments,
        DocumentNode? parent,
        Dictionary<string, List<DocumentNodeLocation>> byAnchor,
        Dictionary<NodeId, List<DocumentNodeLocation>> byId,
        ImmutableArray<DocumentNodeLocation>.Builder inReadingOrder)
    {
        var segments = parentSegments.Add(node.Id);
        var anchor = DocumentAnchor.Create(segments);
        var location = new DocumentNodeLocation(node, anchor, parent);

        Add(byAnchor, anchor.Value, location);
        Add(byId, node.Id, location);
        inReadingOrder.Add(location);

        foreach (var child in DocumentNodeTraversal.GetChildren(node))
        {
            IndexNode(child, segments, node, byAnchor, byId, inReadingOrder);
        }
    }

    private static void Add<TKey>(
        Dictionary<TKey, List<DocumentNodeLocation>> dictionary,
        TKey key,
        DocumentNodeLocation location)
        where TKey : notnull
    {
        if (!dictionary.TryGetValue(key, out var locations))
        {
            locations = [];
            dictionary.Add(key, locations);
        }

        locations.Add(location);
    }
}
