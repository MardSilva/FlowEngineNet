using System.Collections.Immutable;
using Flow.Epub;

namespace Flow.Epub.Corpus;

/// <summary>Classifies whether one neutral EPUB candidate can proceed to later Flow qualification.</summary>
public enum EpubPrivateInventoryStatus
{
    Ready,
    ReviewRequired,
    Protected,
    Corrupt,
    Unsuitable,
}

/// <summary>Summarizes package protection evidence without claiming that every rights file is DRM.</summary>
public enum EpubPrivateInventoryProtectionStatus
{
    None,
    FontObfuscationOnly,
    RightsMetadataPresent,
    UnsupportedEncryption,
}

/// <summary>Identifies the severity of a path-free inventory finding.</summary>
public enum EpubPrivateInventoryDiagnosticSeverity
{
    Information,
    Warning,
    Error,
}

/// <summary>Defines stable inventory-specific diagnostic codes.</summary>
public static class EpubPrivateInventoryDiagnosticCodes
{
    public const string SearchLimitExceeded = "EPI001";
    public const string UnsafeFileSystemEntry = "EPI002";
    public const string CandidateUnreadable = "EPI003";
    public const string ArchiveLimitExceeded = "EPI004";
    public const string InspectionFailed = "EPI005";
    public const string UnknownEpubVersion = "EPI006";
    public const string MissingReadingOrder = "EPI007";
    public const string UnsupportedEncryption = "EPI008";
    public const string RightsMetadataPresent = "EPI009";
    public const string FontObfuscationPresent = "EPI010";
    public const string InvalidProtectionMetadata = "EPI011";
    public const string DuplicateContent = "EPI012";
    public const string UnsupportedResources = "EPI013";
}

/// <summary>Contains one stable finding without a physical path, title, identifier, or source text.</summary>
public sealed record EpubPrivateInventoryDiagnostic(
    string Code,
    EpubPrivateInventoryDiagnosticSeverity Severity);

/// <summary>Contains neutral resource counts for one distinct EPUB payload.</summary>
public sealed record EpubPrivateInventoryResourceCounts(
    int ArchiveEntries,
    int ManifestItems,
    int Xhtml,
    int Css,
    int RasterImages,
    int Svg,
    int Fonts,
    int Audio,
    int Other)
{
    public static EpubPrivateInventoryResourceCounts Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0);
}

/// <summary>Describes one distinct local EPUB without retaining its path or editorial identity.</summary>
public sealed record EpubPrivateInventoryItem
{
    public EpubPrivateInventoryItem(
        EpubCorpusPublicationId id,
        EpubCorpusSha256? sha256,
        EpubPrivateInventoryStatus status,
        EpubPrivateInventoryProtectionStatus protection,
        int copyCount,
        long fileBytes,
        EpubVersionFamily epubVersion,
        IEnumerable<string> languages,
        int spineItemCount,
        int linearSpineItemCount,
        int nonLinearSpineItemCount,
        EpubPrivateInventoryResourceCounts resources,
        IEnumerable<EpubPrivateInventoryDiagnostic> diagnostics)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(copyCount);
        ArgumentOutOfRangeException.ThrowIfNegative(fileBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(spineItemCount);
        ArgumentOutOfRangeException.ThrowIfNegative(linearSpineItemCount);
        ArgumentOutOfRangeException.ThrowIfNegative(nonLinearSpineItemCount);
        ArgumentNullException.ThrowIfNull(languages);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(diagnostics);

        Id = id;
        Sha256 = sha256;
        Status = status;
        Protection = protection;
        CopyCount = copyCount;
        FileBytes = fileBytes;
        EpubVersion = epubVersion;
        Languages = languages
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();
        SpineItemCount = spineItemCount;
        LinearSpineItemCount = linearSpineItemCount;
        NonLinearSpineItemCount = nonLinearSpineItemCount;
        Resources = resources;
        Diagnostics = diagnostics
            .Distinct()
            .OrderBy(static item => item.Code, StringComparer.Ordinal)
            .ThenBy(static item => item.Severity)
            .ToImmutableArray();
    }

    public EpubCorpusPublicationId Id { get; }

    public EpubCorpusSha256? Sha256 { get; }

    public EpubPrivateInventoryStatus Status { get; }

    public EpubPrivateInventoryProtectionStatus Protection { get; }

    public int CopyCount { get; }

    public long FileBytes { get; }

    public EpubVersionFamily EpubVersion { get; }

    public ImmutableArray<string> Languages { get; }

    public int SpineItemCount { get; }

    public int LinearSpineItemCount { get; }

    public int NonLinearSpineItemCount { get; }

    public EpubPrivateInventoryResourceCounts Resources { get; }

    public ImmutableArray<EpubPrivateInventoryDiagnostic> Diagnostics { get; }
}

/// <summary>Summarizes one deterministic private inventory run.</summary>
public sealed record EpubPrivateInventorySummary(
    int DiscoveredFiles,
    int DistinctPublications,
    int Ready,
    int ReviewRequired,
    int Protected,
    int Corrupt,
    int Unsuitable);

/// <summary>Contains a path-free and metadata-free inventory suitable for local evidence.</summary>
public sealed record EpubPrivateInventoryReport
{
    public const string CurrentFormat = "flow-epub-private-inventory-0.1";

    public EpubPrivateInventoryReport(
        int discoveredFiles,
        IEnumerable<EpubPrivateInventoryItem> publications,
        IEnumerable<EpubPrivateInventoryDiagnostic>? diagnostics = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(discoveredFiles);
        ArgumentNullException.ThrowIfNull(publications);
        Publications = publications.OrderBy(static item => item.Id.Value, StringComparer.Ordinal).ToImmutableArray();
        Diagnostics = (diagnostics ?? [])
            .Distinct()
            .OrderBy(static item => item.Code, StringComparer.Ordinal)
            .ThenBy(static item => item.Severity)
            .ToImmutableArray();
        Summary = new EpubPrivateInventorySummary(
            discoveredFiles,
            Publications.Length,
            Publications.Count(static item => item.Status == EpubPrivateInventoryStatus.Ready),
            Publications.Count(static item => item.Status == EpubPrivateInventoryStatus.ReviewRequired),
            Publications.Count(static item => item.Status == EpubPrivateInventoryStatus.Protected),
            Publications.Count(static item => item.Status == EpubPrivateInventoryStatus.Corrupt),
            Publications.Count(static item => item.Status == EpubPrivateInventoryStatus.Unsuitable));
    }

    public EpubPrivateInventorySummary Summary { get; }

    public ImmutableArray<EpubPrivateInventoryItem> Publications { get; }

    public ImmutableArray<EpubPrivateInventoryDiagnostic> Diagnostics { get; }
}

/// <summary>Defines bounded local discovery without persisting physical paths.</summary>
public sealed record EpubPrivateInventoryOptions
{
    public EpubPrivateInventoryOptions(int maximumCandidateFiles = 4_096, int maximumRecursionDepth = 32)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCandidateFiles);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumRecursionDepth);
        MaximumCandidateFiles = maximumCandidateFiles;
        MaximumRecursionDepth = maximumRecursionDepth;
    }

    public int MaximumCandidateFiles { get; }

    public int MaximumRecursionDepth { get; }
}

/// <summary>Builds a neutral inventory from one explicitly supplied local directory.</summary>
public interface IEpubPrivateInventoryService
{
    public Task<EpubPrivateInventoryReport> InventoryAsync(
        string sourceDirectory,
        EpubPrivateInventoryOptions? options = null,
        CancellationToken cancellationToken = default);
}
