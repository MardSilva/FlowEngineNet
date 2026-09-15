using System.Collections.Immutable;

namespace Flow.Epub;

/// <summary>Summarizes archive and manifest resources without reading publication content.</summary>
public sealed record EpubResourceSummary
{
    public EpubResourceSummary(
        int archiveEntryCount,
        int manifestItemCount,
        int existingManifestItemCount,
        int missingManifestItemCount,
        int unsupportedManifestItemCount,
        int navigationDocumentCount,
        long totalCompressedBytes,
        long totalUncompressedBytes,
        IEnumerable<KeyValuePair<string, int>> mediaTypeCounts)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(archiveEntryCount);
        ArgumentOutOfRangeException.ThrowIfNegative(manifestItemCount);
        ArgumentOutOfRangeException.ThrowIfNegative(existingManifestItemCount);
        ArgumentOutOfRangeException.ThrowIfNegative(missingManifestItemCount);
        ArgumentOutOfRangeException.ThrowIfNegative(unsupportedManifestItemCount);
        ArgumentOutOfRangeException.ThrowIfNegative(navigationDocumentCount);
        ArgumentOutOfRangeException.ThrowIfNegative(totalCompressedBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(totalUncompressedBytes);
        ArgumentNullException.ThrowIfNull(mediaTypeCounts);

        ArchiveEntryCount = archiveEntryCount;
        ManifestItemCount = manifestItemCount;
        ExistingManifestItemCount = existingManifestItemCount;
        MissingManifestItemCount = missingManifestItemCount;
        UnsupportedManifestItemCount = unsupportedManifestItemCount;
        NavigationDocumentCount = navigationDocumentCount;
        TotalCompressedBytes = totalCompressedBytes;
        TotalUncompressedBytes = totalUncompressedBytes;
        MediaTypeCounts = mediaTypeCounts.ToImmutableSortedDictionary(StringComparer.Ordinal);
    }

    public int ArchiveEntryCount { get; }

    public int ManifestItemCount { get; }

    public int ExistingManifestItemCount { get; }

    public int MissingManifestItemCount { get; }

    public int UnsupportedManifestItemCount { get; }

    public int NavigationDocumentCount { get; }

    public long TotalCompressedBytes { get; }

    public long TotalUncompressedBytes { get; }

    public ImmutableSortedDictionary<string, int> MediaTypeCounts { get; }

    public static EpubResourceSummary Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, []);
}
