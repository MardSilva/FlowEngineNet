using System.Collections.Immutable;

namespace Flow.Epub;

/// <summary>Describes one normalized OPF manifest item and its archive availability.</summary>
public sealed record EpubManifestItemInfo
{
    public EpubManifestItemInfo(
        string id,
        string declaredHref,
        string path,
        string mediaType,
        IEnumerable<string> properties,
        bool existsInArchive,
        bool isNavigationDocument,
        bool isSupported,
        string? fallbackId = null,
        string? mediaOverlayId = null,
        long compressedBytes = 0,
        long uncompressedBytes = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(declaredHref);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentOutOfRangeException.ThrowIfNegative(compressedBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(uncompressedBytes);

        Id = id;
        DeclaredHref = declaredHref;
        Path = path;
        MediaType = mediaType;
        Properties = properties.Order(StringComparer.Ordinal).ToImmutableArray();
        ExistsInArchive = existsInArchive;
        IsNavigationDocument = isNavigationDocument;
        IsSupported = isSupported;
        FallbackId = fallbackId;
        MediaOverlayId = mediaOverlayId;
        CompressedBytes = compressedBytes;
        UncompressedBytes = uncompressedBytes;
    }

    public string Id { get; }

    public string DeclaredHref { get; }

    public string Path { get; }

    public string MediaType { get; }

    public ImmutableArray<string> Properties { get; }

    public bool ExistsInArchive { get; }

    public bool IsNavigationDocument { get; }

    public bool IsSupported { get; }

    public string? FallbackId { get; }

    public string? MediaOverlayId { get; }

    /// <summary>Gets the compressed archive bytes when the resource exists.</summary>
    public long CompressedBytes { get; }

    /// <summary>Gets the uncompressed archive bytes when the resource exists.</summary>
    public long UncompressedBytes { get; }
}
