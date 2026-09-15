using System.Collections.Immutable;

namespace Flow.Epub;

/// <summary>Classifies the result of processing one declared spine position.</summary>
public enum EpubSpineDisposition
{
    Included,
    Substituted,
    Excluded,
}

/// <summary>Provides a machine-readable reason for a spine processing decision.</summary>
public enum EpubSpineDecisionReason
{
    DirectXhtml,
    XhtmlFallback,
    MissingManifestItem,
    MissingArchiveResource,
    UnsupportedMediaType,
    BrokenFallback,
    CircularFallback,
    InvalidXhtml,
}

/// <summary>Distinguishes primary reading-order content from explicitly supplementary content.</summary>
public enum EpubSpineReadingRole
{
    Linear,
    Supplemental,
}

/// <summary>Preserves the manifest declarations that affect resource resolution.</summary>
public sealed record EpubManifestResourceDecision
{
    public EpubManifestResourceDecision(
        string id,
        string path,
        string mediaType,
        IEnumerable<string> properties,
        string? fallbackId,
        string? mediaOverlayId,
        bool existsInArchive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        ArgumentNullException.ThrowIfNull(properties);

        Id = id;
        Path = path;
        MediaType = mediaType;
        Properties = properties.Order(StringComparer.Ordinal).ToImmutableArray();
        FallbackId = fallbackId;
        MediaOverlayId = mediaOverlayId;
        ExistsInArchive = existsInArchive;
    }

    public string Id { get; }

    public string Path { get; }

    public string MediaType { get; }

    public ImmutableArray<string> Properties { get; }

    public string? FallbackId { get; }

    public string? MediaOverlayId { get; }

    public bool ExistsInArchive { get; }
}

/// <summary>Explains how one OPF spine position contributed to imported reading content.</summary>
public sealed record EpubSpineProcessingDecision(
    int Position,
    string? RequestedItemId,
    EpubSpineReadingRole ReadingRole,
    bool IsRepeatedReference,
    EpubSpineDisposition Disposition,
    EpubSpineDecisionReason Reason,
    string? SelectedItemId,
    string? SelectedResourcePath,
    ImmutableArray<string> FallbackChain);

/// <summary>Contains noncanonical manifest and spine processing evidence for an EPUB import.</summary>
public sealed record EpubPackageProcessingReport
{
    public EpubPackageProcessingReport(
        IEnumerable<EpubManifestResourceDecision> manifest,
        IEnumerable<EpubSpineProcessingDecision> spine)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(spine);
        Manifest = manifest.ToImmutableArray();
        Spine = spine.OrderBy(static item => item.Position).ToImmutableArray();
    }

    public ImmutableArray<EpubManifestResourceDecision> Manifest { get; }

    public ImmutableArray<EpubSpineProcessingDecision> Spine { get; }
}
