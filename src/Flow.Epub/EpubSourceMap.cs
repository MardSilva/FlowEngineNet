using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Flow.Core;

namespace Flow.Epub;

/// <summary>Associates one normalized EPUB resource/fragment occurrence with a semantic Flow node.</summary>
public sealed record EpubSourceLocation
{
    public EpubSourceLocation(string resourcePath, string? fragment, NodeId nodeId, int occurrence = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourcePath);
        ArgumentNullException.ThrowIfNull(nodeId);
        if (fragment is not null && fragment.Length == 0)
        {
            throw new ArgumentException("A source fragment cannot be empty.", nameof(fragment));
        }

        if (occurrence < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(occurrence), occurrence, "An occurrence must be positive.");
        }

        ResourcePath = resourcePath;
        Fragment = fragment;
        NodeId = nodeId;
        Occurrence = occurrence;
    }

    public string ResourcePath { get; }

    public string? Fragment { get; }

    public NodeId NodeId { get; }

    public int Occurrence { get; }
}

/// <summary>Provides deterministic, noncanonical traceability from EPUB references to Flow node IDs.</summary>
public sealed record EpubSourceMap
{
    private readonly ImmutableDictionary<SourceKey, NodeId> primaryIndex;
    private readonly ImmutableDictionary<NodeId, ImmutableArray<EpubSourceLocation>> reverseIndex;

    public EpubSourceMap(IEnumerable<EpubSourceLocation> locations)
    {
        ArgumentNullException.ThrowIfNull(locations);
        Locations = locations.ToImmutableArray();
        var index = ImmutableDictionary.CreateBuilder<SourceKey, NodeId>();
        foreach (var location in Locations)
        {
            index.TryAdd(new SourceKey(location.ResourcePath, location.Fragment), location.NodeId);
        }

        primaryIndex = index.ToImmutable();
        reverseIndex = Locations
            .GroupBy(static location => location.NodeId)
            .ToImmutableDictionary(
                static group => group.Key,
                static group => group.ToImmutableArray());
    }

    public ImmutableArray<EpubSourceLocation> Locations { get; }

    /// <summary>Gets every source occurrence associated with a semantic node, in discovery order.</summary>
    public ImmutableArray<EpubSourceLocation> GetLocations(NodeId nodeId)
    {
        ArgumentNullException.ThrowIfNull(nodeId);
        return reverseIndex.TryGetValue(nodeId, out var locations)
            ? locations
            : [];
    }

    /// <summary>Resolves an exact normalized resource path and optional decoded fragment.</summary>
    public bool TryResolve(
        string resourcePath,
        string? fragment,
        [NotNullWhen(true)] out NodeId? nodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourcePath);
        var decodedFragment = DecodeFragment(fragment);
        if (fragment is not null && decodedFragment is null)
        {
            nodeId = null;
            return false;
        }

        return primaryIndex.TryGetValue(new SourceKey(resourcePath, decodedFragment), out nodeId);
    }

    /// <summary>Resolves a safe relative EPUB link against its containing normalized resource path.</summary>
    public bool TryResolveReference(
        string sourceResourcePath,
        string reference,
        [NotNullWhen(true)] out NodeId? nodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceResourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        nodeId = null;
        if (Uri.TryCreate(reference, UriKind.Absolute, out _))
        {
            return false;
        }

        var parts = reference.Split('#', 2);
        string targetPath;
        if (parts[0].Length == 0)
        {
            targetPath = sourceResourcePath;
        }
        else if (!EpubArchiveUtilities.TryNormalizeArchivePath(
                     EpubArchiveUtilities.GetDirectory(sourceResourcePath),
                     parts[0],
                     out targetPath))
        {
            return false;
        }

        return TryResolve(targetPath, parts.Length == 2 ? parts[1] : null, out nodeId);
    }

    private static string? DecodeFragment(string? fragment)
    {
        if (fragment is null)
        {
            return null;
        }

        try
        {
            var decoded = Uri.UnescapeDataString(fragment);
            return decoded.Length == 0 || decoded.Any(char.IsControl) ? null : decoded;
        }
        catch (UriFormatException)
        {
            return null;
        }
    }

    private readonly record struct SourceKey(string ResourcePath, string? Fragment);
}
